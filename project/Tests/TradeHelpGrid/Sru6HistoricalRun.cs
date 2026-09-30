/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using OsEngine.Entity;
using OsEngine.Logging;
using OsEngine.Market;
using OsEngine.Market.Servers;
using OsEngine.Market.Servers.Tester;
using OsEngine.OsTrader.Grids.Futures2;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.Robots;
using OsEngine.Robots.MyBots;

namespace OsEngine.TradeHelpGrid.Tests
{
    /// <summary>
    /// Explicit offline native Tester qualification for exact local SRU6 history and the signed tick loader.
    /// It starts no live connector and submits only Tester orders in a new isolated working directory.
    /// Each invocation runs in a dedicated process because the native Tester worker has no public shutdown API.
    /// </summary>
    /// <remarks>
    /// TXT and QSH Quotes are separate scenarios; this harness does not claim synchronized Deals/book replay,
    /// partial-fill, queue, crash-recovery or live TRANSAQ evidence. Contract: THG-QUALIFICATION-001.
    /// </remarks>
    internal sealed class Sru6HistoricalRun
    {
        private readonly List<string> _errors = new List<string>();
        private readonly object _eventSync = new object();
        private readonly Dictionary<int, List<ObservedTick>> _ticksByEndpoint = new Dictionary<int, List<ObservedTick>>();
        private readonly Dictionary<int, int> _depthsByEndpoint = new Dictionary<int, int>();
        private readonly Dictionary<BotTabSimple, Action<Trade>> _tickHandlers = new Dictionary<BotTabSimple, Action<Trade>>();
        private readonly Dictionary<BotTabSimple, Action<MarketDepth>> _depthHandlers = new Dictionary<BotTabSimple, Action<MarketDepth>>();
        private TesterServer _tester;
        private Futures2Grid _robot;
        private Application _application;
        private volatile bool _started;
        private volatile bool _ended;
        private int _clockEvents;
        private int _tickEvents;
        private int _depthEvents;
        private string _output;
        private DateTime _replayStart;
        private DateTime _replayEnd;
        private DateTime _lastReplayTime;

        internal static int RunZeroLoader(string output)
        {
            Sru6HistoricalRun run = new Sru6HistoricalRun();
            int result = run.ExecuteZeroLoader(output);
            Console.Out.Flush();
            Console.Error.Flush();
            Environment.Exit(result);
            return result;
        }

        internal static int Run(string mode, string source, DateTime day, string output)
        {
            Sru6HistoricalRun run = new Sru6HistoricalRun();
            int result = run.Execute(mode, source, day, output);
            Console.Out.Flush();
            Console.Error.Flush();
            Environment.Exit(result);
            return result;
        }

        private int ExecuteZeroLoader(string output)
        {
            _output = PrepareOutput(output);
            string data = Path.Combine(_output, "ticks");
            Directory.CreateDirectory(data);
            string fixture = Path.Combine(data, "SIGNED_ZERO.txt");
            File.WriteAllLines(fixture, new[]
            {
                "20260929,100000,1,1,Buy,0,1",
                "20260929,100001,0,1,Sell,0,2",
                "20260929,100002,-1,1,Sell,0,3"
            }, new UTF8Encoding(false));
            try
            {
                StartApplicationAndTester(TesterDataType.TickOnlyReadyCandle, data);
                Wait(() => _tester.DataIsReady, 45000, "signed zero tick folder load");
                Security security = _tester.Securities?.SingleOrDefault(item => item.Name == "SIGNED_ZERO.txt");
                bool passed = security != null && security.PriceStep == 1;
                WriteJson("result.json", new
                {
                    status = passed ? "PASS" : "FAIL",
                    scenario = "native-zero-loader",
                    rows = 3,
                    prices = new[] { 1m, 0m, -1m },
                    securityLoaded = security != null,
                    priceStep = security?.PriceStep,
                    errors = ErrorSnapshot(),
                    limitation = "Loader admission and metadata only; no orders or broker connection"
                });
                if (!passed)
                {
                    Console.Error.WriteLine("NATIVE_ZERO_LOADER_FAIL instrument was dropped or price step changed");
                    return 1;
                }
                Console.WriteLine("NATIVE_ZERO_LOADER_PASS " + _output);
                return 0;
            }
            catch (Exception error)
            {
                RecordFailure(error);
                return 1;
            }
            finally
            {
                Stop();
            }
        }

