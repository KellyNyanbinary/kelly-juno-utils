namespace Assets.Scripts
{
    using System;
    using UnityEngine;
    using UnityEngine.Scripting;

    /// <summary>
    /// Logs the player build's garbage-collector capabilities without changing GC behavior.
    /// </summary>
    internal static class GCDiagnostics
    {
        public static void LogCapabilities()
        {
            try
            {
                Debug.Log(
                    "[KellyUtils] GC diagnostics — "
                    + $"isIncremental={GarbageCollector.isIncremental}, "
                    + $"GCMode={GarbageCollector.GCMode}, "
                    + $"incrementalTimeSliceNanoseconds={GarbageCollector.incrementalTimeSliceNanoseconds}, "
                    + $"scriptingBackend={Application.platform}, "
                    + $"unityVersion={Application.unityVersion}");
            }
            catch (Exception ex)
            {
                Debug.LogError("[KellyUtils] GC diagnostics failed: " + ex);
            }
        }
    }
}
