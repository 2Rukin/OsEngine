/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using OsEngine.Entity;

namespace OsEngine.Market.Servers.Transaq
{
    /// <summary>Managed opt-in TRANSAQ protocol state, owned by the realization; no workers, DLL calls or independent lifecycle.</summary>
    /// <remarks>THG-TRANSAQ-PLAN-001. Prices remain decimal. Callback clocks are captured before queues.
    /// Missing positions never imply zero; broker references, rather than reused venue numbers, establish ownership.</remarks>
    internal sealed class TransaqSignedProtocol
    {
        internal sealed class Frame
        {
            internal string Xml;
            internal string Session;
            internal long Sequence;
            internal DateTime ReceivedAt;
            internal string ClientKey = "";
        }

        private sealed class Binding
        {
            internal Order Template;
            internal string Transaction = "";
            internal string TransactionSession = "";
            internal string Venue = "";
            internal string VenueSession = "";
            internal string Status = "";
            internal bool Withdrawn;
            internal bool HasBalance;
            internal decimal Executed;
            internal OrderStateType State;
            internal long Sequence;
        }

        private sealed class Book
        {
            internal readonly SortedDictionary<decimal, decimal> Bids = new SortedDictionary<decimal, decimal>();
            internal readonly SortedDictionary<decimal, decimal> Asks = new SortedDictionary<decimal, decimal>();
        }

        private readonly object _gate = new object();
        private readonly Dictionary<string, Binding> _orders = new Dictionary<string, Binding>(StringComparer.Ordinal);
        private readonly Dictionary<string, Book> _books = new Dictionary<string, Book>(StringComparer.Ordinal);
        private readonly List<Frame> _unmatched = new List<Frame>();
        private string _session = "";
        private long _sequence;
        internal bool UnionProfile { get; private set; }
        internal bool HasRegistrations { get { lock (_gate) return _orders.Count > 0; } }
        internal string Session { get { lock (_gate) return _session; } }

        internal void Begin(bool union)
        {
            lock (_gate)
            {
                UnionProfile = union; _session = Guid.NewGuid().ToString("N"); _sequence = 0; _books.Clear();
                foreach (Binding binding in _orders.Values) { binding.Transaction = ""; binding.TransactionSession = ""; binding.VenueSession = ""; binding.Sequence = 0; binding.Status = ""; binding.HasBalance = false; binding.Withdrawn = false; }
            }
        }

        internal void Invalidate()
        { lock (_gate) { _session = ""; _books.Clear(); } }

        /// <summary>Captures local receipt/generation and closes readiness on a received disconnect, before dispatch.</summary>
        internal Frame Capture(string xml, DateTime receivedAt)
        {
            lock (_gate)
            {
                Frame frame = new Frame { Xml = xml, Session = _session, Sequence = checked(++_sequence), ReceivedAt = receivedAt };
                // Readiness closes at source receipt, before converter/AServer queues can delay the status event.
                if (xml.StartsWith("<server_status", StringComparison.Ordinal)
                    && (xml.Contains("connected=\"false\"") || xml.Contains("recover=\"true\""))) Invalidate();
                return frame;
            }
        }

        internal void Enqueue(string xml, DateTime receivedAt, ConcurrentQueue<Frame> queue)
        {
            // Sequence assignment and queue insertion are one operation even with concurrent native callbacks.
            lock (_gate) queue.Enqueue(Capture(xml, receivedAt));
        }

        /// <summary>Registers persisted ownership and returns buffered matching facts; never sends a command.</summary>
        /// <remarks>Caller publishes the returned observations after the protocol lock is released. A reference
        /// collision throws rather than adopting another native order. THG-TRANSAQ-IMPLEMENTATION-006.</remarks>
        internal List<object> Register(Order order, Func<string, string, string, Security> security)
        {
            lock (_gate)
            {
                if (!order.UsesSignedPrice || order.SignedIdentity == null || !IsReference(order.SignedIdentity.ClientKey))
                    throw new InvalidOperationException("A persisted signed broker reference is required.");
                string key = order.SignedIdentity.ClientKey;
                if (_orders.TryGetValue(key, out Binding existing))
                {
                    if (!SameCommand(existing.Template, order)) throw new InvalidOperationException("Signed broker reference collision.");
                }
                else _orders.Add(key, new Binding { Template = Copy(order), Venue = order.NumberMarket ?? "", Executed = order.SignedIdentity.Executed, State = order.State });
                List<object> output = new List<object>();
                for (int index = 0; index < _unmatched.Count; index++)
                {
                    Frame frame = _unmatched[index];
                    XElement node = XElement.Parse(frame.Xml);
                    if (frame.ClientKey != key) continue;
                    _unmatched.RemoveAt(index--); ParseOwned(node, frame, output, false, frame.ClientKey);
                }
                return output;
            }
        }

