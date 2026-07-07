namespace Assets.Scripts
{
    using System;
    using UnityEngine;

    /// <summary>
    /// A persistent, scene-independent MonoBehaviour that logs the managed heap allocation rate
    /// once per second, along with craft state, when <see cref="ModSettings.EnableAllocationWatchdog"/>
    /// is enabled. Purely diagnostic — used to correlate GC pressure with what the craft is doing
    /// (part count, throttle, player control, altitude), since <c>GCMode</c> cannot control when
    /// automatic collections happen on this build (see <see cref="GCScheduler"/>), so the only
    /// remaining lever is reducing what's being allocated in the first place.
    /// </summary>
    public sealed class AllocationWatchdog : MonoBehaviour
    {
        private const float SampleIntervalSeconds = 1f;

        private float _accumulatedTime;
        private long _memoryAtWindowStart;
        private int _gen0AtWindowStart;

        /// <summary>
        /// Creates the persistent GameObject hosting this watchdog, if it doesn't already exist.
        /// Safe to call multiple times.
        /// </summary>
        public static void EnsureCreated()
        {
            if (FindObjectOfType<AllocationWatchdog>() != null)
                return;

            var go = new GameObject("KellyUtils.AllocationWatchdog");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<AllocationWatchdog>();
        }

        private void Start()
        {
            _memoryAtWindowStart = GC.GetTotalMemory(false);
            _gen0AtWindowStart = GC.CollectionCount(0);
        }

        private void Update()
        {
            bool enabled;
            try
            {
                enabled = ModSettings.Instance.EnableAllocationWatchdog.Value;
            }
            catch (Exception)
            {
                return;
            }

            if (!enabled)
            {
                // Keep the window anchored to "now" so that toggling this on mid-flight doesn't
                // report a stale, misleadingly large delta from whenever it was last enabled.
                _accumulatedTime = 0f;
                _memoryAtWindowStart = GC.GetTotalMemory(false);
                _gen0AtWindowStart = GC.CollectionCount(0);
                return;
            }

            _accumulatedTime += Time.unscaledDeltaTime;
            if (_accumulatedTime < SampleIntervalSeconds)
                return;

            var currentMemory = GC.GetTotalMemory(false);
            var currentGen0 = GC.CollectionCount(0);
            var memoryDeltaMb = (currentMemory - _memoryAtWindowStart) / (1024f * 1024f);
            var allocRateMbPerSec = memoryDeltaMb / _accumulatedTime;
            var gen0Delta = currentGen0 - _gen0AtWindowStart;

            var game = Game.Instance;
            var sceneManager = game?.SceneManager;
            var scene = sceneManager?.CurrentScene ?? "unknown";

            var partCount = -1;
            var throttle = -1f;
            var playerControl = false;
            var altitudeAgl = -1.0;

            if (sceneManager != null && sceneManager.InFlightScene)
            {
                var craftNode = game.FlightScene?.CraftNode;
                if (craftNode != null)
                {
                    partCount = craftNode.CraftPartCount;
                    throttle = craftNode.Controls?.Throttle ?? -1f;
                    playerControl = craftNode.AllowPlayerControl;
                    altitudeAgl = craftNode.AltitudeAgl;
                }
            }

            Debug.Log(
                $"[KellyUtils] Alloc: {allocRateMbPerSec:F2} MB/s over {_accumulatedTime:F1}s — "
                + $"gen0Delta={gen0Delta}, heap={currentMemory / (1024f * 1024f):F1} MB, "
                + $"scene={scene}, parts={partCount}, throttle={throttle:F2}, "
                + $"playerControl={playerControl}, altAgl={altitudeAgl:F0}");

            _accumulatedTime = 0f;
            _memoryAtWindowStart = currentMemory;
            _gen0AtWindowStart = currentGen0;
        }
    }
}
