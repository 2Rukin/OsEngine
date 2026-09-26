/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OsEngine.Entity;
using OsEngine.Logging;
using OsEngine.Market;
using OsEngine.Market.Servers;
using OsEngine.Market.Servers.Tester;
using OsEngine.Market.Servers.Optimizer;
using OsEngine.OsTrader.Panels.Tab;

namespace OsEngine.OsTrader.AdaptivePositionManager
{
    /// <summary>
    /// Native Tester/Optimizer adapter for one campaign on a dedicated BotTabSimple.
    /// Uses safe tab methods and a separate remaining-inventory ledger; no account-wide operations.
    /// </summary>
    /// <remarks>
    /// Live is rejected until broker query, external protection, fee and recovery capabilities are qualified.
    /// Native market fills/clock differences remain those of the selected server; no live parity is claimed.
    /// Own callbacks during Send are copied and buffered until new order identities are bound.
    /// The controller monitor owns all mutable adapter state. Contract: APM-INTEGRATION-001.
    /// </remarks>
    public sealed class ApmOsEngineAdapter : IApmOrderGateway, IDisposable
    {
        private readonly BotTabSimple _tab;
        private readonly ApmFeatures _features;
        private readonly Dictionary<int, string> _orderIntents = new Dictionary<int, string>();
        private readonly Dictionary<string, Order> _intentOrders = new Dictionary<string, Order>();
        private readonly Queue<object> _buffer = new Queue<object>();
        private readonly ApmPolicy _policy;
        private readonly StartProgram _mode;
        private Position _position;
        private bool _submitting;
        private bool _disposed;
        private long _sourceSequence;
        private bool _hasPrice;
        private IServer _clockServer;

        /// <summary>Controller used by UI commands and native callbacks; its snapshots contain only copies.</summary>
        public ApmExecutionController Controller { get; }

        /// <summary>Forward diagnostic exceptions through the robot logger; otherwise use ServerMaster.</summary>
        public event Action<string, LogMessageType> LogMessageEvent;

        /// <summary>
        /// Subscribe one isolated campaign. Existing open positions, different instruments/accounts and live
        /// mode are rejected before any subscription or order; metadata is rechecked at submission.
        /// </summary>
        public ApmOsEngineAdapter(BotTabSimple tab, StartProgram mode, ApmCampaignSpec spec, ApmPolicy policy, ApmArtifacts artifacts)
        {
            _tab = tab ?? throw new ArgumentNullException(nameof(tab));
            _mode = mode;
            ValidateSource(mode, _tab.StartProgram, _tab.Connector.MyServer);
            _policy = policy;
            if (_tab.ManualPositionSupport.StopIsOn || _tab.ManualPositionSupport.ProfitIsOn
                || _tab.ManualPositionSupport.DoubleExitIsOn)
                throw new InvalidOperationException("Disable native automatic stop/profit/double-exit support before starting APM.");
            if (_tab.PositionsOpenAll != null && _tab.PositionsOpenAll.Any(p => p.OpenVolume != 0 || p.OpenActive || p.CloseActive))
                throw new InvalidOperationException("APM requires a dedicated flat tab.");
            _features = new ApmFeatures(spec, policy);
            Controller = new ApmExecutionController(new ApmCampaign(spec, policy, new ApmOperationalLimits()), this, artifacts,
                mode == StartProgram.IsTester ? "NativeTester — full volume, no queue" : "NativeOptimizer — full volume, no queue");
            _tab.NewTickEvent += Tab_NewTickEvent;
            _tab.MyTradeEvent += Tab_MyTradeEvent;
            _tab.OrderUpdateEvent += Tab_OrderUpdateEvent;
            AttachClock();
        }

        #region Native source callbacks

        private void AttachClock()
        {
            IServer server = _tab.Connector.MyServer;
            if (ReferenceEquals(server, _clockServer)) return;
            DetachClock();
            _clockServer = server;
            if (_clockServer is TesterServer tester) tester.ReplayTimeAdvancedEvent += Server_TimeServerChangeEvent;
            else if (_clockServer is OptimizerServer optimizer) optimizer.ReplayTimeAdvancedEvent += Server_TimeServerChangeEvent;
        }