        internal static bool IsReference(string value) => value != null && value.StartsWith("F2", StringComparison.Ordinal);

        internal static bool SameCommand(Order first, Order second) => first.NumberUser == second.NumberUser
            && first.SecurityNameCode == second.SecurityNameCode && first.SecurityClassCode == second.SecurityClassCode
            && first.PortfolioNumber == second.PortfolioNumber && first.Side == second.Side
            && first.TypeOrder == second.TypeOrder && first.Price == second.Price && first.Volume == second.Volume;

        internal static string NewOrderCommand(Order order, Security security, bool unionProfile)
        {
            if (security == null || security.NameClass != "FUT" || security.Name != order.SecurityNameCode
                || security.NameClass != order.SecurityClassCode || security.PriceStep <= 0 || security.Lot <= 0
                || order.Volume <= 0 || order.Volume != decimal.Truncate(order.Volume)
                || order.TypeOrder != OrderPriceType.Limit && order.TypeOrder != OrderPriceType.Market
                || order.TypeOrder == OrderPriceType.Limit && order.Price % security.PriceStep != 0
                || order.Side != Side.Buy && order.Side != Side.Sell || order.SignedIdentity == null)
                throw new InvalidOperationException("Signed TRANSAQ command metadata is invalid.");
            bool union = order.PortfolioNumber?.StartsWith("United_", StringComparison.Ordinal) == true;
            if (union != unionProfile || string.IsNullOrEmpty(order.PortfolioNumber) || union && order.PortfolioNumber.Length == 7)
                throw new InvalidOperationException("Signed TRANSAQ account profile mismatch.");
            XElement command = new XElement("command", new XAttribute("id", "neworder"),
                new XElement("security", new XElement("board", "FUT"), new XElement("seccode", order.SecurityNameCode)),
                new XElement(union ? "union" : "client", union ? order.PortfolioNumber.Substring(7) : order.PortfolioNumber),
                order.TypeOrder == OrderPriceType.Market ? new XElement("bymarket") : new XElement("price", Number(order.Price)),
                new XElement("quantity", Number(order.Volume)), new XElement("buysell", order.Side == Side.Buy ? "B" : "S"),
                new XElement("brokerref", order.SignedIdentity.ClientKey), new XElement("unfilled", "PutInQueue"));
            return command.ToString(SaveOptions.DisableFormatting);
        }

        /// <summary>Applies one source frame under the protocol lock and returns detached native observations.</summary>
        /// <remarks>Stale account/quotes are ignored; identified late executions survive. Invalid identities,
        /// units or buffer exhaustion throw so the realization invalidates readiness without logging raw XML.
        /// This is not a complete history/reconciliation service. THG-TRANSAQ-IMPLEMENTATION-006.</remarks>
        internal List<object> Parse(Frame frame, Func<string, string, string, Security> security)
        {
            lock (_gate)
            {
                List<object> output = new List<object>();
                XElement root = XElement.Parse(frame.Xml);
                string name = root.Name.LocalName;
                if (name == "orders" || name == "trades")
                {
                    foreach (XElement node in root.Elements()) ParseOwned(node, frame, output, true);
                    return output; // Late owned fills survive connection invalidation.
                }
                if (string.IsNullOrEmpty(_session) || frame.Session != _session) return output;
                if (name == "quotes") ParseQuotes(root, frame, security, output);
                else if (name == "mc_portfolio" && UnionProfile) ParsePortfolio(root, frame, security, output);
                else if (name == "positions") ParsePositions(root, frame, security, output);
                else if (name == "clientlimits" && !UnionProfile && TryDecimal(root, "money_free", out decimal free))
                    AddAccount(output, Attr(root, "client"), free, true, Array.Empty<PositionOnBoard>(), frame);
                return output;
            }
        }

