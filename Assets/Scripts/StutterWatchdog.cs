namespace Assets.Scripts
{
    using System;
    using UnityEngine;
    using UnityEngine.Scripting;

    /// <summary>
    /// Logs slow frames and whether a generation-zero collection occurred since the preceding frame.
    /// </summary>
    /// <remarks>
    /// A collection delta identifies correlation, not which code initiated the collection.
    /// </remarks>
    public sealed class StutterWatchdog : MonoBehaviour
    {
        private const float ThresholdMs = 50f;
        private const float BytesPerMiB = 1024f * 1024f;

        private int _lastGen0Count;
        private long _lastTotalMemory;

        /// <summary>
        /// Creates the persistent GameObject hosting this watchdog, if it doesn't already exist.
        /// Safe to call multiple times.
        /// </summary>
        public static void EnsureCreated()
        {
            if (FindObjectOfType<StutterWatchdog>() != null)
                return;

            var go = new GameObject("KellyUtils.StutterWatchdog");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<StutterWatchdog>();
        }

        private void Start()
        {
            _lastGen0Count = GC.CollectionCount(0);
            _lastTotalMemory = GC.GetTotalMemory(false);
        }

        private void Update()
        {
            var frameMs = Time.unscaledDeltaTime * 1000f;
            var gen0Count = GC.CollectionCount(0);
            var totalMemory = GC.GetTotalMemory(false);
            var gen0Delta = gen0Count - _lastGen0Count;
            var memoryDeltaMiB = (totalMemory - _lastTotalMemory) / BytesPerMiB;

            _lastGen0Count = gen0Count;
            _lastTotalMemory = totalMemory;

            if (frameMs < ThresholdMs)
                return;

            var scene = Game.Instance?.SceneManager?.CurrentScene ?? "unknown";

            Debug.LogWarning(
                $"[KellyUtils] Stutter: {frameMs:F1} ms frame — "
                + $"gen0CollectionDelta={gen0Delta}, "
                + $"heap={totalMemory / BytesPerMiB:F1} MiB (delta {memoryDeltaMiB:F1} MiB), "
                + $"scene={scene}, GCMode={GarbageCollector.GCMode}, frame={Time.frameCount}");
        }
    }
}