        private void DetachClock()
        {
            if (_clockServer is TesterServer tester) tester.ReplayTimeAdvancedEvent -= Server_TimeServerChangeEvent;
            else if (_clockServer is OptimizerServer optimizer) optimizer.ReplayTimeAdvancedEvent -= Server_TimeServerChangeEvent;
        }

        private void Tab_NewTickEvent(Trade trade)
        {
            try
            {
                lock (Controller.SyncRoot)
                {
                    if (_disposed) return;
                    ValidateSource(_mode, _tab.StartProgram, _tab.Connector.MyServer);
                    AttachClock();
                    if (trade.SecurityNameCode != Controller.Spec.Instrument)
                    { Controller.Fault("INSTRUMENT_MISMATCH"); return; }
                    ApmTick tick = new ApmTick("Native", trade.SecurityNameCode,
                        trade.Time.ToString("yyyyMMdd", CultureInfo.InvariantCulture), trade.Time, ++_sourceSequence,
                        trade.Id ?? "", trade.Price, trade.Volume, trade.Side == Side.Buy ? 1 : trade.Side == Side.Sell ? -1 : 0,
                        trade.MicroSeconds);
                    ApmMarket market = _features.OnTick(tick);
                    _hasPrice = true;
                    CheckOwnership();
                    Controller.Process(market);
                }
            }
            catch (Exception error) { HandleError(error); }
        }

        private void Server_TimeServerChangeEvent(DateTime time)
        {
            try
            {
                lock (Controller.SyncRoot)
                {
                    if (_disposed || !_hasPrice) return;
                    ValidateSource(_mode, _tab.StartProgram, _tab.Connector.MyServer);
                    Controller.Process(_features.OnTimer(time, ++_sourceSequence));
                }
            }
            catch (Exception error) { HandleError(error); }
        }

        private void Tab_MyTradeEvent(MyTrade trade)
        {
            try
            {
                lock (Controller.SyncRoot)
                {
                    if (_disposed) return;
                    NativeFill fill = new NativeFill(trade.NumberTrade, trade.NumberOrderParent, trade.SecurityNameCode,
                        trade.Time, trade.Price, trade.Volume, trade.Side);
                    if (_submitting) Enqueue(fill); else Apply(fill);
                }
            }
            catch (Exception error) { HandleError(error); }
        }

        private void Tab_OrderUpdateEvent(Order order)
        {
            try
            {
                lock (Controller.SyncRoot)
                {
                    if (_disposed) return;
                    NativeOrder update = new NativeOrder(order.NumberUser, order.NumberMarket, order.State, order.VolumeExecute);
                    if (_submitting) Enqueue(update); else Apply(update);
                }
            }
            catch (Exception error) { HandleError(error); }
        }

        private void Enqueue(object message)
        {
            if (_buffer.Count >= 4096) throw new InvalidOperationException("APM callback buffer overflow; reconciliation required.");
            _buffer.Enqueue(message);
        }

        private void Apply(NativeOrder order)
        {
            if (!_orderIntents.TryGetValue(order.UserId, out string intentId))
            {
                // Old closed native positions can still publish callbacks on the same tab.
                if (_position != null && AllOrders().Any(o => o.NumberUser == order.UserId)) Controller.Fault("UNOWNED_ORDER");
                return;
            }
            ApmOrderState state = order.State switch
            {
                OrderStateType.Done => ApmOrderState.Filled,
                OrderStateType.Cancel => ApmOrderState.Canceled,
                OrderStateType.Fail => ApmOrderState.Rejected,
                OrderStateType.Active or OrderStateType.Partial or OrderStateType.Pending => ApmOrderState.Working,
                _ => ApmOrderState.Unknown
            };
            Controller.ApplyOrder(intentId, state, order.Filled, order.MarketId);
        }