        private string Reference(XElement node, Frame frame)
        {
            string key = Text(node, "brokerref");
            if (key.Length > 0) return key;
            if (node.Name.LocalName != "order" || string.IsNullOrEmpty(frame.Session)) return "";
            string transaction = Attr(node, "transactionid"); string venue = Text(node, "orderno");
            IEnumerable<string> registered = _orders.Where(pair =>
                transaction.Length > 0 && pair.Value.TransactionSession == frame.Session && pair.Value.Transaction == transaction
                || venue.Length > 0 && venue != "0" && pair.Value.VenueSession == frame.Session && pair.Value.Venue == venue)
                .Select(pair => pair.Key);
            // Startup callbacks can precede durable ownership registration. Keep deltas attached to
            // the unique broker reference already witnessed in this bounded, same-session buffer.
            IEnumerable<string> buffered = _unmatched.Where(saved => saved.Session == frame.Session).Where(saved =>
            {
                XElement prior = XElement.Parse(saved.Xml);
                return transaction.Length > 0 && Attr(prior, "transactionid") == transaction
                    || venue.Length > 0 && venue != "0" && Text(prior, "orderno") == venue;
            }).Select(saved => saved.ClientKey);
            string[] matches = registered.Concat(buffered).Distinct(StringComparer.Ordinal).ToArray();
            if (matches.Length > 1) throw new InvalidOperationException("Ambiguous signed TRANSAQ delta identity.");
            return matches.Length == 1 ? matches[0] : "";
        }

        internal string LegacyPayload(Frame frame)
        {
            lock (_gate)
            {
                if (!frame.Xml.StartsWith("<orders>", StringComparison.Ordinal) && !frame.Xml.StartsWith("<trades>", StringComparison.Ordinal)) return frame.Xml;
                XElement root = XElement.Parse(frame.Xml);
                foreach (XElement node in root.Elements().ToArray())
                    if (IsReference(Reference(node, frame))) node.Remove();
                return root.HasElements ? root.ToString(SaveOptions.DisableFormatting) : "";
            }
        }

