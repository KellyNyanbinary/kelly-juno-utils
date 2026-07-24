namespace Assets.Scripts
{
    using System;
    using System.Linq;
    using UnityEngine;

    /// <summary>
    /// Reports the flight-update classes with the most managed-heap growth.
    /// </summary>
    public sealed class AllocationAttributionReporter : MonoBehaviour
    {
        private const float ReportIntervalSeconds = 1f;
        private const int TopN = 6;
        private const float BytesPerMiB = 1024f * 1024f;

        private float _accumulatedTime;
        private bool _wasEnabled;

        /// <summary>
        /// Creates the persistent GameObject hosting this reporter, if it doesn't already exist.
        /// Safe to call multiple times.
        /// </summary>
        public static void EnsureCreated()
        {
            if (FindObjectOfType<AllocationAttributionReporter>() != null)
                return;

            var go = new GameObject("KellyUtils.AllocationAttributionReporter");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<AllocationAttributionReporter>();
        }

        private void Update()
        {
            bool enabled;
            try
            {
                enabled = ModSettings.Instance.EnableAllocationAttribution.Value;
            }
            catch (Exception)
            {
                return;
            }

            if (!enabled)
            {
                AllocationAttribution.Enabled = false;

                if (_wasEnabled)
                    AllocationAttribution.TakeSnapshot();

                _wasEnabled = false;
                _accumulatedTime = 0f;
                return;
            }

            if (!_wasEnabled)
            {
                AllocationAttribution.TakeSnapshot();
                AllocationAttribution.Enabled = true;
                _wasEnabled = true;
                _accumulatedTime = 0f;
                Debug.Log(
                    "[KellyUtils] AllocationAttribution enabled — "
                    + "measuring per-class managed-heap deltas.");
                return;
            }

            _accumulatedTime += Time.unscaledDeltaTime;
            if (_accumulatedTime < ReportIntervalSeconds)
                return;

            var snapshot = AllocationAttribution.TakeSnapshot();
            var window = _accumulatedTime;
            _accumulatedTime = 0f;

            if (snapshot.Count == 0)
                return;

            var top = snapshot
                .OrderByDescending(kv => kv.Value)
                .Take(TopN)
                .Select(kv =>
                    $"{kv.Key}={kv.Value / BytesPerMiB:F2} MiB/"
                    + $"{kv.Value / window / BytesPerMiB:F2} MiB/s");

            Debug.Log($"[KellyUtils] AllocTop (over {window:F1}s): {string.Join(", ", top)}");
        }
    }
}