        private int Execute(string mode, string source, DateTime day, string output)
        {
            if (mode != "txt" && mode != "qsh") throw new ArgumentException("Mode must be txt or qsh.");
            source = Path.GetFullPath(source);
            _output = PrepareOutput(output);
            string data = Path.Combine(_output, "data");
            Directory.CreateDirectory(data);
            HistoricalInput input = mode == "txt" ? PrepareTxt(source, day, data) : PrepareQsh(source, day, data);
            try
            {
                StartApplicationAndTester(mode == "txt" ? TesterDataType.TickOnlyReadyCandle : TesterDataType.MarketDepthOnlyReadyCandle, data);
                Wait(() => _tester.DataIsReady && _tester.Securities?.Any(item => item.Name == input.Security) == true,
                    45000, "SRU6 native data load");
                Security security = _tester.Securities.Single(item => item.Name == input.Security);
                decimal priceStep = mode == "txt" ? input.Tick : security.PriceStep;
                security.PriceStep = priceStep;
                security.PriceStepCost = priceStep;
                security.Lot = 1;
                security.VolumeStep = 1;
                security.DecimalsVolume = 0;
                security.MinTradeAmount = 1;
                security.MarginBuy = 100;
                security.MarginSell = 100;
                _tester.SaveSecurityDopSettings(security);
                SecurityTester historical = _tester.SecuritiesTester.Single(item => item.Security.Name == input.Security);
                _replayStart = historical.TimeStart;
                _replayEnd = mode == "qsh" && historical.TimeEnd > _replayStart.AddMinutes(5)
                    ? _replayStart.AddMinutes(5) : historical.TimeEnd;
                _tester.TimeStart = _replayStart;
                _tester.TimeEnd = _replayEnd;
                _tester.StartPortfolio = 1000000;
                _tester.SlippageToSimpleOrder = 0;
                _tester.SlippageToStopOrder = 0;
                if (!BotFactory.GetIncludeNamesStrategy().Contains("Futures2Grid"))
                    throw new InvalidOperationException("Futures2Grid is absent from BotFactory.");
                _robot = (Futures2Grid)BotFactory.GetStrategyForName("Futures2Grid", "SRU6_native", StartProgram.IsTester, false);
                _robot.LogMessageEvent += Log;
                ConfigureRobot(input, priceStep);
                for (int endpoint = 0; endpoint < _robot.TabsSimple.Count; endpoint++)
                {
                    BotTabSimple tab = _robot.TabsSimple[endpoint];
                    int capturedEndpoint = endpoint;
                    Action<Trade> tickHandler = trade => Tick(capturedEndpoint, trade);
                    Action<MarketDepth> depthHandler = depth => Depth(capturedEndpoint, depth);
                    lock (_eventSync)
                    {
                        _ticksByEndpoint[endpoint] = new List<ObservedTick>();
                        _depthsByEndpoint[endpoint] = 0;
                    }
                    _tickHandlers[tab] = tickHandler;
                    _depthHandlers[tab] = depthHandler;
                    tab.NewTickEvent += tickHandler;
                    tab.MarketDepthUpdateEvent += depthHandler;
                    Connect(tab, input.Security);
                }
                Wait(() => _robot.TabsSimple.All(tab => tab.IsConnected && tab.IsReadyToTrade), 45000, "SRU6 tab subscription");
                _tester.SynchSecurities(new List<BotPanel> { _robot });
                Pump(5500);
                Thread starter = new Thread(() => _tester.TestingStart()) { IsBackground = true, Name = "Futures2 SRU6 native replay start" };
                starter.Start();
                Wait(() => _ended, 180000, "SRU6 native replay terminal event");
                Pump(300);
                return Verify(mode, source, day, input);
            }
            catch (Exception error)
            {
                RecordFailure(error);
                return 1;
            }
            finally
            {
                Stop();
            }
        }

