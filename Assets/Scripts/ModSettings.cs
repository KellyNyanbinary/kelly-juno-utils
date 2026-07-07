namespace Assets.Scripts
{
    using ModApi.Settings.Core;

    /// <summary>
    /// The settings for the mod.
    /// </summary>
    /// <seealso cref="ModApi.Settings.Core.SettingsCategory{Assets.Scripts.ModSettings}" />
    public class ModSettings : SettingsCategory<ModSettings>
    {
        /// <summary>
        /// The mod settings instance.
        /// </summary>
        private static ModSettings _instance;

        /// <summary>
        /// Initializes a new instance of the <see cref="ModSettings"/> class.
        /// </summary>
        public ModSettings() : base("Kelly Utils")
        {
        }

        /// <summary>
        /// Gets the mod settings instance.
        /// </summary>
        /// <value>
        /// The mod settings instance.
        /// </value>
        public static ModSettings Instance =>
            _instance ?? (_instance = Game.Instance.Settings.ModSettings.GetCategory<ModSettings>());

        /// <summary>
        /// The maximum distance (meters) the craft can drift from the floating origin
        /// before a reference-frame recenter is forced. The stock game uses ~5000 m,
        /// which is loose enough that 32-bit float precision loss causes visible part
        /// jitter well before recentering happens.
        /// </summary>
        public NumericSetting<float> RecenterDistance { get; private set; }

        /// <summary>
        /// If enabled, prevents the first-person camera's far clip plane from collapsing
        /// to a very short distance whenever the astronaut or a physical Camera part is
        /// used in first-person view. Fixes reduced terrain scatter (Juno Parallax) and
        /// shadow draw distance in FPV.
        /// </summary>
        public BoolSetting FixFirstPersonDrawDistance { get; private set; }

        /// <summary>
        /// When enabled, hands the garbage collector over to <see cref="GCScheduler"/>,
        /// which only forces collections while the game is paused or outside the flight
        /// scene (menus, designer, etc.), instead of letting Unity trigger a blocking,
        /// non-incremental collection at an arbitrary, potentially performance-critical
        /// moment during flight.
        /// </summary>
        public BoolSetting EnableManualGCScheduling { get; private set; }

        /// <summary>
        /// Minimum real time (seconds) between opportunistic collections forced during a
        /// safe window (paused / non-flight scene). Prevents repeatedly collecting on every
        /// brief pause when there's little garbage to reclaim.
        /// </summary>
        public NumericSetting<float> GCMinIntervalSeconds { get; private set; }

        /// <summary>
        /// Hard managed-heap safety cap (megabytes). If the heap grows past this size, a
        /// collection is forced immediately regardless of whether it's a safe window, to
        /// avoid unbounded memory growth while the scheduler waits for a safe moment.
        /// </summary>
        public NumericSetting<float> GCHeapSafetyCapMB { get; private set; }

        /// <summary>
        /// When enabled, logs the managed heap allocation rate once per second, along with
        /// craft state (part count, throttle, player control, altitude), to help attribute
        /// GC pressure to specific flight conditions. Diagnostic only; produces frequent log
        /// output, so it defaults to off.
        /// </summary>
        public BoolSetting EnableAllocationWatchdog { get; private set; }

        /// <summary>
        /// When enabled, logs the top allocating per-frame update phases (e.g. IFlightFixedUpdate,
        /// IFlightUpdate) once per second, measured via Harmony patches on the game's internal
        /// update dispatch. See <see cref="AllocationAttribution"/>. Diagnostic only, off by default.
        /// </summary>
        public BoolSetting EnableAllocationAttribution { get; private set; }

        /// <summary>
        /// Initializes the settings in the category.
        /// </summary>
        protected override void InitializeSettings()
        {
            RecenterDistance = CreateNumeric("Recenter Distance", 100f, 5000f, 100f)
                .SetDescription(
                    "Distance (m) from the floating origin at which the flight scene forces a reference-frame recenter. Lower values reduce part jitter caused by 32-bit float precision loss. Stock game default is ~5000 m.")
                .SetDisplayFormatter(x => x.ToString("F0") + " m")
                .SetDefault(100f);

            FixFirstPersonDrawDistance = CreateBool("Fix First-Person Draw Distance")
                .SetDescription(
                    "If enabled, forces the first-person camera's near/far clip plane to start at their normal full-range values instead of collapsing toward a very short far clip whenever the astronaut or a physical Camera part is used in first-person view. Fixes reduced terrain scatter (for Juno Parallax) and shadow draw distance in FPV.")
                .SetDefault(true);

            this.EnableManualGCScheduling = this.CreateBool("Manual GC Scheduling")
                .SetDescription(
                    "EXPERIMENTAL / LIKELY INEFFECTIVE: attempts to defer forced garbage collections to safe windows (paused / non-flight scene). Testing showed GarbageCollector.GCMode has no effect on this build's automatic collector (it appears tied to Unity's incremental GC, which this build was compiled without), so automatic collections still occur during flight regardless of this setting. Left enabled only as a no-cost safety net (heap cap + opportunistic collection while paused).")
                .SetDefault(false);

            this.GCMinIntervalSeconds = this.CreateNumeric("GC Min Interval", 5f, 120f, 5f)
                .SetDescription(
                    "Minimum time (seconds) between opportunistic collections forced during a safe window (paused / non-flight scene).")
                .SetDisplayFormatter(x => x.ToString("F0") + " s")
                .SetDefault(15f);

            this.GCHeapSafetyCapMB = this.CreateNumeric("GC Heap Safety Cap", 256f, 4096f, 256f)
                .SetDescription(
                    "If the managed heap grows past this size (MB), a collection is forced immediately even mid-flight, to prevent unbounded memory growth while waiting for a safe (paused) moment.")
                .SetDisplayFormatter(x => x.ToString("F0") + " MB")
                .SetDefault(1024f);

            this.EnableAllocationWatchdog = this.CreateBool("Allocation Watchdog Logging")
                .SetDescription(
                    "Logs managed heap allocation rate once per second along with craft state, to help identify what's producing garbage during flight. Diagnostic only, off by default.")
                .SetDefault(false);

            this.EnableAllocationAttribution = this.CreateBool("Allocation Attribution Logging")
                .SetDescription(
                    "Logs the top allocating per-frame update phases (e.g. IFlightFixedUpdate, IFlightUpdate) once per second, via Harmony patches on the game's internal update dispatch. Diagnostic only, off by default.")
                .SetDefault(false);
        }
    }
}