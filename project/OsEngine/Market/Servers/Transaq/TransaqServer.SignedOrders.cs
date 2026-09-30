/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Linq;
using OsEngine.Entity;
using OsEngine.Logging;
using OsEngine.Market.Servers.Entity;

namespace OsEngine.Market.Servers.Transaq
{
    /// <summary>Optional signed FUT transport inside the existing TRANSAQ realization.</summary>
    /// <remarks>THG-TRANSAQ-IMPLEMENTATION-006. Native callbacks capture receipt/generation before queues;
    /// managed parsing owns correlation and publishes outside its state lock. Physical sends serialize on
    /// the existing command lock and then the strategy authority gate. Reconnect does not establish a
    /// complete replay. Unknown outcomes retain ownership; native DLL/server compatibility needs owner-run evidence.</remarks>
    public partial class TransaqServerRealization : IExplicitQuoteSource, IExplicitAccountSource, ISignedOrderSource
    {
        private readonly TransaqSignedProtocol _signedProtocol = new TransaqSignedProtocol();
        private string _signedProfile = "Off";
        private ServerParameterEnum _signedProfileParameter;

        /// <summary>Enabled standard TRANSAQ FUT profile; a parameter change invalidates authority until reconnect.</summary>
        public bool HasSignedOrderTransport => _signedProfile != "Off" && ServerParameters != null
            && ServerParameters.Count > 15 && ((ServerParameterEnum)ServerParameters[15]).Value == _signedProfile;

        /// <summary>Original source generation; disconnect/profile changes make queued observations unusable for readiness.</summary>
        public string SignedOrderSession => HasSignedOrderTransport ? _signedProtocol.Session : "";

        /// <summary>Actual row-presence observations are available only for the explicitly selected signed profile.</summary>
        public bool HasExplicitAccountUpdates => HasSignedOrderTransport;

        /// <summary>Exact decimal depth-side observations, including deletion and literal zero prices.</summary>
        public event Action<ExplicitQuote> ExplicitQuoteEvent;

        /// <summary>Actual account rows and typed broker free, never a republished portfolio cache.</summary>
        public event Action<ExplicitAccount> ExplicitAccountEvent;

        private void BeginSignedSession()
        {
            _signedProtocol.Invalidate();
            ServerParameterEnum parameter = ServerParameters.Count > 15 ? (ServerParameterEnum)ServerParameters[15] : null;
            if (!ReferenceEquals(parameter, _signedProfileParameter))
            {
                DetachSignedProfileParameter();
                _signedProfileParameter = parameter;
                if (parameter != null) parameter.ValueChange += SignedProfileChanged;
            }
            _signedProfile = parameter?.Value ?? "Off";
            if (_signedProfile == "Standard union" || _signedProfile == "Standard FORTS")
                _signedProtocol.Begin(_signedProfile == "Standard union");
            else _signedProfile = "Off";
        }

        // Latch every actual change at its event boundary. Switching back cannot revive queued authority.
        private void SignedProfileChanged() => _signedProtocol.Invalidate();

        private void DetachSignedProfileParameter()
        {
            if (_signedProfileParameter != null) _signedProfileParameter.ValueChange -= SignedProfileChanged;
            _signedProfileParameter = null;
        }