        private int Verify(string mode, string source, DateTime day, HistoricalInput input)
        {
            Futures2NativeAdapter adapter = (Futures2NativeAdapter)typeof(Futures2Grid)
                .GetField("_adapter", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_robot);
            Futures2Checkpoint checkpoint = adapter.Engine.Data;
            List<NativeOrderLink> nativeOrders = new List<NativeOrderLink>();
            for (int endpoint = 0; endpoint < _robot.TabsSimple.Count; endpoint++)
            {
                foreach (Position position in _robot.TabsSimple[endpoint].PositionsAll ?? new List<Position>())
                {
                    foreach (Order order in (position.OpenOrders ?? new List<Order>()).Concat(position.CloseOrders ?? new List<Order>()))
                        nativeOrders.Add(new NativeOrderLink { Endpoint = endpoint, PositionNumber = position.Number, Order = order });
                }
            }
            List<MyTrade> fills = _tester.MyTrades ?? new List<MyTrade>();
            string[] errors = ErrorSnapshot();
            List<Futures2Intent> intents = checkpoint.Book.Intents;
            List<TabTickEvidence> tabTicks = TickEvidence(input);
            DateTime lastReplayTime;
            Dictionary<int, int> tabDepths;
            lock (_eventSync)
            {
                lastReplayTime = _lastReplayTime;
                tabDepths = _depthsByEndpoint.OrderBy(item => item.Key).ToDictionary(item => item.Key, item => item.Value);
            }
            bool reachedReplayEnd = lastReplayTime >= _replayEnd;
            bool completeTxtDelivery = mode != "txt" || tabTicks.Count == _robot.TabsSimple.Count
                && tabTicks.All(tab => tab.Complete);
            bool completeQshDelivery = mode != "qsh" || tabDepths.Count == _robot.TabsSimple.Count
                && tabDepths.All(item => item.Value > 0);
            bool nativeLedgerMatch = NativeLedgerMatches(intents, nativeOrders, fills);
            decimal[] orderPrices = nativeOrders.Select(item => item.Order.Price).ToArray();
            decimal[] fillPrices = fills.Select(fill => fill.Price).ToArray();
            bool signedInput = mode == "txt" && input.Ticks.Any(tick => tick.Price < 0)
                && input.Ticks.Any(tick => tick.Price == 0) && input.Ticks.Any(tick => RequiresFifthDecimal(tick.Price));
            bool signedOrderPrices = !signedInput || HasSignedPriceEvidence(orderPrices);
            bool signedFillPrices = !signedInput || HasSignedPriceEvidence(fillPrices);
            bool eventEvidence = mode == "txt" ? _tickEvents > 0 : _depthEvents > 0;
            bool passed = _started && _ended && _clockEvents > 0 && eventEvidence && reachedReplayEnd
                && completeTxtDelivery && completeQshDelivery
                && checkpoint.ActivePlan.Length > 0 && checkpoint.State != Futures2State.Faulted
                && checkpoint.State != Futures2State.Reconciling && nativeOrders.Count > 0
                && nativeOrders.All(item => item.Order.UsesSignedPrice) && fills.Count > 0 && nativeLedgerMatch
                && signedOrderPrices && signedFillPrices && errors.Length == 0;
            WriteJson("result.json", new
            {
                status = passed ? "PASS" : "FAIL",
                scenario = signedInput ? "native-signed-cross-zero-txt" : "native-sru6-" + mode,
                source = Path.GetFullPath(source),
                sourceSha256 = Hash(source),
                inputSha256 = Hash(input.File),
                day = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                replayStart = _replayStart,
                replayEnd = _replayEnd,
                input.Rows,
                input.First,
                input.Last,
                started = _started,
                ended = _ended,
                clockEvents = _clockEvents,
                tickEvents = _tickEvents,
                depthEvents = _depthEvents,
                lastReplayTime,
                reachedReplayEnd,
                completeTxtDelivery,
                completeQshDelivery,
                tabTicks,
                tabDepths,
                state = checkpoint.State.ToString(),
                reason = checkpoint.Reason,
                planLevels = checkpoint.ActivePlan.Length == 0 ? 0 : checkpoint.Plans[checkpoint.ActivePlan].Levels.Count,
                intents = intents.Count,
                nativeOrders = nativeOrders.Count,
                nativeFills = fills.Count,
                allOrdersOptedIntoSignedPrice = nativeOrders.Count > 0 && nativeOrders.All(item => item.Order.UsesSignedPrice),
                nativeLedgerMatch,
                signedInput,
                signedOrderPrices,
                signedFillPrices,
                orderEvidence = nativeOrders.Select(item => new
                {
                    item.Endpoint,
                    item.PositionNumber,
                    item.Order.NumberUser,
                    item.Order.NumberMarket,
                    item.Order.Price,
                    item.Order.Volume,
                    executed = item.Order.VolumeExecute,
                    item.Order.UsesSignedPrice
                }),
                fillEvidence = fills.Select(fill => new
                {
                    fill.NumberTrade,
                    fill.NumberOrderParent,
                    fill.Price,
                    fill.Volume,
                    fill.Side,
                    fill.Time
                }),
                intentEvidence = intents.Select(intent => new
                {
                    intent.Id,
                    intent.Endpoint,
                    intent.PositionNumber,
                    intent.OrderNumber,
                    intent.MarketNumber,
                    intent.Price,
                    intent.Quantity,
                    intent.Filled,
                    filledCost = intent.Allocations.Sum(allocation => allocation.FilledCost)
                }),
                openQuantity = checkpoint.Book.Lots.Sum(lot => lot.Quantity),
                errors,
                limitations = "Historical native Tester; full-fill simulator; no queue, partial fill, impact, process crash or live parity"
            });
            if (!passed)
            {
                Console.Error.WriteLine("NATIVE_SRU6_FAIL mode=" + mode + " state=" + checkpoint.State
                    + " orders=" + nativeOrders.Count + " fills=" + fills.Count + " errors=" + errors.Length
                    + " reachedEnd=" + reachedReplayEnd + " completeTxt=" + completeTxtDelivery + " completeQsh=" + completeQshDelivery
                    + " ledger=" + nativeLedgerMatch + " signedOrders=" + signedOrderPrices + " signedFills=" + signedFillPrices);
                return 1;
            }
            Console.WriteLine("NATIVE_SRU6_PASS mode=" + mode + " events=" + (mode == "txt" ? _tickEvents : _depthEvents)
                + " orders=" + nativeOrders.Count + " fills=" + fills.Count + " " + _output);
            return 0;
        }