        private void ParseOwned(XElement node, Frame frame, List<object> output, bool buffer, string replayKey = null)
        {
            // Buffered ownership was resolved at capture processing, before a possible session switch.
            // Replaying that fact must not re-authorize its old transaction for a new-session cancel.
            string key = replayKey ?? Reference(node, frame);
            if (!IsReference(key)) return;
            if (!_orders.TryGetValue(key, out Binding binding))
            {
                if (buffer)
                {
                    if (_unmatched.Count >= 1024) { Invalidate(); throw new InvalidOperationException("Signed TRANSAQ unmatched evidence buffer exhausted; owner reconciliation required."); }
                    _unmatched.Add(new Frame { Xml = node.ToString(SaveOptions.DisableFormatting), Session = frame.Session, Sequence = frame.Sequence, ReceivedAt = frame.ReceivedAt, ClientKey = key });
                }
                return;
            }
            Order original = binding.Template;
            bool trade = node.Name.LocalName == "trade";
            if (!trade && node.Name.LocalName != "order") return;
            if (trade || node.Element("union") != null || node.Element("client") != null)
            {
                // A union delta may contain the client as well; only the selected routing namespace is authoritative.
                string routing = original.PortfolioNumber.StartsWith("United_", StringComparison.Ordinal)
                    ? Text(node, "union").Length > 0 ? "United_" + Text(node, "union") : ""
                    : Text(node, "client");
                if ((trade || routing.Length > 0) && routing != original.PortfolioNumber)
                    throw new InvalidOperationException("Signed TRANSAQ callback account mismatch.");
            }
            if ((trade || node.Element("seccode") != null) && Text(node, "seccode") != original.SecurityNameCode
                || (trade || node.Element("board") != null) && Text(node, "board") != original.SecurityClassCode
                || (trade || node.Element("buysell") != null) && Text(node, "buysell") != (original.Side == Side.Buy ? "B" : "S"))
                throw new InvalidOperationException("Signed TRANSAQ callback ownership mismatch.");
            string venue = Text(node, "orderno");
            if (venue == "0") venue = "";
            if (venue.Length > 0 && binding.Venue.Length > 0 && venue != binding.Venue)
                throw new InvalidOperationException("Signed TRANSAQ venue identity changed; explicit reconciliation required.");
            if (venue.Length > 0)
            {
                binding.Venue = venue;
                if (binding.VenueSession.Length == 0 || frame.Session == _session) binding.VenueSession = frame.Session;
            }
            SignedOrderIdentity identity = new SignedOrderIdentity { ClientKey = key, Session = frame.Session,
                Sequence = frame.Sequence, ReceivedAt = frame.ReceivedAt, Executed = binding.Executed };
            if (trade)
            {
                if (venue.Length == 0 || !TryDecimal(node, "quantity", out decimal quantity) || quantity <= 0
                    || !TryDecimal(node, "price", out decimal price) || Text(node, "tradeno").Length == 0)
                    throw new InvalidOperationException("Signed TRANSAQ fill lacks required facts.");
                Order observed = Copy(original); observed.NumberMarket = binding.Venue;
                observed.State = binding.State == OrderStateType.None ? OrderStateType.Pending : binding.State;
                observed.SignedIdentity = identity; output.Add(observed);
                output.Add(new MyTrade { SecurityNameCode = original.SecurityNameCode, NumberOrderParent = venue,
                    NumberTrade = Text(node, "tradeno"), Volume = quantity, Price = price, Side = original.Side,
                    Time = ParseTime(Text(node, "time"), frame.ReceivedAt), SignedIdentity = identity });
                return;
            }
            if (node.Element("quantity") != null && (!TryDecimal(node, "quantity", out decimal volume) || volume != original.Volume))
                throw new InvalidOperationException("Signed TRANSAQ order quantity mismatch.");
            if (node.Element("balance") != null)
            {
                if (!TryDecimal(node, "balance", out decimal balance) || balance < 0 || balance > original.Volume)
                    throw new InvalidOperationException("Signed TRANSAQ order balance invalid.");
                binding.Executed = Math.Max(binding.Executed, original.Volume - balance); binding.HasBalance = true;
            }
            string transaction = Attr(node, "transactionid");
            if (frame.Session == _session && _session.Length > 0 && frame.Sequence >= binding.Sequence)
            {
                if (transaction.Length > 0) { binding.Transaction = transaction; binding.TransactionSession = frame.Session; }
                binding.Sequence = frame.Sequence;
            }
            if (node.Element("status") != null) binding.Status = Text(node, "status");
            if (node.Element("withdrawtime") != null) binding.Withdrawn = HasActualTime(node, "withdrawtime", frame.ReceivedAt);
            OrderStateType state = binding.Status switch
            {
                "forwarding" or "wait" or "watching" => OrderStateType.Pending,
                "active" => binding.Venue.Length == 0 ? OrderStateType.Pending : binding.Executed > 0 ? OrderStateType.Partial : OrderStateType.Active,
                "matched" => OrderStateType.Done,
                "cancelled" => binding.Withdrawn ? OrderStateType.Cancel : OrderStateType.Pending,
                "expired" or "disabled" or "removed" => OrderStateType.Cancel,
                "denied" or "rejected" or "failed" => OrderStateType.Fail,
                _ => OrderStateType.LostAfterActive
            };
            if (state == OrderStateType.Done && !binding.HasBalance) binding.Executed = original.Volume;
            if (state == OrderStateType.Cancel && !binding.HasBalance) state = OrderStateType.LostAfterActive;
            bool terminal = binding.State == OrderStateType.Done || binding.State == OrderStateType.Cancel || binding.State == OrderStateType.Fail;
            if (!terminal || state == OrderStateType.Done) binding.State = state;
            identity.Transaction = binding.Transaction; identity.Executed = binding.Executed;
            Order result = Copy(original); result.NumberMarket = binding.Venue; result.State = binding.State;
            result.VolumeExecute = binding.Executed; result.TimeCallBack = frame.ReceivedAt; result.SignedIdentity = identity;
            output.Add(result);
        }