        private void Apply(NativeFill fill)
        {
            KeyValuePair<string, Order> pair = _intentOrders.FirstOrDefault(p => p.Value.NumberMarket == fill.OrderId);
            if (pair.Key == null) return;
            if (fill.Security != Controller.Spec.Instrument || string.IsNullOrEmpty(fill.Id) || string.IsNullOrEmpty(fill.OrderId))
            { Controller.Fault("UNIDENTIFIED_FILL"); return; }
            Order native = pair.Value;
            if (native.Side != fill.Side) { Controller.Fault("FILL_DIRECTION_MISMATCH"); return; }
            string key = System.Text.Json.JsonSerializer.Serialize(new[] { "Native", Controller.Spec.Instrument,
                fill.Time.ToString("yyyyMMdd", CultureInfo.InvariantCulture), fill.OrderId, fill.Id });
            Controller.ApplyFill(new ApmFill(key, pair.Key, fill.Time, fill.Price, fill.Volume,
                fill.Volume * Controller.Spec.FeePerContract));
        }

        #endregion

        #region Owned commands

        /// <summary>Dispatch one durable intent via BotTabSimple; no Unsafe methods or external transport.</summary>
        public void Send(ApmIntent intent)
        {
            lock (Controller.SyncRoot)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                ValidateNative();
                HashSet<int> before = new HashSet<int>(AllOrders().Select(o => o.NumberUser));
                _submitting = true;
                try
                {
                    bool buy = Controller.Spec.Direction == ApmDirection.Long;
                    if (intent.Action == ApmAction.InitialEntry)
                    {
                        _position = intent.IsLimit
                            ? (buy ? _tab.BuyAtLimit(intent.Volume, intent.PriceBound, intent.Id)
                                : _tab.SellAtLimit(intent.Volume, intent.PriceBound, intent.Id))
                            : (buy ? _tab.BuyAtMarket(intent.Volume, intent.Id) : _tab.SellAtMarket(intent.Volume, intent.Id));
                    }
                    else
                    {
                        if (_position == null) throw new InvalidOperationException("Missing owned native position.");
                        if (intent.Action == ApmAction.Add)
                        {
                            if (intent.IsLimit)
                            {
                                if (buy) _tab.BuyAtLimitToPosition(_position, intent.PriceBound, intent.Volume);
                                else _tab.SellAtLimitToPosition(_position, intent.PriceBound, intent.Volume);
                            }
                            else if (buy) _tab.BuyAtMarketToPosition(_position, intent.Volume);
                            else _tab.SellAtMarketToPosition(_position, intent.Volume);
                        }
                        else
                        {
                            if (intent.Volume > _position.OpenVolume) throw new InvalidOperationException("Native quantity diverged before close.");
                            if (intent.IsLimit) _tab.CloseAtLimit(_position, intent.PriceBound, intent.Volume, intent.Id);
                            else _tab.CloseAtMarket(_position, intent.Volume, intent.Id);
                        }
                    }
                    List<Order> created = AllOrders().Where(o => !before.Contains(o.NumberUser)).ToList();
                    if (created.Count != 1) throw new InvalidOperationException("Native submission did not expose exactly one owned order.");
                    _orderIntents.Add(created[0].NumberUser, intent.Id);
                    _intentOrders.Add(intent.Id, created[0]);
                }
                finally { _submitting = false; }
                while (_buffer.Count > 0)
                {
                    object message = _buffer.Dequeue();
                    if (message is NativeOrder order) Apply(order);
                    else Apply((NativeFill)message);
                }
            }
        }

