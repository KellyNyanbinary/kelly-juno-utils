namespace Assets.Scripts
{
    using System;
    using UnityEngine;
    using UnityEngine.Scripting;

    /// <summary>
    /// One-shot diagnostic that logs the Mono/IL2CPP garbage collector's capabilities at mod load
    /// time. This is a read-only probe used to determine whether the shipped player build has
    /// incremental (time-sliced) GC compiled in, which is a prerequisite for any runtime GC-tuning
    /// feature. It makes no changes to GC behavior.
    /// </summary>
    internal static class GCDiagnostics
    {
        public static void LogCapabilities()
        {
            try
            {
                var isIncremental = GarbageCollector.isIncremental;
                var mode = GarbageCollector.GCMode;
                var incrementalTimeSliceNs = GarbageCollector.incrementalTimeSliceNanoseconds;

                Debug.Log(
                    "[KellyUtils] GC diagnostics — "
                    + $"isIncremental={isIncremental}, "
                    + $"GCMode={mode}, "
                    + $"incrementalTimeSliceNanoseconds={incrementalTimeSliceNs}, "
                    + $"scriptingBackend={Application.platform}, "
                    + $"unityVersion={Application.unityVersion}");
            }
            catch (Exception ex)
            {
                // If the GarbageCollector API is missing/unsupported in this player build,
                // fail loudly in the log rather than silently doing nothing, so it's obvious
                // that the incremental-GC path is unavailable.
                Debug.LogError("[KellyUtils] GC diagnostics failed: " + ex);
            }
        }
    }
}