        private Security SignedSecurity(string name, string board, string id)
        {
            Security[] matches = (_securities ?? new List<Security>()).Where(s =>
                (string.IsNullOrEmpty(id) || s.NameId == id) && (string.IsNullOrEmpty(name) || s.Name == name)
                && (string.IsNullOrEmpty(board) || s.NameClass == board)).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        /// <summary>Rebinds durable native ownership without sending or reusing a persisted transaction number.</summary>
        public void RegisterSignedOrder(Order order)
        {
            PublishSigned(_signedProtocol.Register(order, SignedSecurity));
        }

        // This finite managed entry is shared by the converter and synthetic component fixtures.
        private void DispatchSignedFrame(TransaqSignedProtocol.Frame frame)
        {
            if (!frame.Xml.StartsWith("<", StringComparison.Ordinal) || !HasSignedOrderTransport && !_signedProtocol.HasRegistrations) return;
            try { PublishSigned(_signedProtocol.Parse(frame, SignedSecurity)); }
            catch (Exception)
            {
                _signedProtocol.Invalidate();
                // Raw XML/errors may contain account data. Publish only a bounded diagnosis.
                SendLogMessage("Signed TRANSAQ evidence invalid; reconnect and reconcile owned orders.", LogMessageType.Error);
            }
        }

        private void PublishSigned(List<object> observations)
        {
            foreach (object observation in observations)
            {
                if (observation is Order order) MyOrderEvent?.Invoke(order);
                else if (observation is MyTrade trade) MyTradeEvent?.Invoke(trade);
                else if (observation is ExplicitQuote quote) ExplicitQuoteEvent?.Invoke(quote);
                else if (observation is ExplicitAccount account) ExplicitAccountEvent?.Invoke(account);
            }
        }

        private void SendSignedOrder(Order order)
        {
            bool started = order.SignedDispatch == null || order.SignedDispatch.Started;
            try
            {
                if (!HasSignedOrderTransport || string.IsNullOrEmpty(SignedOrderSession) || order.SignedDispatch == null)
                    throw new InvalidOperationException("Signed TRANSAQ transport authority is unavailable.");
                RegisterSignedOrder(order);
                string command = TransaqSignedProtocol.NewOrderCommand(order,
                    SignedSecurity(order.SecurityNameCode, order.SecurityClassCode, ""), _signedProtocol.UnionProfile);
                string session = SignedOrderSession;
                string response = null;
                lock (_commandLocker)
                {
                    bool sent = order.SignedDispatch.Run(() =>
                    {
                        if (!HasSignedOrderTransport || SignedOrderSession != session || ServerStatus != ServerConnectStatus.Connect)
                            throw new InvalidOperationException("Signed TRANSAQ session changed before dispatch.");
                        started = true;
                        response = SendSignedCommand(command);
                    });
                    if (!sent) return;
                }
                Order result = _signedProtocol.ObserveResult(order, response, session, DateTime.Now);
                if (result.State == OrderStateType.LostAfterActive) _signedProtocol.Invalidate();
                MyOrderEvent?.Invoke(result);
            }
            catch (Exception)
            {
                Order outcome = TransaqSignedProtocol.Copy(order);
                outcome.State = started ? OrderStateType.LostAfterActive : OrderStateType.Fail;
                if (started) _signedProtocol.Invalidate();
                MyOrderEvent?.Invoke(outcome);
                SendLogMessage(started ? "Signed TRANSAQ submit outcome unknown; reserve retained."
                    : "Signed TRANSAQ submission suppressed before external effect.", LogMessageType.Error);
            }
        }

        private bool CancelSignedOrder(Order order)
        {
            if (order.SignedDispatch?.CancelBeforeSend() == true) return true;
            try
            {
                lock (_commandLocker)
                {
                    if (!HasSignedOrderTransport || ServerStatus != ServerConnectStatus.Connect) return false;
                    string response = SendSignedCommand(_signedProtocol.CancelCommand(order));
                    // Acceptance is not terminal cancellation; only an identified callback changes the order state.
                    return response != null && response.StartsWith("<result success=\"true\"", StringComparison.Ordinal);
                }
            }
            catch (Exception)
            {
                SendLogMessage("Signed TRANSAQ cancellation lacks current-session proof; reconcile.", LogMessageType.Error);
                return false;
            }
        }

        /// <summary>Physical signed-command boundary, invoked under the command lock and final dispatch authority.</summary>
        /// <remarks>The default uses the existing native transport. Synthetic subclasses can supply deterministic
        /// responses without loading the DLL; such evidence does not qualify the native connection.</remarks>
        protected virtual string SendSignedCommand(string command) => ConnectorSendCommand(command);
    }
}