        /// <summary>Request cancellation only for the exact native order bound to this intent.</summary>
        public void Cancel(ApmIntent intent)
        {
            lock (Controller.SyncRoot)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                ValidateSource(_mode, _tab.StartProgram, _tab.Connector.MyServer);
                if (!_intentOrders.TryGetValue(intent.Id, out Order order))
                    throw new InvalidOperationException("Cannot cancel an unbound native order.");
                _tab.CloseOrder(order);
            }
        }

        private IEnumerable<Order> AllOrders()
        {
            if (_position == null) return Array.Empty<Order>();
            return (_position.OpenOrders ?? new List<Order>()).Concat(_position.CloseOrders ?? new List<Order>());
        }

        private void CheckOwnership()
        {
            List<Position> positions = _tab.PositionsOpenAll;
            if (positions != null && positions.Any(p => !ReferenceEquals(p, _position)
                && (p.OpenVolume != 0 || p.OpenActive || p.CloseActive))) Controller.Fault("FOREIGN_POSITION");
            if (_position != null && !_submitting && _position.OpenVolume != Controller.Snapshot.FilledVolume)
                Controller.Fault("POSITION_LEDGER_MISMATCH");
        }

        private void ValidateNative()
        {
            ValidateSource(_mode, _tab.StartProgram, _tab.Connector.MyServer);
            if (_tab.Security == null || _tab.Portfolio == null || !_tab.Connector.IsConnected || !_tab.Connector.IsReadyToTrade)
                throw new InvalidOperationException("Native tab is not ready.");
            ApmCampaignSpec spec = Controller.Spec;
            if (_tab.Security.Name != spec.Instrument || _tab.Portfolio.Number != spec.Account
                || _tab.Security.PriceStep != spec.PriceStep || _tab.Security.PriceStepCost != spec.PriceStepCost)
                throw new InvalidOperationException("Native instrument/account/valuation does not match immutable schedule.");
            ValidateVolumeStep(_tab.Security, spec);
            if (_position != null && (_position.StopOrderIsActive || _position.ProfitOrderIsActive))
                throw new InvalidOperationException("Independent native protection conflicts with the APM close arbiter.");
            if (_tab.ManualPositionSupport.StopIsOn || _tab.ManualPositionSupport.ProfitIsOn
                || _tab.ManualPositionSupport.DoubleExitIsOn)
                throw new InvalidOperationException("Native automatic protection was enabled during APM ownership.");
        }

        private void HandleError(Exception error)
        {
            try { if (error is not ApmExecutionUncertainException) Controller.Fault("ADAPTER_EXCEPTION"); }
            catch (Exception persistenceError) { ServerMaster.SendNewLogMessage(persistenceError.ToString(), LogMessageType.Error); }
            if (LogMessageEvent != null) LogMessageEvent(error.ToString(), LogMessageType.Error);
            else ServerMaster.SendNewLogMessage(error.ToString(), LogMessageType.Error);
        }

        private static void ValidateSource(StartProgram requested, StartProgram actual, IServer server)
        {
            if (requested != actual || (actual != StartProgram.IsTester && actual != StartProgram.IsOsOptimizer))
                throw new NotSupportedException("APM ResearchOnly requires the actual native research tab mode.");
            TesterDataType data;
            if (actual == StartProgram.IsTester && server is TesterServer tester) data = tester.TypeTesterData;
            else if (actual == StartProgram.IsOsOptimizer && server is OptimizerServer optimizer) data = optimizer.TypeTesterData;
            else throw new NotSupportedException("APM requires a matching actual Tester/Optimizer server.");
            if (data != TesterDataType.TickAllCandleState && data != TesterDataType.TickOnlyReadyCandle)
                throw new NotSupportedException("APM TradeOnly requires native tick data; candle/depth-generated trades are unsupported.");
        }

        private static void ValidateVolumeStep(Security security, ApmCampaignSpec spec)
        {
            if (security.VolumeStep <= 0 || security.VolumeStep != spec.VolumeStep
                || security.DecimalsVolume < 0 || security.DecimalsVolume > 28
                || decimal.Round(spec.VolumeStep, security.DecimalsVolume) != spec.VolumeStep)
                throw new InvalidOperationException("Explicit native volume step/precision must match the saved campaign; no inferred lot size.");
        }

        /// <summary>Unsubscribe native events and release artifacts; caller must inspect unclosed research positions.</summary>
        public void Dispose()
        {
            lock (Controller.SyncRoot)
            {
                if (_disposed) return;
                _tab.NewTickEvent -= Tab_NewTickEvent;
                _tab.MyTradeEvent -= Tab_MyTradeEvent;
                _tab.OrderUpdateEvent -= Tab_OrderUpdateEvent;
                DetachClock();
                _clockServer = null;
                Controller.Dispose();
                _disposed = true;
            }
        }

        #endregion

        private sealed record NativeOrder(int UserId, string MarketId, OrderStateType State, decimal Filled);
        private sealed record NativeFill(string Id, string OrderId, string Security, DateTime Time, decimal Price, decimal Volume, Side Side);
    }
}
