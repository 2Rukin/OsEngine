/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Linq;

namespace OsEngine.Entity
{
    public partial class Position
    {
        /// <summary>Whether the initial order or explicit inventory ledger opted into literal signed/zero prices.</summary>
        public bool UsesSignedPrices => Inventory != null || OpenOrders != null && OpenOrders.Count > 0 && OpenOrders[0].UsesSignedPrice;

        /// <summary>Computes fees from identified fill quantities; percentage fees use absolute turnover.</summary>
        /// <remarks>Absolute turnover is an explicit reporting convention, not a broker fee schedule.
        /// Strategies requiring independent per-unit monetary fees configure OneLotFix instead.</remarks>
        public decimal SignedCommissionTotal()
        {
            if (!UsesSignedPrices || CommissionType == CommissionType.None) return 0;
            MyTrade[] fills = (OpenOrders ?? new System.Collections.Generic.List<Order>()).Concat(CloseOrders ?? new System.Collections.Generic.List<Order>())
                .Where(o => o.MyTrades != null).SelectMany(o => o.MyTrades).ToArray();
            decimal multiplier = Inventory?.LotMultiplier ?? (IsLotServer() && Lots != 0 ? Lots : 1);
            if (CommissionType == CommissionType.OneLotFix) return checked(fills.Sum(t => t.Volume) * multiplier * CommissionValue);
            decimal conversion = PriceStep > 0 && PriceStepCost > 0 ? PriceStepCost / PriceStep : 1;
            return checked(fills.Sum(t => Math.Abs(t.Price) * t.Volume) * multiplier * conversion * CommissionValue / 100);
        }

        /// <summary>Marks a signed position from identified fills and explicit quote presence.</summary>
        /// <remarks>No positive-price assumption is made. Missing liquidation quote retains the last mark.
        /// A fully identified closed position needs no quote. Percent uses the opening order's or inventory
        /// ledger's positive independent price-unit base, not entry price. Inventory preserves historical
        /// realized results across remaining-basis edits. Legacy positions retain SetBidAsk behavior.</remarks>
        public void SetSignedBidAsk(bool hasBid, decimal bid, bool hasAsk, decimal ask)
        {
            if (Inventory != null)
            {
                PositionInventoryValue value = Inventory.Value;
                if (value.Entered == 0 || value.Quantity > 0 && (Direction == Side.Buy ? !hasBid : !hasAsk)) return;
                if (Direction == Side.Buy ? hasBid : hasAsk) Inventory.LastMark = Direction == Side.Buy ? bid : ask;
                decimal mark = value.Realized + ((Direction == Side.Buy ? bid : ask) * value.Quantity - value.Cost)
                    * (Direction == Side.Buy ? 1 : -1);
                ProfitOperationAbs = mark / value.Entered;
                ProfitOperationPercent = ProfitOperationAbs / Inventory.PercentBase * 100;
                return;
            }
            if (!UsesSignedPrices || OpenOrders[0].SignedPercentBase <= 0) return;
            MyTrade[] entries = OpenOrders.Where(o => o.MyTrades != null).SelectMany(o => o.MyTrades).ToArray();
            MyTrade[] exits = CloseOrders == null ? Array.Empty<MyTrade>()
                : CloseOrders.Where(o => o.MyTrades != null).SelectMany(o => o.MyTrades).ToArray();
            decimal entered = entries.Sum(t => t.Volume);
            decimal closed = exits.Sum(t => t.Volume);
            if (entered <= 0 || closed > entered) return;
            decimal remaining = entered - closed;
            if (remaining > 0 && (Direction == Side.Buy ? !hasBid : !hasAsk)) return;
            checked
            {
                decimal entryCost = entries.Sum(t => t.Price * t.Volume);
                decimal exitCost = exits.Sum(t => t.Price * t.Volume) + remaining * (Direction == Side.Buy ? bid : ask);
                decimal difference = (exitCost - entryCost) / entered * (Direction == Side.Buy ? 1 : -1);
                decimal percent = difference / OpenOrders[0].SignedPercentBase * 100;
                ProfitOperationAbs = difference;
                ProfitOperationPercent = percent;
            }
        }
    }
}
