using System;
using System.Collections.Generic;

namespace OsEngine.OsTrader.AdaptivePositionManager
{
    /// <summary>Explicit top-of-book observation in quote-price and book-volume units; sequence belongs to one certified source session.</summary>
    public sealed record ApmBookQuote(DateTime Time, long Sequence, decimal Bid, decimal BidSize, decimal Ask, decimal AskSize);

    /// <summary>Bounded-window research feature. Sampled snapshots remain a proxy; Ready never certifies a synchronized execution feed.</summary>
    public sealed record ApmBookFeature(DateTime Time, long Sequence, decimal EventOfi, decimal WindowOfi,
        decimal MeanDepth, decimal NormalizedOfi, decimal Mid, bool Ready, string Quality, ApmDataProfile Profile);

    /// <summary>
    /// SRC-OFI best-quote indicator formula with explicit reset/readiness. This research component is not
    /// wired to the TradeOnly robot: native synchronized trades+quotes and empirical calibration are unqualified.
    /// </summary>
    public sealed class ApmOrderBookResearch
    {
        private readonly Queue<(decimal Ofi, decimal Depth)> _window = new Queue<(decimal, decimal)>();
        private readonly int _windowEvents;
        private readonly TimeSpan _maximumGap;
        private readonly ApmDataProfile _profile;
        private ApmBookQuote _previous;
        private decimal _ofi;
        private decimal _depth;

        /// <summary>Declare actual source profile and finite event window; TradeOnly cannot create book features.</summary>
        public ApmOrderBookResearch(ApmDataProfile profile, int windowEvents, TimeSpan maximumGap)
        {
            if (!Enum.IsDefined(profile) || profile == ApmDataProfile.TradeOnly || windowEvents < 1 || windowEvents > 100000 || maximumGap <= TimeSpan.Zero)
                throw new ArgumentException("Book capabilities and finite window are required.");
            _profile = profile; _windowEvents = windowEvents; _maximumGap = maximumGap;
        }

        /// <summary>Clear continuity after disconnect/gap. The next valid quote is only a baseline, never a fabricated impulse.</summary>
        public void Reset() { _previous = null; _window.Clear(); _ofi = 0; _depth = 0; }

        /// <summary>Process one observed quote; invalid/order-regressing events invalidate continuity without sorting or reconstruction.</summary>
        public ApmBookFeature Observe(ApmBookQuote quote)
        {
            if (quote == null) throw new ArgumentNullException(nameof(quote));
            string quality = quote.Bid <= 0 || quote.Ask <= quote.Bid || quote.BidSize <= 0 || quote.AskSize <= 0
                ? "EMPTY_OR_CROSSED" : _previous != null && (quote.Sequence <= _previous.Sequence || quote.Time < _previous.Time)
                ? "OUT_OF_ORDER" : _previous != null && quote.Time - _previous.Time > _maximumGap ? "STALE_GAP" : "OK";
            if (quality != "OK")
            {
                Reset();
                if (quality == "STALE_GAP") _previous = quote;
                return Empty(quote, quality);
            }
            if (_previous == null) { _previous = quote; return Empty(quote, "WARMUP_BASELINE"); }
            try
            {
                decimal impulse = EventOfi(_previous, quote);
                decimal depth = quote.BidSize / 2 + quote.AskSize / 2;
                _window.Enqueue((impulse, depth)); _ofi += impulse; _depth += depth;
                if (_window.Count > _windowEvents) { (decimal Ofi, decimal Depth) old = _window.Dequeue(); _ofi -= old.Ofi; _depth -= old.Depth; }
                _previous = quote;
                decimal meanDepth = _depth / _window.Count;
                return new ApmBookFeature(quote.Time, quote.Sequence, impulse, _ofi, meanDepth, _ofi / meanDepth,
                    quote.Bid / 2 + quote.Ask / 2, true, _profile == ApmDataProfile.SampledBook ? "SAMPLED_PROXY" : "OBSERVED_TOP_ONLY", _profile);
            }
            catch (ArithmeticException) { Reset(); return Empty(quote, "NUMERIC_RANGE"); }
        }