        private List<TabTickEvidence> TickEvidence(HistoricalInput input)
        {
            List<TabTickEvidence> evidence = new List<TabTickEvidence>();
            lock (_eventSync)
            {
                foreach (KeyValuePair<int, List<ObservedTick>> item in _ticksByEndpoint.OrderBy(item => item.Key))
                {
                    bool complete = item.Value.Count == input.Ticks.Count;
                    for (int index = 0; complete && index < input.Ticks.Count; index++)
                        complete = item.Value[index].Same(input.Ticks[index]);
                    evidence.Add(new TabTickEvidence
                    {
                        Endpoint = item.Key,
                        Count = item.Value.Count,
                        Complete = complete,
                        First = item.Value.Count == 0 ? null : item.Value[0],
                        Last = item.Value.Count == 0 ? null : item.Value[item.Value.Count - 1],
                        Prices = item.Value.Select(tick => tick.Price).Distinct().OrderBy(price => price).ToArray()
                    });
                }
            }
            return evidence;
        }

        private static bool NativeLedgerMatches(List<Futures2Intent> intents, List<NativeOrderLink> nativeOrders, List<MyTrade> fills)
        {
            if (intents.Count != nativeOrders.Count || fills.Count == 0) return false;
            foreach (Futures2Intent intent in intents)
            {
                NativeOrderLink[] orderMatches = nativeOrders.Where(item => item.Endpoint == intent.Endpoint
                    && item.PositionNumber == intent.PositionNumber && item.Order.NumberUser == intent.OrderNumber).ToArray();
                if (orderMatches.Length != 1) return false;
                Order order = orderMatches[0].Order;
                MyTrade[] orderFills = fills.Where(fill => fill.NumberOrderParent == order.NumberMarket).ToArray();
                decimal filledVolume = orderFills.Sum(fill => fill.Volume);
                decimal filledCost = orderFills.Sum(fill => fill.Price * fill.Volume);
                if (intent.MarketNumber != order.NumberMarket || intent.Quantity != order.Volume
                    || order.VolumeExecute != filledVolume || intent.Filled != filledVolume
                    || intent.Allocations.Sum(allocation => allocation.FilledCost) != filledCost)
                    return false;
            }
            return fills.All(fill => nativeOrders.Count(item => item.Order.NumberMarket == fill.NumberOrderParent) == 1);
        }

