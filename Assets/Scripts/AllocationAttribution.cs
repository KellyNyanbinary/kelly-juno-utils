namespace Assets.Scripts
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using HarmonyLib;
    using UnityEngine;

    /// <summary>
    /// Harmony-based allocation attribution for the flight scene's per-frame update dispatch.
    /// </summary>
    /// <remarks>
    /// Originally this patched the game's generic <c>UpdateGroup&lt;T&gt;.Update(Action&lt;T&gt;, string)</c>
    /// method per-phase, but reading <c>FlightGameLoop</c> revealed that during normal (non-paused,
    /// non-warping) flight — the case that actually matters for the observed stutter — the two busiest
    /// dispatches (<c>Update</c> and <c>FixedUpdate</c>, plus <c>LateUpdate</c>) go through the *static*
    /// generic <c>UpdateGroup&lt;T&gt;.UpdateMultiple(...)</c> overloads instead, which the original
    /// instrumentation never touched — hence "patched 29/29" but zero <c>AllocTop</c> output.
    /// <para/>
    /// Instead, this patches <c>FlightGameLoop</c>'s own concrete (non-generic) per-phase methods
    /// directly: <c>PreUpdate</c>, <c>Update</c>, <c>PostUpdate</c>, <c>PreFixedUpdate</c>,
    /// <c>FixedUpdate</c>, <c>PostFixedUpdate</c>, <c>PreLateUpdate</c>, <c>LateUpdate</c>,
    /// <c>PostLateUpdate</c>, <c>EndOfFrame</c>. These are the real, concrete call sites Unity invokes
    /// every frame, so this reliably captures 100% of managed allocation for each coarse phase,
    /// regardless of which internal dispatch mechanism (Update vs UpdateMultiple vs
    /// ParallelUpdateAndComplete) a given phase happens to use internally.
    /// </remarks>
    internal static class AllocationAttribution
    {
        // The concrete per-frame phase methods on FlightGameLoop, in roughly execution order.
        private static readonly string[] MonitoredMethods =
        {
            "PreUpdate", "Update", "PostUpdate",
            "PreFixedUpdate", "FixedUpdate", "PostFixedUpdate",
            "PreLateUpdate", "LateUpdate", "PostLateUpdate",
            "EndOfFrame",
        };

        private static readonly Dictionary<string, long> BytesByGroup = new Dictionary<string, long>();

        private static bool _installed;

        /// <summary>
        /// Locates and patches each of <see cref="MonitoredMethods"/> on <c>Assets.Scripts.GameLoop.FlightGameLoop</c>.
        /// Safe to call once; failures for individual phases are logged and skipped rather than aborting the rest.
        /// </summary>
        public static void Install(Harmony harmony)
        {
            if (_installed)
                return;
            _installed = true;

            var simpleRockets2 = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "SimpleRockets2");
            var flightGameLoopType = simpleRockets2?.GetType("Assets.Scripts.GameLoop.FlightGameLoop");
            if (flightGameLoopType == null)
            {
                Debug.LogError(
                    "[KellyUtils] AllocationAttribution: could not find Assets.Scripts.GameLoop.FlightGameLoop — "
                    + "skipping allocation attribution (game internals may have changed).");
                return;
            }

            var prefix = new HarmonyMethod(typeof(AllocationAttribution), nameof(Prefix));
            var postfix = new HarmonyMethod(typeof(AllocationAttribution), nameof(Postfix));

            var patchedCount = 0;
            foreach (var methodName in MonitoredMethods)
            {
                try
                {
                    var method = flightGameLoopType.GetMethod(
                        methodName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        Type.EmptyTypes,
                        null);

                    if (method == null)
                    {
                        Debug.LogWarning($"[KellyUtils] AllocationAttribution: no parameterless '{methodName}' method found on FlightGameLoop — skipping.");
                        continue;
                    }

                    harmony.Patch(method, prefix, postfix);
                    patchedCount++;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[KellyUtils] AllocationAttribution: failed to patch phase '{methodName}': {ex}");
                }
            }

            Debug.Log($"[KellyUtils] AllocationAttribution: patched {patchedCount}/{MonitoredMethods.Length} FlightGameLoop phases.");
        }

        /// <summary>
        /// Removes and returns the accumulated allocation totals (bytes) per phase since the last call,
        /// resetting all counters to zero.
        /// </summary>
        public static Dictionary<string, long> DrainAndReset()
        {
            lock (BytesByGroup)
            {
                var snapshot = new Dictionary<string, long>(BytesByGroup);
                BytesByGroup.Clear();
                return snapshot;
            }
        }

        // ReSharper disable once UnusedMember.Local
        private static void Prefix(out long __state)
        {
            __state = GC.GetAllocatedBytesForCurrentThread();
        }

        // ReSharper disable once UnusedMember.Local
        private static void Postfix(MethodBase __originalMethod, long __state)
        {
            var delta = GC.GetAllocatedBytesForCurrentThread() - __state;
            if (delta <= 0)
                return;

            var key = __originalMethod.Name;

            lock (BytesByGroup)
            {
                BytesByGroup.TryGetValue(key, out var existing);
                BytesByGroup[key] = existing + delta;
            }
        }
    }
}
