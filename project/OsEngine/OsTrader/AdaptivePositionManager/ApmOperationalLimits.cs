using System;
using System.Diagnostics;

namespace OsEngine.OsTrader.AdaptivePositionManager
{
    /// <summary>
    /// Finite lifetime budget for the verified native one-full-fill-per-intent model. Full identities are
    /// retained; no terminal order or fill is evicted. This bound is not a generic partial-fill capability.
    /// </summary>
    public sealed record ApmOperationalLimits(int OrdinaryIntents = 256, int ExitIntents = 257)
    {
        /// <summary>Reject budgets without room to close every possible late native increase plus one terminal retry.</summary>
        public void Validate()
        {
            if (OrdinaryIntents < 1 || OrdinaryIntents > 4096 || ExitIntents < OrdinaryIntents + 1 || ExitIntents > 8192)
                throw new ArgumentException("Invalid native execution lifetime budget.");
        }
    }

    /// <summary>Wall-clock replay health, separate from event-time trading decisions and session cutoff.</summary>
    public sealed record ApmReplayHealth(bool Stalled, double SecondsSinceCallback, long Callbacks);

    /// <summary>
    /// Passive monotonic watchdog. Delayed/paused Tester/Optimizer never changes inventory because of wall time.
    /// Live reconnect/protection needs its own authorized connector capability and is not implemented here.
    /// </summary>
    public sealed class ApmReplayWatchdog
    {
        private readonly Func<long> _clock;
        private readonly long _frequency;
        private readonly object _locker = new object();
        private long _last;
        private long _callbacks;

        /// <summary>Use Stopwatch by default; injected ticks/frequency permit exact independent timeout tests.</summary>
        public ApmReplayWatchdog(Func<long> monotonicClock = null, long frequency = 0)
        {
            _clock = monotonicClock ?? Stopwatch.GetTimestamp;
            _frequency = monotonicClock == null ? Stopwatch.Frequency : frequency;
            if (_frequency <= 0) throw new ArgumentException("Positive monotonic clock frequency required.");
            _last = _clock();
        }

        /// <summary>Record callback progress only; never refresh a market price or reset a trading warmup.</summary>
        public void Observe() { lock (_locker) { _last = _clock(); _callbacks++; } }

        /// <summary>Read elapsed process time. A stalled replay is diagnostic, not an order or an exchange disconnect.</summary>
        public ApmReplayHealth Read(TimeSpan staleAfter)
        {
            if (staleAfter <= TimeSpan.Zero) throw new ArgumentException("Positive diagnostic threshold required.");
            lock (_locker)
            {
                long now = _clock();
                if (now < _last) throw new InvalidOperationException("Watchdog clock must be monotonic.");
                double elapsed = (double)(now - _last) / _frequency;
                return new ApmReplayHealth(elapsed >= staleAfter.TotalSeconds, elapsed, _callbacks);
            }
        }
    }
}
