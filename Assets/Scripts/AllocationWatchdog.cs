namespace Assets.Scripts
{
    using System;
    using UnityEngine;

    /// <summary>
    /// Logs managed-heap growth and craft state once per second while enabled.
    /// </summary>
    public sealed class AllocationWatchdog : MonoBehaviour
    {
        private const float SampleIntervalSeconds = 1f;
        private const float BytesPerMiB = 1024f * 1024f;

        private float _accumulatedTime;
        private long _memoryAtWindowStart;
        private int _gen0AtWindowStart;
        private bool _wasEnabled;

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
                _wasEnabled = false;
                return;
            }

            if (!_wasEnabled)
            {
                _wasEnabled = true;
                ResetWindow();
                return;
            }

            _accumulatedTime += Time.unscaledDeltaTime;
            if (_accumulatedTime < SampleIntervalSeconds)
                return;

            var currentMemory = GC.GetTotalMemory(false);
            var currentGen0 = GC.CollectionCount(0);
            var memoryDeltaMiB = (currentMemory - _memoryAtWindowStart) / BytesPerMiB;
            var allocationRateMiB = memoryDeltaMiB / _accumulatedTime;
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
                $"[KellyUtils] Alloc: {allocationRateMiB:F2} MiB/s over {_accumulatedTime:F1}s — "
                + $"gen0Delta={gen0Delta}, heap={currentMemory / BytesPerMiB:F1} MiB, "
                + $"scene={scene}, parts={partCount}, throttle={throttle:F2}, "
                + $"playerControl={playerControl}, altAgl={altitudeAgl:F0}");

            ResetWindow(currentMemory, currentGen0);
        }

        private void ResetWindow()
        {
            ResetWindow(GC.GetTotalMemory(false), GC.CollectionCount(0));
        }

        private void ResetWindow(long memory, int gen0Count)
        {
            _accumulatedTime = 0f;
            _memoryAtWindowStart = memory;
            _gen0AtWindowStart = gen0Count;
        }
    }
}