        /// <summary>Separates an accepted transaction from order confirmation; absent/malformed responses stay unknown.</summary>
        /// <remarks>Already witnessed callbacks take precedence over an ambiguous synchronous result. A T from
        /// another session cannot authorize cancellation. No retry is performed. THG-TRANSAQ-IMPLEMENTATION-006.</remarks>
        internal Order ObserveResult(Order order, string response, string session, DateTime receivedAt)
        {
            lock (_gate)
            {
                Binding binding = _orders[order.SignedIdentity.ClientKey];
                bool accepted = false; bool rejected = false; string transaction = "";
                try
                {
                    XElement root = XElement.Parse(response ?? "");
                    accepted = root.Name == "result" && Attr(root, "success") == "true" && Attr(root, "transactionid").Length > 0;
                    rejected = root.Name == "result" && Attr(root, "success") == "false";
                    transaction = Attr(root, "transactionid");
                }
                catch (System.Xml.XmlException) { /* A malformed response is an unknown outcome, never a rejection. */ }
                if (accepted && session == _session && binding.TransactionSession != session)
                { binding.Transaction = transaction; binding.TransactionSession = session; }
                bool witnessed = binding.Sequence > 0 && binding.TransactionSession == session;
                if (!witnessed) binding.State = rejected ? OrderStateType.Fail : accepted ? OrderStateType.Pending : OrderStateType.LostAfterActive;
                Order result = Copy(binding.Template); result.State = binding.State; result.NumberMarket = binding.Venue;
                result.SignedIdentity = new SignedOrderIdentity { ClientKey = order.SignedIdentity.ClientKey,
                    Transaction = binding.Transaction, Session = session, ReceivedAt = receivedAt, Executed = binding.Executed };
                return result;
            }
        }

        /// <summary>Builds a cancel only for exactly registered ownership and a transaction proven in this session.</summary>
        /// <remarks>Throws on missing proof. The resulting command or its accepted response is not terminal
        /// cancellation; the confirmed order delta supplies that fact. THG-TRANSAQ-IMPLEMENTATION-006.</remarks>
        internal string CancelCommand(Order order)
        {
            lock (_gate)
            {
                if (order.SignedIdentity == null || !_orders.TryGetValue(order.SignedIdentity.ClientKey, out Binding binding)
                    || !SameCommand(binding.Template, order) || string.IsNullOrEmpty(_session)
                    || binding.TransactionSession != _session || string.IsNullOrEmpty(binding.Transaction))
                    throw new InvalidOperationException("No current-session transaction proof for signed cancellation.");
                return new XElement("command", new XAttribute("id", "cancelorder"), new XElement("transactionid", binding.Transaction)).ToString(SaveOptions.DisableFormatting);
            }
        }

        private void ParseQuotes(XElement root, Frame frame, Func<string, string, string, Security> resolver, List<object> output)
        {
            HashSet<string> changed = new HashSet<string>(StringComparer.Ordinal);
            foreach (XElement row in root.Elements("quote"))
            {
                Security security = resolver(Text(row, "seccode"), Text(row, "board"), Attr(row, "secid"));
                if (security == null || security.NameClass != "FUT") continue;
                if (Text(row, "source").Length > 0) throw new InvalidOperationException("Source-keyed depth is outside the signed FUT profile.");
                if (!TryDecimal(row, "price", out decimal price)) throw new InvalidOperationException("Signed depth price is missing.");
                if (!_books.TryGetValue(security.Name, out Book book)) { book = new Book(); _books.Add(security.Name, book); }
                Patch(book.Bids, row, "buy", price); Patch(book.Asks, row, "sell", price); changed.Add(security.Name);
            }
            foreach (string instrument in changed)
            {
                Book book = _books[instrument];
                output.Add(new ExplicitQuote(instrument, book.Bids.Any(p => p.Value > 0), book.Bids.Any(p => p.Value > 0) ? book.Bids.Last(p => p.Value > 0).Key : 0,
                    book.Asks.Any(p => p.Value > 0), book.Asks.Any(p => p.Value > 0) ? book.Asks.First(p => p.Value > 0).Key : 0, frame.ReceivedAt,
                    frame.ReceivedAt, frame.Session, frame.Sequence));
            }
        }

