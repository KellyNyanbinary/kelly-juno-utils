using Rendering;

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
        /// If enabled, applies SMAA 1x to the composited scene image on the master camera,
        /// replacing the stock FXAA/DLAA post-process antialiasing. SMAA reconstructs edge
        /// gradients using pattern classification rather than a directional blur, so it preserves
        /// texture detail noticeably better than FXAA or DLAA.
        /// </summary>
        public BoolSetting EnableSmaa { get; private set; }

        /// <summary>
        /// The SMAA quality preset, controlling the edge-detection threshold, the number of edge
        /// search steps, and whether diagonal and corner detection run.
        /// </summary>
        public EnumSetting<SmaaQualityPreset> SmaaQuality { get; private set; }

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

            EnableSmaa = CreateBool("Enable SMAA")
                .SetDescription(
                    "Applies SMAA 1x anti-aliasing to the final scene image, replacing the stock FXAA/DLAA option. Preserves texture detail noticeably better than FXAA or DLAA, at roughly 0.3-0.8 ms per frame at 1080p. Works alongside MSAA if you also have MSAA selected in Display settings.")
                .SetDefault(false);

            SmaaQuality = CreateEnum<SmaaQualityPreset>("SMAA Quality")
                .SetDescription(
                    "The SMAA quality preset. Higher presets trace edges further and enable diagonal and corner detection. Only used when SMAA is enabled.")
                .SetDefault(SmaaQualityPreset.High);
        }
    }
}