        private ApmBookFeature Empty(ApmBookQuote quote, string quality) =>
            new ApmBookFeature(quote.Time, quote.Sequence, 0, 0, 0, 0, 0, false, quality, _profile);

        /// <summary>SRC-OFI four indicator terms, including both terms at an unchanged price. Result has book-volume units.</summary>
        public static decimal EventOfi(ApmBookQuote previous, ApmBookQuote current) =>
            (current.Bid >= previous.Bid ? current.BidSize : 0) - (current.Bid <= previous.Bid ? previous.BidSize : 0)
            - (current.Ask <= previous.Ask ? current.AskSize : 0) + (current.Ask >= previous.Ask ? previous.AskSize : 0);
    }

    /// <summary>Contemporaneous impact and future prediction are separate estimands, never interchangeable strategy alpha.</summary>
    public enum ApmCalibrationKind { ContemporaneousImpact, LaggedPrediction }

    /// <summary>One externally matured observation; response must be known by ObservedAt and features carry their own interval.</summary>
    public sealed record ApmMaturedObservation(DateTime FeatureStart, DateTime FeatureEnd, DateTime ResponseStart,
        DateTime ResponseEnd, DateTime ObservedAt, decimal Feature, decimal Response);

    /// <summary>Versioned zero-intercept least-squares estimate; units explicitly describe response per feature.</summary>
    public sealed record ApmCalibrationSnapshot(string Version, ApmCalibrationKind Kind, string Units, long Samples,
        DateTime TrainingCutoff, decimal? Coefficient);

    /// <summary>
    /// Constant-memory research calibration of a declared estimand. Accepts only matured chronological
    /// observations up to a frozen cutoff; no silent online update in validation/test and no cross-moment factorization.
    /// </summary>
    public sealed class ApmCausalCalibration
    {
        private readonly ApmCalibrationKind _kind;
        private readonly string _units;
        private readonly DateTime _cutoff;
        private DateTime _lastResponse;
        private decimal _xx;
        private decimal _xy;
        private long _samples;

        /// <summary>Choose source units, estimand and past-data cutoff before consuming labels.</summary>
        public ApmCausalCalibration(ApmCalibrationKind kind, string units, DateTime trainingCutoff)
        {
            if (string.IsNullOrWhiteSpace(units) || units.Length > 100 || !Enum.IsDefined(kind)) throw new ArgumentException("Explicit calibration units/kind required.");
            _kind = kind; _units = units; _cutoff = trainingCutoff;
        }

        /// <summary>Reject future, overlapping/out-of-order responses, incorrect interval pairing and labels after the frozen fit cutoff.</summary>
        public void Add(ApmMaturedObservation observation, DateTime asOf)
        {
            if (observation == null || observation.FeatureStart >= observation.FeatureEnd
                || observation.ResponseStart >= observation.ResponseEnd || observation.ResponseStart < _lastResponse
                || observation.ResponseEnd > observation.ObservedAt || observation.ObservedAt > asOf
                || observation.ResponseEnd > _cutoff
                || (_kind == ApmCalibrationKind.ContemporaneousImpact
                    ? observation.FeatureStart != observation.ResponseStart || observation.FeatureEnd != observation.ResponseEnd
                    : observation.FeatureEnd >= observation.ResponseStart)) throw new ArgumentException("Noncausal or mismatched calibration observation.");
            decimal xx = _xx + observation.Feature * observation.Feature;
            decimal xy = _xy + observation.Feature * observation.Response;
            long samples = checked(_samples + 1);
            _xx = xx; _xy = xy; _samples = samples; _lastResponse = observation.ResponseEnd;
        }

        /// <summary>Null coefficient means unidentified zero feature energy. It never silently means a fitted zero effect.</summary>
        public ApmCalibrationSnapshot Snapshot => new ApmCalibrationSnapshot("APM-BookFit-v1", _kind, _units,
            _samples, _lastResponse, _xx == 0 ? null : _xy / _xx);
    }
}
