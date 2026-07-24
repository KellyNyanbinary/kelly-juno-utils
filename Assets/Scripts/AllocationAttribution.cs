namespace Assets.Scripts
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using HarmonyLib;
    using ModApi.GameLoop.Interfaces;
    using UnityEngine;

    /// <summary>
    /// Attributes managed-heap growth to concrete flight-update classes.
    /// </summary>
    /// <remarks>
    /// The game dispatches <see cref="IUpdate"/> and <see cref="IFlightUpdate"/> implementations
    /// sequentially on the main thread. Patching their concrete target methods narrows allocation
    /// beyond the coarse <c>FlightGameLoop.Update</c> phase without modifying game code.
    /// </remarks>
    internal static class AllocationAttribution
    {
        private static readonly Type[] MonitoredInterfaces =
        {
            typeof(IUpdate),
            typeof(IFlightUpdate),
        };

        private static readonly Dictionary<string, long> HeapGrowthByType =
            new Dictionary<string, long>();

        private static bool _installed;
        public static bool Enabled { get; set; }

        /// <summary>
        /// Locates and patches the concrete methods backing <see cref="MonitoredInterfaces"/>.
        /// </summary>
        public static void Install(Harmony harmony)
        {
            if (_installed)
                return;
            _installed = true;

            var simpleRockets2 = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "SimpleRockets2");
            if (simpleRockets2 == null)
            {
                Debug.LogError(
                    "[KellyUtils] AllocationAttribution: could not find SimpleRockets2 assembly.");
                return;
            }

            var prefix = new HarmonyMethod(typeof(AllocationAttribution), nameof(Prefix));
            var postfix = new HarmonyMethod(typeof(AllocationAttribution), nameof(Postfix));
            var patchedMethods = new HashSet<MethodInfo>();
            var patchedCount = 0;

            foreach (var type in GetLoadableTypes(simpleRockets2)
                         .Where(type => type.IsClass && !type.IsAbstract))
            {
                foreach (var monitoredInterface in MonitoredInterfaces)
                {
                    if (!monitoredInterface.IsAssignableFrom(type))
                        continue;

                    try
                    {
                        var interfaceMap = type.GetInterfaceMap(monitoredInterface);
                        foreach (var method in interfaceMap.TargetMethods)
                        {
                            if (!patchedMethods.Add(method))
                                continue;

                            harmony.Patch(method, prefix, postfix);
                            patchedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning(
                            $"[KellyUtils] AllocationAttribution: failed to patch "
                            + $"{type.FullName} as {monitoredInterface.Name}: {ex}");
                    }
                }
            }

            Debug.Log(
                $"[KellyUtils] AllocationAttribution: patched {patchedCount} concrete Update methods.");
        }

        /// <summary>
        /// Removes and returns the accumulated heap-growth totals (bytes) per type since the last call,
        /// resetting all counters to zero.
        /// </summary>
        public static Dictionary<string, long> TakeSnapshot()
        {
            lock (HeapGrowthByType)
            {
                var snapshot = new Dictionary<string, long>(HeapGrowthByType);
                HeapGrowthByType.Clear();
                return snapshot;
            }
        }

        // ReSharper disable once UnusedMember.Local
        private static void Prefix(object __instance, out Measurement __state)
        {
            __state = Enabled
                ? new Measurement(__instance.GetType().Name, GC.GetTotalMemory(false))
                : default;
        }

        // ReSharper disable once UnusedMember.Local
        private static void Postfix(Measurement __state)
        {
            if (__state.TypeName == null)
                return;

            var delta = GC.GetTotalMemory(false) - __state.Memory;
            if (delta <= 0)
                return;

            lock (HeapGrowthByType)
            {
                HeapGrowthByType.TryGetValue(__state.TypeName, out var existing);
                HeapGrowthByType[__state.TypeName] = existing + delta;
            }
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(type => type != null);
            }
        }

        private readonly struct Measurement
        {
            public Measurement(string typeName, long memory)
            {
                TypeName = typeName;
                Memory = memory;
            }

            public string TypeName { get; }
            public long Memory { get; }
        }
    }
}
