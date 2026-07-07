namespace Assets.Scripts
{
    using System;
    using UnityEngine;
    using UnityEngine.Scripting;

    /// <summary>
    /// A persistent, scene-independent MonoBehaviour that watches frame time and logs details
    /// whenever a frame takes unusually long, so we can tell whether a given stutter is actually
    /// caused by a garbage collection (ours, or an explicit <c>GC.Collect()</c> call baked into the
    /// game's own code, e.g. terrain pool resizing) or by something else entirely (physics, asset
    /// streaming, shader compilation, etc.).
    /// </summary>
    /// <remarks>
    /// This does not change any behavior — it only logs. <see cref="GC.CollectionCount(int)"/> is
    /// used as a cheap, allocation-free way to detect whether a collection happened during the
    /// slow frame: if the count went up, something (not necessarily KellyUtils) called
    /// <c>GC.Collect()</c> or the runtime triggered one automatically; if it stayed flat, the
    /// stutter has a non-GC cause.
    /// </remarks>
    public sealed class StutterWatchdog : MonoBehaviour
    {
        private const float ThresholdMs = 50f;

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
            if (frameMs < ThresholdMs)
                return;

            var gen0Count = GC.CollectionCount(0);
            var totalMemory = GC.GetTotalMemory(false);
            var gen0Delta = gen0Count - _lastGen0Count;
            var memoryDeltaMb = (totalMemory - _lastTotalMemory) / (1024f * 1024f);

            var game = Game.Instance;
            var sceneName = game?.SceneManager?.CurrentScene ?? "unknown";

            Debug.LogWarning(
                $"[KellyUtils] Stutter: {frameMs:F1} ms frame — "
                + $"gen0CollectionDelta={gen0Delta}, "
                + $"heap={totalMemory / (1024f * 1024f):F1} MB (delta {memoryDeltaMb:F1} MB), "
                + $"scene={sceneName}, "
                + $"GCMode={GarbageCollector.GCMode}, "
                + $"frame={Time.frameCount}");

            _lastGen0Count = gen0Count;
            _lastTotalMemory = totalMemory;
        }
    }
}
