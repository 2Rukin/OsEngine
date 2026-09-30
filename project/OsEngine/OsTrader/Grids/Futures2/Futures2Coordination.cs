/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace OsEngine.OsTrader.Grids.Futures2
{
    /// <summary>Durable source-side transfer, funded only by correlated confirmed close fills.</summary>
    public sealed class Futures2Transfer
    {
        /// <summary>Idempotent cross-instance identity.</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        /// <summary>Explicit destination instance key.</summary>
        public string Destination { get; set; } = "";
        /// <summary>Currency shared by both campaign budgets.</summary>
        public string Currency { get; set; } = "";
        /// <summary>Maximum planning collateral to transfer.</summary>
        public decimal Requested { get; set; }
        /// <summary>Cumulative confirmed release debited from the source budget.</summary>
        public decimal Debited { get; set; }
        /// <summary>Cancellation stops future reductions, but not delivery of already debited credit.</summary>
        public bool Canceled { get; set; }
        /// <summary>Successful requested credit was received and entered, or canceled transfer settled every already debited credit.</summary>
        public bool Completed { get; set; }
    }

    /// <summary>Prepared replacement contract, activated only after old inventory and orders reconcile flat.</summary>
    public sealed class Futures2Rollover
    {
        /// <summary>Detached replacement plan.</summary>
        public Futures2Plan Plan { get; set; }
        /// <summary>Second native endpoint; connection is configured manually.</summary>
        public int Endpoint { get; set; }
        /// <summary>Prepared, Draining, Reducing, Entering, Applied, or Canceled.</summary>
        public string State { get; set; } = "Prepared";
        /// <summary>Confirmed source lots captured after cancellation; preserves old basis independently of native cost.</summary>
        public List<Futures2RolloverLot> Lots { get; set; } = new List<Futures2RolloverLot>();
        /// <summary>Required replacement quantities by stable level ordinal.</summary>
        public Dictionary<int, decimal> Required { get; set; } = new Dictionary<int, decimal>();

        /// <summary>Freezes actual source exposure after all potentially fillable orders have drained.</summary>
        public void Capture(Futures2Checkpoint data)
        {
            if (data.Book.Intents.Any(i => i.CanFill)) throw new InvalidOperationException("Drain source orders before capturing rollover exposure.");
            if (data.Book.Lots.Any(l => l.Quantity > 0 && !data.Plans[l.PlanId].SameOrientation(Plan)))
                throw new InvalidOperationException("Replacement must preserve logical direction and hedge mode.");
            List<Futures2RolloverLot> captured = data.Book.Lots.Where(l => l.Quantity > 0).Select(l =>
            {
                Futures2Plan old = data.Plans[l.PlanId];
                decimal carry = l.ExitCarry;
                return new Futures2RolloverLot { LotId = l.Id, Level = l.LevelId, Quantity = l.Quantity,
                    BasisMoney = (l.Cost + l.Quantity * carry) * old.Input.TickValue / old.Input.Tick,
                    PriorCloseCost = data.Book.Intents.Where(i => !i.Entry).SelectMany(i => i.Allocations).Where(a => a.LotId == l.Id).Sum(a => a.FilledCost),
                    PriceMultiplier = old.Input.TickValue / old.Input.Tick };
            }).ToList();
            Dictionary<int, decimal> required = captured.GroupBy(l => l.Level).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
            foreach (KeyValuePair<int, decimal> item in required)
            {
                Futures2Level level = Plan.Levels.SingleOrDefault(l => l.Id == item.Key);
                if (level == null || item.Value > level.Volume || item.Value < Plan.Input.MinimumVolume || item.Value % Plan.Input.VolumeStep != 0)
                    throw new InvalidOperationException("Replacement plan cannot represent the source level quantity.");
            }
            if (required.Values.Sum() * Plan.Input.Collateral > data.Capital) throw new InvalidOperationException("Replacement exposure exceeds capital.");
            Lots = captured; Required = required;
        }

        /// <summary>Carries old basis minus identified old close proceeds without rewriting fills or realized PnL.</summary>
        public void CarryBasis(Futures2Checkpoint data)
        {
            foreach (KeyValuePair<int, decimal> item in Required)
            {
                decimal carry = 0;
                foreach (Futures2RolloverLot lot in Lots.Where(l => l.Level == item.Key))
                {
                    decimal closeCost = data.Book.Intents.Where(i => !i.Entry).SelectMany(i => i.Allocations)
                        .Where(a => a.LotId == lot.LotId).Sum(a => a.FilledCost);
                    decimal closedBefore = lot.PriorCloseCost;
                    carry += lot.BasisMoney - (closeCost - closedBefore) * lot.PriceMultiplier;
                }
                Plan.ExitCarry[item.Key] = carry / item.Value * Plan.Input.Tick / Plan.Input.TickValue;
            }
        }
    }

    /// <summary>Source inventory basis at the confirmed rollover drain barrier.</summary>
    public sealed class Futures2RolloverLot
    {
        /// <summary>Actual source lot identity.</summary>
        public string LotId { get; set; } = "";
        /// <summary>Mapped replacement level ordinal.</summary>
        public int Level { get; set; }
        /// <summary>Source remaining quantity captured after draining.</summary>
        public decimal Quantity { get; set; }
        /// <summary>Economic source basis in price times quantity times unit value.</summary>
        public decimal BasisMoney { get; set; }
        /// <summary>Positive money multiplier per raw source price unit.</summary>
        public decimal PriceMultiplier { get; set; }
        /// <summary>Old close cost already realized before this replacement began.</summary>
        public decimal PriorCloseCost { get; set; }
    }

    /// <summary>Immutable-by-copy published portfolio view; no native objects or owner locks are shared.</summary>
    public sealed class Futures2Peer
    {
        /// <summary>Registry identity.</summary>
        public string Name { get; set; } = "";
        /// <summary>Budget/PnL currency.</summary>
        public string Currency { get; set; } = "";
        /// <summary>Current spendable campaign capital, which can become zero after transfer.</summary>
        public decimal Capital { get; set; }
        /// <summary>Fixed positive initial return denominator, independent of transfers and current capital.</summary>
        public decimal ReturnBase { get; set; }
        /// <summary>Marked money PnL, absent without fresh quotes.</summary>
        public decimal? Profit { get; set; }
        /// <summary>Confirmed held collateral.</summary>
        public decimal Held { get; set; }
        /// <summary>Publication time on the instance decision clock.</summary>
        public DateTime Time { get; set; }
        /// <summary>Owner explicitly permits incoming transfer credits and group reductions.</summary>
        public bool AcceptCoordination { get; set; }
        /// <summary>Destination owner has started a reconciled campaign eligible for ordinary entries.</summary>
        public bool CanEnter { get; set; }
        /// <summary>Earliest current inventory entry time.</summary>
        public DateTime? FirstEntry { get; set; }
        /// <summary>Campaign/helper creation time on its decision clock.</summary>
        public DateTime Created { get; set; }
        /// <summary>Whether the campaign has received an identified entry fill.</summary>
        public bool WasActivity { get; set; }
        /// <summary>At least one order may still execute.</summary>
        public bool HasPending { get; set; }
        /// <summary>Confirmed transferred collateral allocated to identified destination entry fills.</summary>
        public Dictionary<string, decimal> Spent { get; set; } = new Dictionary<string, decimal>();
        /// <summary>Durably accepted group command identities.</summary>
        public HashSet<string> GroupReceipts { get; set; } = new HashSet<string>();
        /// <summary>Durably accepted cumulative transfer credits by stable transfer identity.</summary>
        public Dictionary<string, decimal> Receipts { get; set; } = new Dictionary<string, decimal>();
    }

    /// <summary>Mailbox command; recipient persists acceptance before acknowledgement publication.</summary>
    public sealed class Futures2PeerCommand
    {
        /// <summary>Idempotency key.</summary>
        public string Id { get; set; } = "";
        /// <summary>Source instance key.</summary>
        public string Source { get; set; } = "";
        /// <summary>Source campaign identity, independent of a recreated display name.</summary>
        public string SourceCampaign { get; set; } = "";
        /// <summary>Monotone sequence for reduction/recovery commands from that campaign.</summary>
        public long Sequence { get; set; }
        /// <summary>Currency identity.</summary>
        public string Currency { get; set; } = "";
        /// <summary>Cumulative credit, or collateral to retain for a group reduction.</summary>
        public decimal Amount { get; set; }
        /// <summary>True for an idempotent group reduction, false for cumulative capital credit.</summary>
        public bool Reduce { get; set; }
        /// <summary>Cancel this helper's voluntary reduction through a barrier; never revokes emergency.</summary>
        public bool Revoke { get; set; }
    }

    /// <summary>Fresh selected-portfolio inputs for the helper, including timer and flat semantics.</summary>
    public sealed class Futures2HelperScope
    {
        /// <summary>Weighted marked return.</summary>
        public decimal Percent { get; set; }
        /// <summary>Earliest currently held entry.</summary>
        public DateTime? FirstEntry { get; set; }
        /// <summary>Earliest selected campaign creation.</summary>
        public DateTime Created { get; set; }
        /// <summary>Any selected campaign has traded.</summary>
        public bool WasActivity { get; set; }
        /// <summary>All selected campaigns have neither inventory nor potentially executable orders.</summary>
        public bool Empty { get; set; }
    }

    /// <summary>In-process coordination through copies and mailboxes, never nested robot locks.</summary>
    /// <remarks>Persistent sender/receiver records, not this volatile registry, establish credit ownership.
    /// Scope keys isolate live, Tester and each Optimizer server. Deleted peers are removed explicitly.</remarks>
    public static class Futures2Coordination
    {
        private static readonly ConcurrentDictionary<string, Futures2Peer> Peers = new ConcurrentDictionary<string, Futures2Peer>();
        private static readonly ConcurrentDictionary<string, ConcurrentQueue<Futures2PeerCommand>> Mailboxes = new ConcurrentDictionary<string, ConcurrentQueue<Futures2PeerCommand>>();

        /// <summary>Publishes a detached snapshot.</summary>
        public static void Publish(Futures2Peer peer) { Peers[peer.Name] = Futures2Commands.Copy(peer); }
        /// <summary>Returns a detached participant snapshot, or null.</summary>
        public static Futures2Peer Get(string name) => Peers.TryGetValue(name, out Futures2Peer peer) ? Futures2Commands.Copy(peer) : null;
        /// <summary>Enqueues to a currently registered consenting participant; bounded to avoid unbounded disconnected replay.</summary>
        public static bool Send(string destination, Futures2PeerCommand command)
        {
            if (!Peers.TryGetValue(destination, out Futures2Peer peer) || !peer.AcceptCoordination || peer.Currency != command.Currency) return false;
            ConcurrentQueue<Futures2PeerCommand> queue = Mailboxes.GetOrAdd(destination, _ => new ConcurrentQueue<Futures2PeerCommand>());
            if (queue.Count >= 256) return false;
            queue.Enqueue(Futures2Commands.Copy(command)); return true;
        }
        /// <summary>Dequeues one command; only the named participant consumes its mailbox.</summary>
        public static bool Receive(string name, out Futures2PeerCommand command)
        { command = null; return Mailboxes.TryGetValue(name, out ConcurrentQueue<Futures2PeerCommand> queue) && queue.TryDequeue(out command); }
        /// <summary>Unregisters an instance; durable senders retry unacknowledged credit after reconnection.</summary>
        public static void Remove(string name) { Peers.TryRemove(name, out _); Mailboxes.TryRemove(name, out _); }
        /// <summary>Builds all helper inputs from the same fresh selection snapshot.</summary>
        public static Futures2HelperScope Helper(IEnumerable<string> selection, string currency, DateTime now, int freshnessSeconds)
        {
            Futures2Peer[] peers = selection.Distinct(StringComparer.Ordinal).Select(Get).ToArray();
            if (peers.Length == 0 || peers.Any(p => p == null || p.Currency != currency || p.ReturnBase <= 0 || !p.Profit.HasValue
                || p.Time > now || now - p.Time > TimeSpan.FromSeconds(freshnessSeconds))) return null;
            return new Futures2HelperScope { Percent = checked(peers.Sum(p => p.Profit.Value) / peers.Sum(p => p.ReturnBase) * 100),
                FirstEntry = peers.Where(p => p.FirstEntry.HasValue).Select(p => p.FirstEntry).DefaultIfEmpty(null).Min(),
                Created = peers.Min(p => p.Created), WasActivity = peers.Any(p => p.WasActivity), Empty = peers.All(p => p.Held == 0 && !p.HasPending) };
        }

        /// <summary>Aggregates explicitly selected fresh same-currency participants over their fixed positive return bases.</summary>
        public static decimal? Percent(IEnumerable<string> selection, string currency, DateTime now, int freshnessSeconds)
        {
            Futures2Peer[] peers = selection.Distinct(StringComparer.Ordinal).Select(Get).ToArray();
            if (peers.Length == 0 || peers.Any(p => p == null || p.Currency != currency || p.ReturnBase <= 0 || !p.Profit.HasValue
                || p.Time > now || now - p.Time > TimeSpan.FromSeconds(freshnessSeconds))) return null;
            return checked(peers.Sum(p => p.Profit.Value) / peers.Sum(p => p.ReturnBase) * 100);
        }
    }
}
