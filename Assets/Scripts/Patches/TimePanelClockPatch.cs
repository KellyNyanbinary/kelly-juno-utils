using System.Diagnostics.CodeAnalysis;
using System.Text;
using Assets.Scripts;
using Assets.Scripts.Flight.UI;
using Flight;
using HarmonyLib;
using JetBrains.Annotations;
using ModApi.Flight.Sim;
using TMPro;

namespace Patches
{
    /// <summary>
    /// Harmony postfix patch for <see cref="TimePanelController.UpdatePanel" /> that appends the
    /// home planet's time, and optionally the local celestial body's time, to the flight clock.
    /// </summary>
    /// <remarks>
    /// The stock clock only shows mission elapsed time or the total elapsed time in days, neither
    /// of which says anything about where the sun is. This appends a line per body giving the solar
    /// time of day at that body's prime meridian, so the home planet reads like a mission control
    /// clock and the local body reads like local time when the craft has left home. The stock
    /// method rewrites the whole label whenever the displayed second changes, so the extra lines
    /// are simply re-appended whenever the label no longer matches what was last written.
    /// </remarks>
    [UsedImplicitly]
    [HarmonyPatch(typeof(TimePanelController), nameof(TimePanelController.UpdatePanel))]
    internal static class TimePanelControllerClockPatch
    {
        // Cached reflected accessor for the private time label on TimePanelController.
        // AccessTools.FieldRefAccess throws at construction if the field is missing, so any
        // incompatibility with a future game version fails loudly at mod loading rather than
        // silently no-op'ing on every frame.
        private static readonly AccessTools.FieldRef<TimePanelController, TextMeshProUGUI> TimeTextRef =
            AccessTools.FieldRefAccess<TimePanelController, TextMeshProUGUI>("_timeText");

        // The last label this patch wrote, used to tell "the stock method just rewrote the label"
        // apart from "nothing has changed since the extra lines were appended".
        private static string _lastText;

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        [UsedImplicitly]
        [HarmonyPostfix]
        private static void Postfix(TimePanelController __instance)
        {
            var showHomePlanet = ModSettings.Instance.ShowHomePlanetTime.Value;
            var showLocalBody = ModSettings.Instance.ShowLocalBodyTime.Value;
            if (!showHomePlanet && !showLocalBody) return;

            var timeText = TimeTextRef(__instance);
            if (timeText == null) return;

            var text = timeText.text;
            if (string.IsNullOrEmpty(text) || text == _lastText) return;

            var newLineIndex = text.IndexOf('\n');
            var missionTime = newLineIndex >= 0 ? text.Substring(0, newLineIndex) : text;

            _lastText = missionTime + BuildClockLines(showHomePlanet, showLocalBody);
            timeText.text = _lastText;
        }

        /// <summary>
        /// Builds the extra clock lines to append to the mission time.
        /// </summary>
        /// <param name="showHomePlanet">Whether the home planet's time is shown.</param>
        /// <param name="showLocalBody">Whether the local celestial body's time is shown.</param>
        /// <returns>The extra lines, or an empty string if there are none.</returns>
        private static string BuildClockLines(bool showHomePlanet, bool showLocalBody)
        {
            var localBody = Game.Instance.FlightScene?.CraftNode?.Parent;
            var homePlanet = CelestialClock.GetHomePlanet(localBody);

            var builder = new StringBuilder();
            if (showHomePlanet)
                AppendClockLine(builder, homePlanet);

            // The local body's time only adds anything when the craft has left the home planet.
            if (showLocalBody && !ReferenceEquals(localBody, homePlanet))
                AppendClockLine(builder, localBody);

            return builder.Length == 0 ? string.Empty : $"\n<size=70%>{builder}</size>";
        }

        /// <summary>
        /// Appends a body's name and solar time of day, if one can be computed for it.
        /// </summary>
        /// <param name="builder">The builder to append to.</param>
        /// <param name="planet">The celestial body.</param>
        private static void AppendClockLine(StringBuilder builder, IPlanetNode planet)
        {
            if (planet is null || !CelestialClock.TryGetTimeOfDay(planet, out var timeOfDay))
                return;

            if (builder.Length > 0)
                builder.Append('\n');

            builder.Append(planet.PlanetData?.Name ?? planet.Name);
            builder.Append(' ');
            builder.Append(CelestialClock.FormatTimeOfDay(timeOfDay));
        }
    }
}
