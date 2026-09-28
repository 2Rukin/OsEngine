/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace OsEngine.Indicators
{
    public abstract partial class Aindicator
    {
        private IndicatorParameterString _timeProfilesParameter;
        private Action<int> _resetTimeProfileState;
        private ThresholdTimeCursor _timeProfileCursor;
        private ThresholdSelection _timeProfileSelection = new ThresholdSelection(true, false, null);
        private string _timeProfileEncoding;
        private string _decodedProfileEncoding;
        private ThresholdTimeProfiles _decodedProfiles;
        private bool _timeProfileRebuild;

        /// <summary>Explicitly registered metric thresholds; an empty collection leaves the ordinary indicator UI unchanged.</summary>
        public ImmutableArray<ThresholdDefinition> TimeThresholds { get; private set; } = ImmutableArray<ThresholdDefinition>.Empty;

        /// <summary>Configures reusable time profiles after the indicator has created its ordinary parameters.</summary>
        /// <remarks>
        /// Call once from Configure. OnProcess must read these thresholds through ResolveTimeThreshold.
        /// resetState must reset private accumulation without deleting series/history or subscriptions; its argument
        /// is the first source candle index of the new segment. It is also invoked on a complete rebuild.
        /// This opt-in contract works in Trader, Tester and Optimizer; it cannot manufacture unavailable OI/trades.
        /// The adapter uses Candle.TimeStart, not a tick's arrival time. Contract: INDICATOR-TIME-PROFILES-001.
        /// </remarks>
        protected void ConfigureThresholdTimeProfiles(IReadOnlyList<ThresholdDefinition> definitions, Action<int> resetState)
        {
            if (_timeProfilesParameter != null || definitions == null || definitions.Count == 0 || resetState == null)
            { throw new ArgumentException("Declare thresholds once with an explicit state-reset callback."); }
            TimeThresholds = definitions.ToImmutableArray();
            _resetTimeProfileState = resetState;
            _timeProfilesParameter = CreateParameterString("Threshold time profiles v1", "");
            TimeProfiles.Validate(TimeThresholds);
        }

        /// <summary>Gets the validated immutable schedule persisted as an ordinary scalar parameter and included in Optimizer cache identity.</summary>
        public ThresholdTimeProfiles TimeProfiles
        {
            get
            {
                string text = _timeProfilesParameter?.ValueString;
                if (_decodedProfiles == null || text != _decodedProfileEncoding)
                {
                    ThresholdTimeProfiles profiles = ThresholdTimeProfiles.Decode(text);
                    profiles.Validate(TimeThresholds); _decodedProfiles = profiles; _decodedProfileEncoding = text;
                }
                return _decodedProfiles;
            }
        }

        /// <summary>Updates an explicitly participating indicator. Caller must serialize with processing, then Reload and Save as for other parameters.</summary>
        public void SetTimeProfiles(ThresholdTimeProfiles profiles)
        {
            if (_timeProfilesParameter == null) { throw new InvalidOperationException("This indicator has no registered metric thresholds."); }
            profiles.Validate(TimeThresholds); _timeProfilesParameter.ValueString = profiles.Encode();
            _timeProfileCursor = null; _cache = null; _tryGetCacheOnce = false;
        }

        /// <summary>Resolves a registered threshold inside OnProcess without mutating its base parameter or bound children.</summary>
        protected decimal ResolveTimeThreshold(string key, decimal baseValue)
        {
            bool known = false;
            foreach (ThresholdDefinition definition in TimeThresholds)
            { if (definition.Key == key) { definition.Validate(baseValue); known = true; break; } }
            if (!known) { throw new ArgumentException("Unregistered metric threshold: " + key); }
            return _timeProfileSelection.Value(key, baseValue);
        }

        internal bool IsTimeProfilesParameter(IndicatorParameter parameter) => ReferenceEquals(parameter, _timeProfilesParameter);

        private void ResetTimeProfileProcessing()
        {
            _timeProfileCursor = null;
            _timeProfileSelection = new ThresholdSelection(true, false, null);
            _timeProfileRebuild = _timeProfilesParameter != null;
        }

        private bool PrepareTimeProfile(DateTime time, int index)
        {
            if (_timeProfilesParameter == null) { return true; }
            string encoding = _timeProfilesParameter.ValueString;
            if (_timeProfileCursor == null || encoding != _timeProfileEncoding)
            { _timeProfileCursor = new ThresholdTimeCursor(TimeProfiles); _timeProfileEncoding = encoding; }
            _timeProfileSelection = _timeProfileCursor.Select(time);
            if (_timeProfileSelection.Reset || _timeProfileRebuild) { _resetTimeProfileState(index); }
            _timeProfileRebuild = false;
            if (!_timeProfileSelection.Active)
            { foreach (IndicatorDataSeries series in DataSeries) { series.Values[index] = 0; } }
            return _timeProfileSelection.Active;
        }
    }
}