        private static void Patch(SortedDictionary<decimal, decimal> side, XElement row, string name, decimal price)
        {
            if (row.Element(name) == null) return;
            if (!TryDecimal(row, name, out decimal quantity)) throw new InvalidOperationException("Invalid signed depth quantity.");
            if (quantity == -1) side.Remove(price);
            else if (quantity >= 0) side[price] = quantity;
            else throw new InvalidOperationException("Invalid signed depth quantity.");
        }

        private static void ParsePortfolio(XElement root, Frame frame, Func<string, string, string, Security> resolver, List<object> output)
        {
            string union = Attr(root, "union");
            if (union.Length == 0) return;
            List<PositionOnBoard> positions = new List<PositionOnBoard>();
            foreach (XElement row in root.Descendants("security"))
            {
                Security security = resolver(Text(row, "seccode"), "FUT", Attr(row, "secid"));
                if (security == null || security.NameClass != "FUT") continue;
                if (security.Lot <= 0 || !TryDecimal(row, "balance", out decimal units))
                    throw new InvalidOperationException("Signed portfolio units are missing.");
                positions.Add(new PositionOnBoard { SecurityNameCode = security.Name, ValueCurrent = units / security.Lot });
            }
            AddAccount(output, "United_" + union, 0, false, positions, frame, true);
        }

        private void ParsePositions(XElement root, Frame frame, Func<string, string, string, Security> resolver, List<object> output)
        {
            if (UnionProfile)
            {
                foreach (XElement row in root.Elements("united_limits"))
                    if (Attr(row, "union").Length > 0 && TryDecimal(row, "free", out decimal free))
                        AddAccount(output, "United_" + Attr(row, "union"), free, true, Array.Empty<PositionOnBoard>(), frame);
                return; // Union inventory has one authoritative refresh source: mc_portfolio.balance.
            }
            foreach (XElement row in root.Elements("forts_position"))
            {
                if (Text(row, "union").Length > 0) continue;
                Security security = resolver(Text(row, "seccode"), "FUT", Text(row, "secid"));
                if (security == null || !TryDecimal(row, "totalnet", out decimal net)) continue;
                AddAccount(output, Text(row, "client"), 0, false,
                    new[] { new PositionOnBoard { SecurityNameCode = security.Name, ValueCurrent = net } }, frame);
            }
        }

        private static void AddAccount(List<object> output, string account, decimal free, bool funds, IEnumerable<PositionOnBoard> positions, Frame frame, bool complete = false)
        {
            if (string.IsNullOrEmpty(account)) throw new InvalidOperationException("Signed account identity missing.");
            output.Add(new ExplicitAccount(new Portfolio { Number = account, ValueCurrent = free }, funds, positions,
                frame.ReceivedAt, true, frame.Session, frame.Sequence, complete));
        }

        internal static Order Copy(Order order) => new Order { NumberUser = order.NumberUser, NumberMarket = order.NumberMarket,
            NumberPosition = order.NumberPosition, SecurityNameCode = order.SecurityNameCode, SecurityClassCode = order.SecurityClassCode,
            PortfolioNumber = order.PortfolioNumber, Side = order.Side, Volume = order.Volume, Price = order.Price,
            TypeOrder = order.TypeOrder, UsesSignedPrice = order.UsesSignedPrice, SignedPercentBase = order.SignedPercentBase,
            SignedIdentity = order.SignedIdentity, State = order.State, ServerType = order.ServerType, TimeCreate = order.TimeCreate };

        private static string Text(XElement node, string name) => node.Element(name)?.Value.Trim() ?? "";
        private static string Attr(XElement node, string name) => node.Attribute(name)?.Value.Trim() ?? "";
        private static string Account(XElement node) => Text(node, "union").Length > 0 ? "United_" + Text(node, "union") : Text(node, "client");
        private static bool TryDecimal(XElement node, string name, out decimal value) => decimal.TryParse(Text(node, name), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
        private static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);
        private static bool HasActualTime(XElement node, string name, DateTime receivedAt)
        {
            string text = Text(node, name);
            return text.Length > 0 && text != "0" && ParseTime(text, receivedAt).Year > 1;
        }
        private static DateTime ParseTime(string value, DateTime receivedAt)
        {
            DateTime time = DateTime.Parse(value, CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.NoCurrentDateDefault);
            return time.Year == 1 ? receivedAt.Date + time.TimeOfDay : time;
        }
    }
}
