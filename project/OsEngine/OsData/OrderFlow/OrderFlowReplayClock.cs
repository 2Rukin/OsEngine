/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Diagnostics;

namespace OsEngine.OsData.OrderFlow
{
    /// <summary>Thread-safe source-time playback clock; never changes tick timestamps or their order.</summary>
    /// <remarks>Speed changes preserve accrued virtual time. Pause freezes it; Step consumes one tick even at equal timestamps.</remarks>
    internal sealed class OrderFlowReplayClock
    {
        #region State and controls

        private readonly object _gate = new object();
        private readonly Func<double> _seconds;
        private double _lastWall;
        private double _position;
        private DateTime? _origin;
        private DateTime? _lastTick;
        private double _speed;
        private bool _paused;
        private bool _skipGaps;
        private int _steps;

        public OrderFlowReplayClock(double speed, bool skipGaps, Func<double> seconds = null)
        {
            ValidateSpeed(speed);
            _seconds = seconds ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
            _lastWall = _seconds();
            _speed = speed;
            _skipGaps = skipGaps;
        }

        public bool Paused { get { lock (_gate) { return _paused; } } }
        internal bool WaitingForStep { get { lock (_gate) { return _paused && _steps == 0; } } }

        public void SetSpeed(double speed)
        {
            ValidateSpeed(speed);
            lock (_gate) { Accrue(); _speed = speed; }
        }

        public void SetPaused(bool paused)
        {
            lock (_gate) { Accrue(); _paused = paused; _steps = 0; }
        }

        public void SetSkipGaps(bool skip) { lock (_gate) { _skipGaps = skip; } }

        public void Step()
        {
            lock (_gate) { Accrue(); _paused = true; if (_steps < int.MaxValue) { _steps++; } }
        }

        #endregion

        #region Advancement

        /// <summary>Consumes permission for exactly one source tick, or returns false while waiting/paused.</summary>
        /// <param name="time">Next validated, selected tick timestamp in nondecreasing source order.</param>
        public bool TryAdvanceTo(DateTime time)
        {
            lock (_gate)
            {
                Accrue();
                if (_paused && _steps == 0) { return false; }
                if (!_origin.HasValue) { _origin = time; _position = 0; _lastWall = _seconds(); }
                double target = (time.Ticks - _origin.Value.Ticks) / (double)TimeSpan.TicksPerSecond;
                if (_steps > 0)
                {
                    _steps--;
                    _position = target;
                }
                else if (_skipGaps && _lastTick.HasValue && (time - _lastTick.Value).TotalSeconds > 60)
                {
                    _position = Math.Max(_position, target);
                }
                if (_position < target) { return false; }
                _lastTick = time;
                return true;
            }
        }

        private void Accrue()
        {
            double now = _seconds();
            if (!_paused && _origin.HasValue) { _position += Math.Max(0, now - _lastWall) * _speed; }
            _lastWall = now;
        }

        private static void ValidateSpeed(double speed)
        {
            if (!double.IsFinite(speed) || speed < 0.25 || speed > 1000) { throw new ArgumentOutOfRangeException(nameof(speed)); }
        }

        #endregion
    }
}
