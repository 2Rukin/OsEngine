/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Text;
using System.Text.Json;

namespace OsEngine.Entity
{
    /// <summary>Optional durable correlation for signed orders; native, transport and venue numbers remain distinct.</summary>
    /// <remarks>THG-TRANSAQ-PLAN-001. A saved transaction number is observation evidence only and cannot
    /// authorize cancellation in a new connection. No account identifier or authenticated payload belongs here.</remarks>
    public sealed class SignedOrderIdentity
    {
        /// <summary>Opaque broker reference fixed and persisted before the first possible external effect.</summary>
        public string ClientKey { get; set; } = "";
        /// <summary>Transport transaction number, scoped to Session.</summary>
        public string Transaction { get; set; } = "";
        /// <summary>Opaque connection generation of this observation.</summary>
        public string Session { get; set; } = "";
        /// <summary>Monotone callback capture sequence within Session, before any dispatch queue.</summary>
        public long Sequence { get; set; }
        /// <summary>Original local callback receipt time.</summary>
        public DateTime ReceivedAt { get; set; }
        /// <summary>Broker-reported cumulative execution, independent of identified trade details.</summary>
        public decimal Executed { get; set; }

        /// <summary>Encodes an optional versioned trailing native serialization field without delimiter collisions.</summary>
        public static string Save(SignedOrderIdentity identity) => identity == null ? "" :
            "1:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(identity)));

        /// <summary>Reads the additive field; old records have no identity. Unknown or malformed formats fail closed.</summary>
        public static SignedOrderIdentity Load(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            if (!value.StartsWith("1:", StringComparison.Ordinal)) throw new FormatException("Unsupported signed order identity format.");
            SignedOrderIdentity result = JsonSerializer.Deserialize<SignedOrderIdentity>(Encoding.UTF8.GetString(Convert.FromBase64String(value.Substring(2))));
            if (result == null || string.IsNullOrEmpty(result.ClientKey) || result.Sequence < 0 || result.Executed < 0)
                throw new FormatException("Invalid signed order identity.");
            return result;
        }

        /// <summary>Requires both opted-in identities to agree; two legacy records retain legacy matching.</summary>
        public static bool Same(SignedOrderIdentity first, SignedOrderIdentity second) =>
            first == null && second == null || first != null && second != null
            && !string.IsNullOrEmpty(first.ClientKey) && first.ClientKey == second.ClientKey;
    }

    /// <summary>Process-local authority for one queued signed send; never serialized or recreated for an unknown outcome.</summary>
    /// <remarks>The physical transport executes Run while holding its command serialization lock. Owner validation,
    /// cancellation and the start of the external effect serialize on the owner's gate. A thrown effect stays Started.</remarks>
    public sealed class SignedOrderDispatch
    {
        private readonly object _gate;
        private readonly Func<bool> _valid;
        private readonly Action _notSent;
        private int _state;

        /// <summary>Binds a frozen command's current authority and a callback for a proven suppressed send.</summary>
        public SignedOrderDispatch(object gate, Func<bool> valid, Action notSent)
        { _gate = gate ?? throw new ArgumentNullException(nameof(gate)); _valid = valid ?? throw new ArgumentNullException(nameof(valid)); _notSent = notSent ?? throw new ArgumentNullException(nameof(notSent)); }

        /// <summary>True once the physical effect started, including a lost or exceptional response.</summary>
        public bool Started { get { lock (_gate) return _state == 1; } }

        /// <summary>Suppresses a command still held locally. False does not prove any broker cancellation.</summary>
        public bool CancelBeforeSend()
        {
            lock (_gate)
            {
                if (_state == 1) return false;
                if (_state == 0) { _state = 2; _notSent(); }
                return true;
            }
        }

        /// <summary>Performs at most one effect after the final authority check. False means no effect from this call.</summary>
        public bool Run(Action effect)
        {
            lock (_gate)
            {
                if (_state != 0) return false;
                if (!_valid()) { _state = 2; _notSent(); return false; }
                _state = 1;
                effect();
                return true;
            }
        }
    }
}
