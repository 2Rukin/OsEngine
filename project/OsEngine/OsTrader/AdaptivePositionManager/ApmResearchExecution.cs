using System;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace OsEngine.OsTrader.AdaptivePositionManager
{
    /// <summary>
    /// Pure published benchmarks, not calibrated trading recommendations. AS monetary gamma, AC lambda/
    /// permanent impact and dimensionless APM inventory penalty are distinct quantities.
    /// </summary>
    public static class ApmResearchExecution
    {
        /// <summary>SRC-AS reservation quote price for signed inventory; moneyPerPrice is the explicitly derived futures-unit adaptation.</summary>
        public static decimal ReservationPrice(decimal price, decimal signedInventory, decimal monetaryGamma,
            decimal priceVariancePerSecond, decimal remainingSeconds, decimal moneyPerPrice = 1)
        {
            if (monetaryGamma < 0 || priceVariancePerSecond < 0 || remainingSeconds < 0 || moneyPerPrice <= 0)
                throw new ArgumentException("Invalid AS reference units/domain.");
            return price - signedInventory * monetaryGamma * moneyPerPrice * priceVariancePerSecond * remainingSeconds;
        }

        /// <summary>
        /// SRC-AC equations16–18 discrete inventory x_j, using etaTilde=eta-gammaPermanent*dt/2.
        /// Lambda has inverse-money units, variance is monetary-price squared/time and kappa is1/time.
        /// Stable exponential ratios avoid sinh overflow; lambda=0 gives the exact linear limit.
        /// </summary>
        public static double[] DiscreteInventory(double quantity, double horizonSeconds, int steps,
            double lambda, double monetaryVariancePerSecond, double temporaryImpact, double permanentImpact)
        {
            if (!double.IsFinite(quantity) || !double.IsFinite(horizonSeconds) || !double.IsFinite(lambda)
                || !double.IsFinite(monetaryVariancePerSecond) || !double.IsFinite(temporaryImpact) || !double.IsFinite(permanentImpact)
                || quantity < 0 || horizonSeconds <= 0 || steps < 1 || steps > 10000 || lambda < 0
                || monetaryVariancePerSecond < 0 || temporaryImpact <= 0 || permanentImpact < 0)
                throw new ArgumentException("Invalid discrete AC domain.");
            double dt = horizonSeconds / steps;
            double adjustedImpact = temporaryImpact - permanentImpact * dt / 2;
            if (adjustedImpact <= 0) throw new ArgumentException("AC etaTilde must be positive.");
            double halfArgument = dt / 2 * Math.Sqrt(lambda * monetaryVariancePerSecond / adjustedImpact);
            double kappa = 2 * Math.Asinh(halfArgument) / dt;
            if (!double.IsFinite(kappa)) throw new ArgumentException("AC numerical range exceeded.");
            double[] inventory = new double[steps + 1];
            inventory[0] = quantity;
            for (int j = 1; j < steps; j++)
            {
                double remaining = horizonSeconds * ((double)(steps - j) / steps);
                double ratio = kappa == 0 ? (double)(steps - j) / steps
                    : kappa * horizonSeconds < 0.000001
                    ? Math.Sinh(kappa * remaining) / Math.Sinh(kappa * horizonSeconds)
                    : Math.Exp(-kappa * (horizonSeconds - remaining)) * OneMinusExp(-2 * kappa * remaining)
                        / OneMinusExp(-2 * kappa * horizonSeconds);
                inventory[j] = quantity * ratio;
                if (!double.IsFinite(inventory[j]) || inventory[j] < 0 || inventory[j] > inventory[j - 1])
                    throw new ArgumentException("AC trajectory is outside its finite monotone domain.");
            }
            return inventory;
        }

        private static double OneMinusExp(double negative) => Math.Abs(negative) < 0.000001
            ? -negative * (1 + negative / 2 + negative * negative / 6) : 1 - Math.Exp(negative);

        /// <summary>SRC-ADAPT equations4–5 realized demand I*c*(p-L). Negative linear values are retained; clipping is a separate model choice.</summary>
        public static decimal LinearDemand(bool marketArrival, decimal slope, decimal reservationDistance, decimal quoteDistance)
        {
            if (slope < 0 || reservationDistance < 0 || quoteDistance < 0) throw new ArgumentException("Invalid demand domain.");
            return marketArrival ? slope * (reservationDistance - quoteDistance) : 0;
        }
    }

    /// <summary>Past-only demand moments E[Icp] and E[Ic]; correlated c/p are never replaced by a product of their means.</summary>
    public sealed class ApmDemandCalibration
    {
        private long _samples;
        private decimal _ic;
        private decimal _icp;
        private DateTime _lastLabel;
        private readonly DateTime _cutoff;

        /// <summary>Freeze the last permitted label timestamp before calibration.</summary>
        public ApmDemandCalibration(DateTime trainingCutoff) { _cutoff = trainingCutoff; }

        /// <summary>Update from one matured realized-demand sample, including no-arrival intervals in the denominator.</summary>
        public void Add(DateTime labelTime, DateTime observedAt, DateTime asOf, bool marketArrival, decimal slope, decimal distance)
        {
            if (labelTime <= _lastLabel || labelTime > observedAt || observedAt > asOf || labelTime > _cutoff
                || slope < 0 || distance < 0) throw new ArgumentException("Noncausal or invalid demand sample.");
            decimal ic = _ic + (marketArrival ? slope : 0);
            decimal icp = _icp + (marketArrival ? slope * distance : 0);
            long samples = checked(_samples + 1);
            _ic = ic; _icp = icp; _samples = samples; _lastLabel = labelTime;
        }

        /// <summary>Expected linear demand in quantity units. Null means uncalibrated; negative values are not silently clipped.</summary>
        public decimal? Predict(decimal quoteDistance)
        {
            if (quoteDistance < 0) throw new ArgumentException("Quote distance must be nonnegative.");
            return _samples == 0 ? null : _icp / _samples - quoteDistance * _ic / _samples;
        }

        /// <summary>Last actually consumed label, not the configured future end of a file.</summary>
        public DateTime TrainingCutoff => _lastLabel;
    }

    /// <summary>Explicit offline AC schedule inputs; no fitted universal constants or order-book liquidity claims.</summary>
    public sealed record ApmAcSettings(int Steps, double HorizonSeconds, double Lambda, double MonetaryVariancePerSecond,
        double TemporaryImpact, double PermanentImpact, string CalibrationVersion, DateTime TrainingCutoff)
    {
        /// <summary>Load explicit research inputs only from an owner-selected small JSON file. Every field is required.</summary>
        public static ApmAcSettings Load(string path)
        {
            if (new FileInfo(path).Length > 32768) throw new InvalidDataException("Research settings exceed 32 KiB.");
            string json = File.ReadAllText(path);
            using JsonDocument document = JsonDocument.Parse(json);
            foreach (PropertyInfo property in typeof(ApmAcSettings).GetProperties())
                if (!document.RootElement.TryGetProperty(property.Name, out _)) throw new InvalidDataException("Missing explicit research input: " + property.Name);
            ApmAcSettings settings = JsonSerializer.Deserialize<ApmAcSettings>(json,
                new JsonSerializerOptions { RespectRequiredConstructorParameters = true });
            new ApmAcExecutionPlanner(settings);
            return settings;
        }
    }

    /// <summary>
    /// ResearchOnly ordinary-action pacing benchmark. No transport, target selection or risk-lock mutation.
    /// Replans from confirmed inventory whenever target/action changes; sent pending volume is deducted.
    /// Initial entry and every protective EXIT bypass the benchmark. Call only under the campaign owner lock.
    /// </summary>
    public sealed class ApmAcExecutionPlanner
    {
        private readonly ApmAcSettings _settings;
        private decimal _target;
        private decimal _startVolume;
        private DateTime _start;
        private ApmAction _action;
        private double[] _inventory;

        /// <summary>Require a versioned past calibration before starting an explicit research experiment.</summary>
        public ApmAcExecutionPlanner(ApmAcSettings settings)
        {
            if (settings == null || string.IsNullOrWhiteSpace(settings.CalibrationVersion) || settings.CalibrationVersion.Length > 128)
                throw new ArgumentException("Explicit research calibration required.");
            ApmResearchExecution.DiscreteInventory(1, settings.HorizonSeconds, settings.Steps, settings.Lambda,
                settings.MonetaryVariancePerSecond, settings.TemporaryImpact, settings.PermanentImpact);
            _settings = settings;
        }

        /// <summary>Return an on-grid cap ≤ current proposal. This is a benchmark only; the normal arbiter must still reserve and account for actual fills.</summary>
        public decimal AllowedVolume(ApmDecision decision, ApmSnapshot snapshot, decimal volumeStep)
        {
            if (volumeStep <= 0) throw new ArgumentException("Explicit volume grid required.");
            if (_inventory != null && (decision.RiskAllowedTarget != _target || snapshot.ExitLatch)) _inventory = null;
            if (decision.Action != ApmAction.Add && decision.Action != ApmAction.Reduce) return decision.Volume;
            if (_settings.TrainingCutoff >= decision.Time) throw new ArgumentException("Execution calibration must precede the decision.");
            int sign = decision.Action == ApmAction.Add ? 1 : -1;
            if (_inventory == null || _target != decision.RiskAllowedTarget || _action != decision.Action)
            {
                _target = decision.RiskAllowedTarget; _startVolume = snapshot.FilledVolume;
                _start = decision.Time; _action = decision.Action;
                _inventory = ApmResearchExecution.DiscreteInventory((double)Math.Abs(_target - _startVolume),
                    _settings.HorizonSeconds, _settings.Steps, _settings.Lambda, _settings.MonetaryVariancePerSecond,
                    _settings.TemporaryImpact, _settings.PermanentImpact);
            }
            if (decision.Time < _start) throw new ArgumentException("Execution planner time regressed.");
            int bucket = Math.Min(_settings.Steps, 1 + (int)Math.Min(_settings.Steps,
                Math.Floor((decision.Time - _start).TotalSeconds * _settings.Steps / _settings.HorizonSeconds)));
            decimal scheduled = Math.Abs(_target - _startVolume) - (decimal)_inventory[bucket];
            decimal executed = sign * (snapshot.FilledVolume - _startVolume);
            decimal pending = sign > 0 ? snapshot.PendingIncrease : snapshot.PendingReduce;
            decimal allowed = Math.Min(decision.Volume, Math.Max(0, scheduled - executed - pending));
            return Math.Floor(allowed / volumeStep) * volumeStep;
        }
    }
}
