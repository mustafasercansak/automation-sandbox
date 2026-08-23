using System;
using System.Collections.Generic;

namespace IntentAutomation
{
    /// <summary>
    /// Encapsulates multi-tier locator recording lookup across test generators.
    /// Resolves recordings by:
    /// 1. Direct step object reference (exact match)
    /// 2. Step execution order
    /// 3. Step target description / locator key
    /// 4. Dynamically synthesized locator key
    /// </summary>
    public sealed class IntentRecordingLookupTable<TRecording> where TRecording : class
    {
        private readonly Dictionary<IntentStep, TRecording> _byStep = new Dictionary<IntentStep, TRecording>();
        private readonly Dictionary<int, TRecording> _byOrder = new Dictionary<int, TRecording>();
        private readonly Dictionary<string, TRecording> _byKey = new Dictionary<string, TRecording>(StringComparer.OrdinalIgnoreCase);

        public IntentRecordingLookupTable(
            IEnumerable<TRecording>? recordings,
            Func<TRecording, IntentStep?> getStep,
            Func<TRecording, string?> getKey)
        {
            if (recordings == null || getStep == null || getKey == null)
            {
                return;
            }

            foreach (var recording in recordings)
            {
                if (recording == null)
                {
                    continue;
                }

                var step = getStep(recording);
                if (step != null && !_byStep.ContainsKey(step))
                {
                    _byStep[step] = recording;
                    if (step.Order > 0 && !_byOrder.ContainsKey(step.Order))
                    {
                        _byOrder[step.Order] = recording;
                    }
                }

                var key = getKey(recording);
                if (!string.IsNullOrWhiteSpace(key) && !_byKey.ContainsKey(key!))
                {
                    _byKey[key!] = recording;
                }
            }
        }

        public bool TryFindRecording(IntentStep step, out TRecording? recording)
        {
            recording = null;
            if (step == null)
            {
                return false;
            }

            if (_byStep.TryGetValue(step, out recording))
            {
                return true;
            }

            if (step.Order > 0 && _byOrder.TryGetValue(step.Order, out recording))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(step.TargetDescription) && _byKey.TryGetValue(step.TargetDescription, out recording))
            {
                return true;
            }

            var synthesizedKey = IntentLocatorKeySynthesizer.Synthesize(step);
            if (!string.IsNullOrWhiteSpace(synthesizedKey) && _byKey.TryGetValue(synthesizedKey, out recording))
            {
                return true;
            }

            return false;
        }
    }

    public static class IntentRecordingLookupTable
    {
        public static IntentRecordingLookupTable<IntentLocatorRecordingResult> Create(
            IEnumerable<IntentLocatorRecordingResult>? recordings)
        {
            return new IntentRecordingLookupTable<IntentLocatorRecordingResult>(
                recordings,
                r => r?.Step,
                r => r?.LocatorKey);
        }

        public static IntentRecordingLookupTable<IntentDesktopLocatorRecordingResult> CreateDesktop(
            IEnumerable<IntentDesktopLocatorRecordingResult>? recordings)
        {
            return new IntentRecordingLookupTable<IntentDesktopLocatorRecordingResult>(
                recordings,
                r => r?.Step,
                r => r?.LocatorKey);
        }
    }
}
