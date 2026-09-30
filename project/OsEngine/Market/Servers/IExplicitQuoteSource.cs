/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using OsEngine.Entity;

namespace OsEngine.Market.Servers
{
    /// <summary>Optional, presence-aware market data stream; zero is a literal price.</summary>
    /// <remarks>Consumers must match instrument identity, check connection/session and expire snapshots.
    /// This interface establishes data representation, not broker permission to submit signed prices.
    /// Legacy IServer events keep their existing filtering. THG-PRICE-001.</remarks>
    public interface IExplicitQuoteSource
    {
        /// <summary>Publishes an immutable quote, including disappearance of either depth side.</summary>
        event Action<ExplicitQuote> ExplicitQuoteEvent;
    }

    /// <summary>An immutable top-of-book snapshot with independently known sides.</summary>
    public sealed class ExplicitQuote
    {
        /// <summary>Instrument code from the source feed.</summary>
        public string Instrument { get; }
        /// <summary>Presence of bid, independent of its value.</summary>
        public bool HasBid { get; }
        /// <summary>Literal signed bid.</summary>
        public decimal Bid { get; }
        /// <summary>Presence of ask, independent of its value.</summary>
        public bool HasAsk { get; }
        /// <summary>Literal signed ask.</summary>
        public decimal Ask { get; }
        /// <summary>Exchange/event timestamp; live consumers also record local receipt time.</summary>
        public DateTime Time { get; }
        /// <summary>Local source capture time, preserved across queued dispatch for live freshness.</summary>
        public DateTime ReceivedAt { get; }
        /// <summary>Optional original connection generation; empty retains legacy receipt semantics.</summary>
        public string Session { get; }
        /// <summary>Source capture sequence within Session; equal timestamps need not mean duplicate observations.</summary>
        public long Sequence { get; }

        /// <summary>Creates a snapshot without assuming absent values equal zero.</summary>
        public ExplicitQuote(string instrument, bool hasBid, decimal bid, bool hasAsk, decimal ask, DateTime time,
            DateTime? receivedAt = null, string session = "", long sequence = 0)
        {
            Instrument = instrument; HasBid = hasBid; Bid = bid; HasAsk = hasAsk; Ask = ask; Time = time;
            ReceivedAt = receivedAt ?? DateTime.Now; Session = session; Sequence = sequence;
        }

        /// <summary>Copies top levels; empty and one-sided books are delivered as such.</summary>
        public static ExplicitQuote FromDepth(MarketDepth depth)
        {
            bool bid = depth.Bids != null && depth.Bids.Count > 0;
            bool ask = depth.Asks != null && depth.Asks.Count > 0;
            return new ExplicitQuote(depth.SecurityNameCode, bid, bid ? (decimal)depth.Bids[0].Price : 0,
                ask, ask ? (decimal)depth.Asks[0].Price : 0, depth.Time);
        }
    }
}
