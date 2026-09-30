using OsEngine.Entity;

namespace OsEngine.Market.Servers
{
    /// <summary>Optional opt-in transport contract for durable signed-order correlation and original observation clocks.</summary>
    /// <remarks>Capabilities do not establish a complete reconnect replay or broker qualification.
    /// Registering a journal order never authorizes submission and never replaces reconciliation.</remarks>
    public interface ISignedOrderSource
    {
        /// <summary>True only for the explicitly enabled, currently supported connection profile.</summary>
        bool HasSignedOrderTransport { get; }
        /// <summary>Opaque generation captured at the source; empty means no usable observation session.</summary>
        string SignedOrderSession { get; }
        /// <summary>Registers an already persisted native identity for exact broker-reference correlation.</summary>
        void RegisterSignedOrder(Order order);
    }
}
