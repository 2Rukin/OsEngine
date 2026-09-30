/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using OsEngine.Entity;

namespace OsEngine.Market.Servers
{
    /// <summary>Optional account updates captured before the mutable legacy portfolio dispatch queue.</summary>
    public interface IExplicitAccountSource
    {
        /// <summary>True only when actual realization updates, rather than republished cache, supply observation presence.</summary>
        bool HasExplicitAccountUpdates { get; }
        /// <summary>Only the account and position rows actually present in this realization update.</summary>
        event Action<ExplicitAccount> ExplicitAccountEvent;
    }

    /// <summary>Immutable account receipt; missing delta rows mean no update, full-snapshot omissions mean unknown, never confirmed zero.</summary>
    /// <remarks>Receipt time is local capture time, not exchange sequence or an atomic order/account snapshot.</remarks>
    public sealed class ExplicitAccount
    {
        /// <summary>Native account routing identity; consumers must not log it.</summary>
        public string Number { get; }
        /// <summary>Source capture time retained through asynchronous dispatch.</summary>
        public DateTime ReceivedAt { get; }
        /// <summary>Funds were actually updated by this observation; a position-only update does not refresh funds.</summary>
        public bool FundsUpdated { get; }
        /// <summary>Raw current value; its interpretation as free collateral is an explicit connector-profile choice.</summary>
        public decimal Current { get; }
        /// <summary>Raw blocked value in the same native units.</summary>
        public decimal Blocked { get; }
        /// <summary>True when Current is already free collateral, so Blocked must not be subtracted again.</summary>
        public bool FundsAreFree { get; }
        /// <summary>Optional original connection generation; empty retains legacy receipt semantics.</summary>
        public string Session { get; }
        /// <summary>Source capture sequence within Session.</summary>
        public long Sequence { get; }
        /// <summary>The source enumerated the selected profile inventory; missing rows become unknown, not zero.</summary>
        public bool PositionsComplete { get; }
        /// <summary>Position quantities explicitly updated by the source, keyed by instrument.</summary>
        public IReadOnlyDictionary<string, decimal> Positions { get; }

        /// <summary>Copies one actual source observation, rejecting ambiguous duplicate instrument rows.</summary>
        /// <remarks>The realization supplies only rows touched by this callback and its original receipt time.
        /// Passing a cached full portfolio list would violate this contract. FundsUpdated is independent
        /// of row presence; absence cannot refresh either fact. Consumers serialize/expire observations.
        /// ADR-THG-002; offline Alor callback fixtures do not establish broker transport qualification.</remarks>
        public ExplicitAccount(Portfolio portfolio, bool fundsUpdated, IEnumerable<PositionOnBoard> updatedPositions, DateTime receivedAt,
            bool fundsAreFree = false, string session = "", long sequence = 0, bool positionsComplete = false)
        {
            Number = portfolio.Number; Current = portfolio.ValueCurrent; Blocked = portfolio.ValueBlocked;
            FundsUpdated = fundsUpdated; ReceivedAt = receivedAt;
            FundsAreFree = fundsAreFree; Session = session; Sequence = sequence; PositionsComplete = positionsComplete;
            Positions = new ReadOnlyDictionary<string, decimal>((updatedPositions ?? Array.Empty<PositionOnBoard>())
                .ToDictionary(p => p.SecurityNameCode, p => p.ValueCurrent, StringComparer.Ordinal));
        }
    }
}