        private static bool HasSignedPriceEvidence(decimal[] prices) => prices.Any(price => price < 0)
            && prices.Any(price => price == 0) && prices.Any(RequiresFifthDecimal)
            && prices.All(SignedPriceMath.HasFivePlaces);

        private static bool RequiresFifthDecimal(decimal price) => price * 10000m != decimal.Truncate(price * 10000m);

        private void ConfigureRobot(HistoricalInput input, decimal priceStep)
        {
            SetBool("Signed order capability selected", true);
            SetDecimal("Capital", 100000m);
            SetDecimal("Manual available funds", 100000m);
            SetString("Grid.Currency", "RUB");
            SetString("Grid.Direction", "Long");
            SetBool("Grid.IsHedge", false);
            SetDecimal("Grid.Low", input.Low);
            SetDecimal("Grid.High", input.High);
            SetInt("Grid.Count", 5);
            SetString("Grid.CountMode", "Manual");
            SetDecimal("Grid.RequestedStep", (input.High - input.Low) / 4);
            SetDecimal("Grid.TickValue", 1m);
            SetDecimal("Grid.VolumeStep", 1m);
            SetDecimal("Grid.MinimumVolume", 1m);
            SetDecimal("Grid.Budget", 500m);
            SetDecimal("Grid.Collateral", 100m);
            SetDecimal("Grid.FixedVolume", 1m);
            SetDecimal("Grid.Markup", priceStep >= 1 ? priceStep * 10 : priceStep * 5);
            SetDecimal("Grid.PercentBase", Math.Max(priceStep, Math.Max(Math.Abs(input.Low), Math.Abs(input.High))));
            SetBool("Policy.QuoteOrdinaryLimits", true);
            SetInt("Policy.MaxActions", 3);
            SetInt("Policy.IntervalMilliseconds", 0);
            SetInt("Policy.FreshnessSeconds", 60);
            SetInt("Policy.OrderLifeSeconds", 300);
            SetInt("Policy.UnknownAfterSeconds", 300);
            SetBool("Policy.StopAfterExit", true);
            SetString("Regime", "On");
        }

