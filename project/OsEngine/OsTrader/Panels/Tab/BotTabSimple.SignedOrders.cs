/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Linq;
using OsEngine.Entity;

namespace OsEngine.OsTrader.Panels.Tab
{
    public partial class BotTabSimple
    {
        /// <summary>Captures this tab's identified inventory; no position is published and no quantity is changed.</summary>
        public string CaptureInventory(Position candidate, decimal percentBase, DateTime at)
        {
            if (_isDelete || candidate == null || candidate.NameBot != TabName || Portfolio == null || Security == null)
                throw new InvalidOperationException("Invalid inventory capture endpoint.");
            return _journal.CaptureInventory(candidate, _connector.ServerFullName, Portfolio.Number, percentBase, at);
        }

        /// <summary>Applies an already persisted ownership operation to this tab's journal without sending.</summary>
        public Position ApplyInventory(Position snapshot, PositionInventoryAdjustment adjustment)
        {
            if (_isDelete || snapshot == null || snapshot.Inventory == null || snapshot.NameBot != TabName)
                throw new InvalidOperationException("Invalid native inventory owner.");
            return _journal.ApplyInventory(snapshot, adjustment);
        }

        /// <summary>Creates one explicitly signed-price native order and binds its identity before dispatch.</summary>
        /// <param name="position">Null opens a native position; otherwise reduces this tab's existing position.</param>
        /// <param name="side">Actual order side; must oppose an existing position when reducing.</param>
        /// <param name="volume">Positive quantity already reserved by the strategy.</param>
        /// <param name="price">Exact tick-aligned literal price, including zero.</param>
        /// <param name="market">Use native market type if supported; otherwise this exact limit is used.</param>
        /// <param name="percentBase">Independent positive price-unit basis for native percentage display.</param>
        /// <param name="signal">Stable strategy intent correlation.</param>
        /// <param name="beforeSend">Must durably bind the returned position/order before any external send.</param>
        /// <returns>The native journal position owning the created order.</returns>
        /// <remarks>Does not cancel previous orders, infer quote presence, or claim exchange capability.
        /// Caller owns freshness, capability, reservations and cancel-confirm sequencing. A throw after
        /// beforeSend begins is an uncertain outcome, never permission to resend. THG-EXECUTION-001.</remarks>
        public Position SubmitSignedOrder(Position position, Side side, decimal volume, decimal price,
            bool market, decimal percentBase, string signal, Action<Position, Order> beforeSend)
        {
            if (beforeSend == null || percentBase <= 0 || volume <= 0 || (side != Side.Buy && side != Side.Sell))
                throw new ArgumentException("Invalid signed order request.");
            if (_isDelete || Security == null || Portfolio == null || !_connector.IsReadyToTrade || !_connector.IsConnected)
                throw new InvalidOperationException("Native execution endpoint is not ready.");
            if (!SignedPriceMath.HasFivePlaces(price) || !SignedPriceMath.IsOnTick(price, Security.PriceStep))
                throw new ArgumentException("Signed limit is not representable on the instrument tick grid.");
            if (!SignedPriceMath.IsNativeVolume(volume, Security))
                throw new ArgumentException("Signed order quantity violates native contract metadata.");
            if (position != null && (PositionsAll == null || !PositionsAll.Contains(position)
                || position.Direction == side || volume > position.OpenVolume))
                throw new InvalidOperationException("Close must reserve this journal's remaining position.");
            OrderPriceType type = market && _connector.MarketOrdersIsSupport ? OrderPriceType.Market : OrderPriceType.Limit;
            Order order;
            if (position == null)
            {
                position = _dealCreator.CreatePosition(TabName, side, price, volume, type,
                    ManualPositionSupport.SecondToOpen, Security, Portfolio, StartProgram,
                    GetOrderLifeTimeFromSettings(), false);
                position.SignalTypeOpen = signal;
                position.NameBotClass = BotClassName;
                order = position.OpenOrders[0];
                order.UsesSignedPrice = true;
                order.SignedPercentBase = percentBase;
                _journal.SetNewDeal(position);
            }
            else
            {
                order = _dealCreator.CreateCloseOrderForDeal(Security, position, price, type,
                    ManualPositionSupport.SecondToClose, StartProgram, GetOrderLifeTimeFromSettings(),
                    _connector.ServerFullName, false);
                if (order == null) throw new InvalidOperationException("Native close position is empty.");
                order.Volume = volume;
                order.UsesSignedPrice = true;
                order.SignedPercentBase = percentBase;
                position.SignalTypeClose = signal;
                position.AddNewCloseOrder(order);
            }
            beforeSend(position, order);
            _journal.Save();
            _connector.OrderExecute(order);
            return position;
        }
    }
}
