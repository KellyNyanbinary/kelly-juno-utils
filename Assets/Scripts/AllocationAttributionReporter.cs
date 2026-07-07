namespace Assets.Scripts
{
    using System;
    using System.Linq;
    using UnityEngine;

    /// <summary>
    /// A persistent, scene-independent MonoBehaviour that periodically logs the top allocating
    /// update phases recorded by <see cref="AllocationAttribution"/>, when
    /// <see cref="ModSettings.EnableAllocationAttribution"/> is enabled. Diagnostic only.
    /// </summary>
    public sealed class AllocationAttributionReporter : MonoBehaviour
    {
        private const float ReportIntervalSeconds = 1f;
        private const int TopN = 6;

        private float _accumulatedTime;

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
                // Drain without reporting so a subsequent enable doesn't report a stale, misleading
                // backlog accumulated while this was off.
                AllocationAttribution.DrainAndReset();
                _accumulatedTime = 0f;
                return;
            }

            _accumulatedTime += Time.unscaledDeltaTime;
            if (_accumulatedTime < ReportIntervalSeconds)
                return;

            var snapshot = AllocationAttribution.DrainAndReset();
            var window = _accumulatedTime;
            _accumulatedTime = 0f;

            if (snapshot.Count == 0)
                return;

            var top = snapshot
                .OrderByDescending(kv => kv.Value)
                .Take(TopN)
                .Select(kv => $"{kv.Key}={kv.Value / (1024f * 1024f):F2}MB/{(kv.Value / window / (1024f * 1024f)):F2}MB/s");

            Debug.Log($"[KellyUtils] AllocTop (over {window:F1}s): {string.Join(", ", top)}");
        }
    }
}
