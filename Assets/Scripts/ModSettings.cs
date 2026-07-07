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
        }
    }
}