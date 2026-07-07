namespace Assets.Scripts
{
    using System;
    using ModApi;
    using UnityEngine;
    using UnityEngine.Scripting;

    /// <summary>
    /// A persistent, scene-independent MonoBehaviour that takes manual control of the garbage
    /// collector (via <see cref="GarbageCollector.GCMode"/>) when
    /// <see cref="ModSettings.EnableManualGCScheduling"/> is enabled.
    /// </summary>
    /// <remarks>
    /// This player build does not have Unity's incremental GC compiled in
    /// (<c>GarbageCollector.isIncremental</c> is false), so every collection is a full,
    /// blocking, stop-the-world pause — there is no way for a mod to make individual
    /// collections shorter. What a mod *can* do is control *when* they happen: instead of
    /// letting the runtime trigger a collection at an arbitrary moment (e.g. mid-throttle,
    /// mid-maneuver), this scheduler only forces collections while the game is paused or the
    /// flight scene isn't active (menus, designer, tech tree, planet studio). A heap-size
    /// safety valve still forces a collection even during active flight if memory grows too
    /// large, to avoid unbounded growth while waiting for a safe moment.
    /// </remarks>
    public sealed class GCScheduler : MonoBehaviour
    {
        /// <summary>
        /// Minimum growth in the managed heap (bytes) since the last collection before an
        /// opportunistic "safe window" collection is worth doing. Avoids pointlessly collecting
        /// every time the game happens to be paused when little garbage has accumulated.
        /// </summary>
        private const long OpportunisticAllocationThresholdBytes = 16L * 1024 * 1024;

        private bool _managingGCMode;
        private float _lastCollectRealtime;
        private long _memoryAtLastCollect;

        /// <summary>
        /// Creates the persistent GameObject hosting this scheduler, if it doesn't already exist.
        /// Safe to call multiple times.
        /// </summary>
        public static void EnsureCreated()
        {
            if (FindObjectOfType<GCScheduler>() != null)
                return;

            var go = new GameObject("KellyUtils.GCScheduler");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<GCScheduler>();
        }

        private void Update()
        {
            bool enabled;
            float minInterval;
            long heapSafetyCapBytes;

            try
            {
                var settings = ModSettings.Instance;
                enabled = settings.EnableManualGCScheduling.Value;
                minInterval = settings.GCMinIntervalSeconds.Value;
                heapSafetyCapBytes = (long)(settings.GCHeapSafetyCapMB.Value * 1024f * 1024f);
            }
            catch (Exception)
            {
                // Settings aren't ready yet (e.g. very early in startup); do nothing this frame.
                return;
            }

            if (!enabled)
            {
                RelinquishGCControl();
                return;
            }

            TakeGCControl();

            if (_managingGCMode && GarbageCollector.GCMode != GarbageCollector.Mode.Manual)
            {
                Debug.LogWarning(
                    $"[KellyUtils] GCMode was reset to {GarbageCollector.GCMode} by something other than "
                    + "GCScheduler — re-asserting Manual. A collection may have just occurred outside our control.");
                GarbageCollector.GCMode = GarbageCollector.Mode.Manual;
            }

            var currentMemory = GC.GetTotalMemory(false);

            if (currentMemory >= heapSafetyCapBytes)
            {
                Debug.LogWarning(
                    $"[KellyUtils] GC heap safety cap reached ({currentMemory / (1024 * 1024)} MB >= "
                    + $"{heapSafetyCapBytes / (1024 * 1024)} MB) — forcing a collection mid-flight.");
                Collect();
                return;
            }

            if (!IsSafeWindow())
                return;

            if (Time.realtimeSinceStartup - _lastCollectRealtime < minInterval)
                return;

            if (currentMemory - _memoryAtLastCollect < OpportunisticAllocationThresholdBytes)
                return;

            Collect();
        }

        private void OnDestroy()
        {
            RelinquishGCControl();
        }

        private void OnApplicationQuit()
        {
            RelinquishGCControl();
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
            _lastCollectRealtime = Time.realtimeSinceStartup;
            _memoryAtLastCollect = GC.GetTotalMemory(false);
        }

        private void TakeGCControl()
        {
            if (_managingGCMode)
                return;

            GarbageCollector.GCMode = GarbageCollector.Mode.Manual;
            _managingGCMode = true;
            Debug.Log($"[KellyUtils] GCScheduler took control — GCMode now {GarbageCollector.GCMode}.");
        }

        private void RelinquishGCControl()
        {
            if (!_managingGCMode)
                return;

            GarbageCollector.GCMode = GarbageCollector.Mode.Enabled;
            _managingGCMode = false;
            Debug.Log($"[KellyUtils] GCScheduler relinquished control — GCMode now {GarbageCollector.GCMode}.");
        }
    }
}