        private void StartApplicationAndTester(TesterDataType type, string data)
        {
            _application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            _application.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri("/OsEngine;component/Themes/ThemeDarkOrange.xaml", UriKind.Relative) });
            OsEngine.MainWindow.ProccesIsWorked = true;
            ServerMaster.LogMessageEvent += Log;
            ServerMaster.CreateServer(ServerType.Tester, false);
            _tester = (TesterServer)ServerMaster.GetServers().Single(server => server.ServerType == ServerType.Tester);
            _tester.LogMessageEvent += Log;
            _tester.TestingStartEvent += Started;
            _tester.TestingEndEvent += Ended;
            _tester.ReplayTimeAdvancedEvent += Clock;
            _tester.TypeTesterData = type;
            _tester.SourceDataType = TesterSourceDataType.Folder;
            _tester.SetFolderPath(data);
        }

        private void Connect(BotTabSimple tab, string security)
        {
            tab.Connector.ServerType = ServerType.Tester;
            tab.Connector.ServerFullName = _tester.ServerNameAndPrefix;
            tab.Connector.PortfolioName = "GodMode";
            tab.Connector.TimeFrame = TimeFrame.Sec1;
            tab.Connector.SecurityClass = "TestClass";
            tab.Connector.SecurityName = security;
        }

        private void Started()
        {
            _robot?.Clear();
            if (_robot != null) _tester.SynchSecurities(new List<BotPanel> { _robot });
            _started = true;
        }

        private void Ended() => _ended = true;

        private void Clock(DateTime time)
        {
            Interlocked.Increment(ref _clockEvents);
            lock (_eventSync)
            {
                if (time > _lastReplayTime) _lastReplayTime = time;
            }
        }

        private void Tick(int endpoint, Trade trade)
        {
            Interlocked.Increment(ref _tickEvents);
            lock (_eventSync)
            {
                _ticksByEndpoint[endpoint].Add(new ObservedTick
                {
                    Time = trade.Time,
                    MicroSeconds = trade.MicroSeconds,
                    Id = trade.Id,
                    Price = trade.Price,
                    Volume = trade.Volume,
                    Side = trade.Side
                });
            }
        }

        private void Depth(int endpoint, MarketDepth depth)
        {
            Interlocked.Increment(ref _depthEvents);
            lock (_eventSync) _depthsByEndpoint[endpoint]++;
        }

        private void SetString(string name, string value) => ((StrategyParameterString)_robot.Parameters.Single(p => p.Name == name)).ValueString = value;
        private void SetDecimal(string name, decimal value) => ((StrategyParameterDecimal)_robot.Parameters.Single(p => p.Name == name)).ValueDecimal = value;
        private void SetInt(string name, int value) => ((StrategyParameterInt)_robot.Parameters.Single(p => p.Name == name)).ValueInt = value;
        private void SetBool(string name, bool value) => ((StrategyParameterBool)_robot.Parameters.Single(p => p.Name == name)).ValueBool = value;

        private void Log(string message, LogMessageType type)
        {
            if (type != LogMessageType.Error) return;
            lock (_errors) _errors.Add(message);
            Console.Error.WriteLine("NATIVE_ERROR " + message);
        }

        private string[] ErrorSnapshot()
        {
            lock (_errors) return _errors.ToArray();
        }

        private void Wait(Func<bool> condition, int milliseconds, string stage)
        {
            DateTime limit = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (!condition())
            {
                if (DateTime.UtcNow >= limit)
                    throw new TimeoutException(stage + "; clocks=" + _clockEvents + "; ticks=" + _tickEvents + "; depths=" + _depthEvents);
                Pump(50);
            }
            Console.WriteLine("NATIVE_STAGE " + stage);
        }

        private static void Pump(int milliseconds)
        {
            DateTime limit = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < limit)
            {
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(Empty));
                Thread.Sleep(10);
            }
        }

        private static void Empty() { }

        private void Stop()
        {
            if (_tester != null)
            {
                _tester.TesterRegime = TesterRegime.Pause;
                _tester.TestingStartEvent -= Started;
                _tester.TestingEndEvent -= Ended;
                _tester.ReplayTimeAdvancedEvent -= Clock;
                _tester.LogMessageEvent -= Log;
            }
            if (_robot != null)
            {
                foreach (BotTabSimple tab in _robot.TabsSimple)
                {
                    if (_tickHandlers.TryGetValue(tab, out Action<Trade> tickHandler)) tab.NewTickEvent -= tickHandler;
                    if (_depthHandlers.TryGetValue(tab, out Action<MarketDepth> depthHandler)) tab.MarketDepthUpdateEvent -= depthHandler;
                }
                _robot.LogMessageEvent -= Log;
                _robot.Delete();
            }
            ServerMaster.LogMessageEvent -= Log;
            OsEngine.MainWindow.ProccesIsWorked = false;
            if (_output != null) File.WriteAllLines(Path.Combine(_output, "errors.txt"), ErrorSnapshot());
            _application?.Shutdown();
        }

        private void RecordFailure(Exception error)
        {
            Console.Error.WriteLine(error);
            File.WriteAllText(Path.Combine(_output, "failure.txt"), error.ToString());
        }

        private void WriteJson(string name, object value)
        {
            File.WriteAllText(Path.Combine(_output, name), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static string PrepareOutput(string output)
        {
            string full = Path.GetFullPath(output);
            if (Directory.Exists(full)) throw new InvalidOperationException("Evidence directory must be new: " + full);
            Directory.CreateDirectory(full);
            string workspace = Path.Combine(full, "workspace");
            Directory.CreateDirectory(workspace);
            Environment.CurrentDirectory = workspace;
            Directory.CreateDirectory("Engine");
            Directory.CreateDirectory("Data");
            Directory.CreateDirectory("Log");
            return full;
        }

        private static HistoricalInput PrepareTxt(string source, DateTime day, string data)
        {
            string full = Path.GetFullPath(source);
            if (!File.Exists(full)) throw new FileNotFoundException("SRU6 TXT not found.", full);
            string target = Path.Combine(data, "SRU6.txt");
            string prefix = day.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ",";
            int rows = 0;
            string first = "";
            string last = "";
            decimal low = decimal.MaxValue;
            decimal high = decimal.MinValue;
            decimal tick = decimal.MaxValue;
            List<ObservedTick> inputTicks = new List<ObservedTick>();
            using (StreamReader reader = new StreamReader(full))
            using (StreamWriter writer = new StreamWriter(target, false, new UTF8Encoding(false)))
            {
                string row;
                while ((row = reader.ReadLine()) != null)
                {
                    if (!row.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        if (rows > 0) break;
                        continue;
                    }
                    writer.WriteLine(row);
                    string[] fields = row.Split(',');
                    Trade sourceTrade = new Trade();
                    sourceTrade.SetTradeFromString(row);
                    decimal price = sourceTrade.Price;
                    inputTicks.Add(new ObservedTick
                    {
                        Time = sourceTrade.Time,
                        MicroSeconds = sourceTrade.MicroSeconds,
                        Id = sourceTrade.Id,
                        Price = sourceTrade.Price,
                        Volume = sourceTrade.Volume,
                        Side = sourceTrade.Side
                    });
                    low = Math.Min(low, price);
                    high = Math.Max(high, price);
                    int separator = fields[2].IndexOf('.');
                    int scale = separator < 0 ? 0 : fields[2].Length - separator - 1;
                    decimal observedTick = scale == 0 ? 1 : 1m / (decimal)Math.Pow(10, scale);
                    tick = Math.Min(tick, observedTick);
                    first = rows == 0 ? row : first;
                    last = row;
                    rows++;
                }
            }
            if (rows == 0) throw new InvalidOperationException("No SRU6 rows for " + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (low >= high) throw new InvalidOperationException("Historical test requires a non-constant price range.");
            return new HistoricalInput { File = target, Security = "SRU6.txt", Rows = rows, First = first, Last = last,
                Low = low, High = high, Tick = tick, Ticks = inputTicks };
        }

        private static HistoricalInput PrepareQsh(string source, DateTime day, string data)
        {
            string full = Path.GetFullPath(source);
            if (!File.Exists(full)) throw new FileNotFoundException("SRU6 QSH Quotes not found.", full);
            string expected = "." + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".Quotes.qsh";
            if (!full.EndsWith(expected, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("QSH scenario requires the exact day's Quotes.qsh file.");
            string target = Path.Combine(data, Path.GetFileName(full));
            File.Copy(full, target, false);
            return new HistoricalInput { File = target, Security = "SRU6", Rows = 0, First = "QSH v4 Quotes", Last = "QSH v4 Quotes",
                Low = 30000m, High = 32000m, Tick = 1m };
        }

        private static string Hash(string file)
        {
            using FileStream stream = File.OpenRead(file);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        private sealed class HistoricalInput
        {
            internal string File;
            internal string Security;
            internal int Rows;
            internal string First;
            internal string Last;
            internal decimal Low;
            internal decimal High;
            internal decimal Tick;
            internal List<ObservedTick> Ticks = new List<ObservedTick>();
        }

        private sealed class ObservedTick
        {
            public DateTime Time { get; set; }
            public int MicroSeconds { get; set; }
            public string Id { get; set; }
            public decimal Price { get; set; }
            public decimal Volume { get; set; }
            public Side Side { get; set; }

            internal bool Same(ObservedTick other) => other != null && Time == other.Time && MicroSeconds == other.MicroSeconds
                && Id == other.Id && Price == other.Price && Volume == other.Volume && Side == other.Side;
        }

        private sealed class TabTickEvidence
        {
            public int Endpoint { get; set; }
            public int Count { get; set; }
            public bool Complete { get; set; }
            public ObservedTick First { get; set; }
            public ObservedTick Last { get; set; }
            public decimal[] Prices { get; set; }
        }

        private sealed class NativeOrderLink
        {
            internal int Endpoint;
            internal int PositionNumber;
            internal Order Order;
        }
    }
}
