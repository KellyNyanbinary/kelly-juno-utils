namespace Assets.Scripts
{
    using System;
    using UnityEngine;

    /// <summary>
    /// A persistent, scene-independent MonoBehaviour that reduces GC-related stutter during
    /// flight by opportunistically forcing garbage collections at safe moments, when
    /// <see cref="ModSettings.EnableManualGCScheduling"/> is enabled.
    /// </summary>
    /// <remarks>
    /// This player build does not have Unity's incremental GC compiled in
    /// (<c>GarbageCollector.isIncremental</c> is false), so every collection is a full,
    /// blocking, stop-the-world pause, and <c>GarbageCollector.GCMode</c> has no effect on the
    /// automatic collector (verified empirically: collections still occurred with GCMode set to
    /// Manual). A mod, therefore, cannot shorten collections or suppress them, but it can
    /// reduce how often they land mid-flight by collecting whenever the game is paused or the
    /// flight scene isn't active (menus, designer, etc.), keeping the heap small so the
    /// runtime's own collections trigger less frequently during active flight. A heap-size
    /// safety valve also forces a collection mid-flight if memory grows past a configured cap.
    /// </remarks>
    public sealed class GCScheduler : MonoBehaviour
    {
        /// <summary>
        /// Minimum growth in the managed heap (bytes) since the last collection before an
        /// opportunistic "safe window" collection is worth doing. Avoids pointlessly collecting
        /// every time the game happens to be paused when little garbage has accumulated.
        /// </summary>
        private const long OpportunisticAllocationThresholdBytes = 16L * 1024 * 1024;

        private double _lastCollectRealtime;
        private long _memoryAtLastCollect;

        /// <summary>
        /// Creates the persistent GameObject hosting this scheduler if it doesn't already exist.
        /// Safe to call multiple times.
        /// </summary>
        public static void EnsureCreated()
        {
            if (FindObjectOfType<GCScheduler>() != null)
                return;

            var go = new GameObject("KellyUtils.GCScheduler");
            DontDestroyOnLoad(go);
            go.AddComponent<GCScheduler>();
        }

        private void Update()
        {
            bool isEnabled;
            float minInterval;
            long heapSafetyCapBytes;

            try
            {
                var settings = ModSettings.Instance;
                isEnabled = settings.EnableManualGCScheduling.Value;
                minInterval = settings.GCMinIntervalSeconds.Value;
                heapSafetyCapBytes = (long)(settings.GCHeapSafetyCapMiB.Value * 1024f * 1024f);
            }
            catch (Exception)
            {
                // Settings aren't ready yet (e.g. very early in startup); do nothing this frame.
                return;
            }

            if (!isEnabled)
                return;
            
            if (heapSafetyCapBytes <= 0)
                return;

            var currentMemory = GC.GetTotalMemory(false);

            if (currentMemory >= heapSafetyCapBytes)
            {
                Debug.LogWarning(
                    $"[KellyUtils] GC heap safety cap reached ({currentMemory / (1024 * 1024)} MiB >= "
                    + $"{heapSafetyCapBytes / (1024 * 1024)} MiB), forcing a collection mid-flight.");
                Collect();
                return;
            }

            if (!IsSafeWindow())
                return;

            if (Time.realtimeSinceStartupAsDouble - _lastCollectRealtime < minInterval)
                return;

            if (currentMemory - _memoryAtLastCollect < OpportunisticAllocationThresholdBytes)
                return;

            Collect();
        }

        /// <summary>
        /// Determines whether now is a safe moment to force a blocking collection: the game is
        /// paused, or the flight scene (where stutter actually matters) isn't active at all.
        /// </summary>
        private static bool IsSafeWindow()
        {
            var game = Game.Instance;
            var sceneManager = game?.SceneManager;
            if (sceneManager == null)
                return false;

            if (!sceneManager.InFlightScene)
                return true;

            return game.FlightScene?.TimeManager?.Paused ?? false;
        }

        private void Collect()
        {
            GC.Collect();
            _lastCollectRealtime = Time.realtimeSinceStartupAsDouble;
            _memoryAtLastCollect = GC.GetTotalMemory(false);
        }
    }
}
