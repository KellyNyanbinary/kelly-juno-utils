using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Assets.Scripts;
using ModApi.Flight.Sim;
using ModApi.Ui;
using TMPro;
using UI.Xml;
using UnityEngine;

namespace Flight
{
    /// <summary>
    /// An extra row in the flight scene's time panel showing the flight's date and time of day on
    /// Earth's calendar, on the home planet's, and on that of the body the craft is at.
    /// </summary>
    /// <remarks>
    /// The stock clock only shows mission elapsed time or the total elapsed time in days, neither
    /// of which says anything about the date or about where the sun is. The row is injected into
    /// the stock time panel's XML through ModApi's user interface build action, so this owns a
    /// label of its own rather than rewriting the stock clock's, and the panel's own layout places
    /// and sizes it. A single instance of this, living for as long as the mod does, keeps the label
    /// up to date.
    /// </remarks>
    internal class CelestialClockPanel : MonoBehaviour
    {
        private const string RowId = "kelly-utils-clock-row";
        private const string TextId = "kelly-utils-clock-text";
        private const string FontSize = "14";

        // Explains the calendar, and is also what makes the row a raycast target, which it has to
        // be for the tooltip to be raised at all. The lines are kept short because the tooltip is
        // as wide as its widest line.
        private const string TooltipFormat =
            "Dates start at year {0}, day {0} at flight start.\n" +
            "A day is a solar day: noon to noon, not one\n" +
            "rotation. A year is one orbit around the sun,\n" +
            "and a moon keeps its planet's. A new year\n" +
            "starts on the next whole day, not partway\n" +
            "through one.";

        // Vertical padding (px) added around the text to size the row, and the height the row is
        // built with before any text has been measured.
        private const int RowPadding = 8;
        private const int InitialRowHeight = 26;

        // How often the flight scene is searched for the injected row while it is not attached.
        private const float SearchInterval = 0.5f;

        private IXmlElement _row;
        private TextMeshProUGUI _text;

        private float _nextSearchTime;
        private long _second = long.MinValue;
        private bool _startAtOne;
        private bool _rowVisible;
        private int _rowHeight = InitialRowHeight;

        /// <summary>
        /// Registers the time panel build action and starts the updater that fills the row in.
        /// Call once while the mod is initializing, before any flight scene has loaded.
        /// </summary>
        public static void Register()
        {
            Game.Instance.UserInterface.AddBuildUserInterfaceXmlAction(
                UserInterfaceIds.Flight.TimePanel,
                OnBuildTimePanel);

            var updater = new GameObject("Kelly Utils Celestial Clock");
            DontDestroyOnLoad(updater);
            updater.AddComponent<CelestialClockPanel>();
        }

        /// <summary>
        /// Adds the clock row to the stock time panel layout.
        /// </summary>
        /// <param name="request">The layout being built.</param>
        private static void OnBuildTimePanel(BuildUserInterfaceXmlRequest request)
        {
            var root = request.XmlDocument.Root;
            var container = root?.Elements().FirstOrDefault(e => e.Name.LocalName == "VerticalLayout");
            if (container is null)
            {
                Debug.LogError(
                    "[KellyUtils] The stock time panel layout was not in the expected shape, " +
                    "so the celestial clock was not added to it.");
                return;
            }

            var ns = root.Name.Namespace;
            var row = new XElement(
                ns + "Panel",
                new XAttribute("id", RowId),
                new XAttribute("class", "flight-panel"),
                new XAttribute("preferredHeight", InitialRowHeight),
                new XAttribute("active", "false"),
                new XAttribute("tooltip", BuildTooltip(ModSettings.Instance.DatesStartAtOne.Value)),
                new XElement(
                    ns + "TextMeshPro",
                    new XAttribute("id", TextId),
                    new XAttribute("class", "value"),
                    new XAttribute("fontSize", FontSize),
                    new XAttribute("alignment", "Center"),
                    new XAttribute("text", string.Empty)));

            // Directly under the clock, and above the adjust panel so the row does not move as that
            // panel shows and hides underneath it.
            var adjustPanel = container.Elements().FirstOrDefault(e => (string)e.Attribute("id") == "adjust-panel");
            if (adjustPanel is null)
            {
                container.Add(row);
            }
            else
            {
                adjustPanel.AddBeforeSelf(row);
            }
        }

        /// <summary>
        /// Looks for the injected row in the flight scene's user interface.
        /// </summary>
        /// <remarks>
        /// The layout cannot hand the row over itself: the stock time panel's controller overrides
        /// <c>LayoutRebuilt</c> without calling its base, so no layout rebuilt callback is raised
        /// for it. The row is instead searched for, which is safe because the id only exists in the
        /// layout injected into above.
        /// </remarks>
        /// <returns>Whether the row was found.</returns>
        private bool TrySearchForRow()
        {
            if (Time.unscaledTime < _nextSearchTime) return false;
            _nextSearchTime = Time.unscaledTime + SearchInterval;

            var ui = Game.Instance.SceneManager.InFlightScene ? Game.Instance.UserInterface.Transform : null;
            if (ui == null) return false;

            foreach (var layout in ui.GetComponentsInChildren<XmlLayout>(true))
            {
                var row = layout.GetElementById(RowId);
                var text = layout.GetElementById<TextMeshProUGUI>(TextId);
                if (row == null || text == null) continue;

                _row = row;
                _text = text;
                _rowVisible = false;
                _rowHeight = InitialRowHeight;
                _second = long.MinValue;
                return true;
            }

            return false;
        }

        private void Update()
        {
            // The label is destroyed with the flight scene, so this falls back to searching.
            if (_text == null && !TrySearchForRow()) return;

            // Every displayed clock reads whole seconds of flight time, so there is nothing to
            // redraw until that second, or the setting the dates are numbered by, changes.
            var flightScene = Game.Instance.FlightScene;
            var time = flightScene?.FlightState?.Time ?? 0.0;
            var second = (long)time;
            var startAtOne = ModSettings.Instance.DatesStartAtOne.Value;
            if (second == _second && startAtOne == _startAtOne) return;

            _second = second;
            _startAtOne = startAtOne;
            Refresh(flightScene?.CraftNode?.Parent, time, startAtOne);
        }

        /// <summary>
        /// Rebuilds the row's text and tooltip.
        /// </summary>
        /// <param name="localBody">The celestial body the craft is at.</param>
        /// <param name="time">The flight time in seconds.</param>
        /// <param name="startAtOne">Whether the first year and day are numbered 1 rather than 0.</param>
        private void Refresh(IPlanetNode localBody, double time, bool startAtOne)
        {
            var homePlanet = CelestialClock.GetHomePlanet(localBody);
            var lines = new StringBuilder();
            var tooltip = new StringBuilder(BuildTooltip(startAtOne));

            // Earth's calendar is always shown, so a body that keeps it needs no line of its own.
            AppendLine(lines, tooltip, CelestialClock.EarthName, CelestialClock.FormatEarthDate(time, startAtOne),
                CelestialClock.FormatCalendar(CelestialClock.EarthDaysPerYear, CelestialClock.EarthHoursPerDay));

            if (!CelestialClock.IsEarth(homePlanet))
                AppendBody(lines, tooltip, homePlanet, time, startAtOne);

            // The local body only adds a line of its own once the craft has left the home planet.
            if (!ReferenceEquals(localBody, homePlanet) && !CelestialClock.IsEarth(localBody))
                AppendBody(lines, tooltip, localBody, time, startAtOne);

            SetRow(lines.ToString(), tooltip.ToString());
        }

        /// <summary>
        /// Builds the part of the tooltip that explains the calendar.
        /// </summary>
        /// <param name="startAtOne">Whether the first year and day are numbered 1 rather than 0.</param>
        /// <returns>The explanation.</returns>
        private static string BuildTooltip(bool startAtOne) =>
            string.Format(CultureInfo.InvariantCulture, TooltipFormat, startAtOne ? 1 : 0);

        /// <summary>
        /// Shows the given text and tooltip, sizing the row to the text.
        /// </summary>
        /// <param name="text">The text to show.</param>
        /// <param name="tooltip">The tooltip to show.</param>
        private void SetRow(string text, string tooltip)
        {
            _text.text = text;
            _row.Tooltip = tooltip;

            var height = Mathf.CeilToInt(_text.GetPreferredValues(text).y) + RowPadding;
            if (height != _rowHeight)
            {
                _rowHeight = height;
                _row.SetAndApplyAttribute("preferredHeight", height.ToString(CultureInfo.InvariantCulture));
            }

            if (!_rowVisible) _row.Show();
            _rowVisible = true;
        }

        /// <summary>
        /// Appends a body's date, and the lengths of its year and day, if they can be computed.
        /// </summary>
        /// <param name="lines">The builder of the displayed lines.</param>
        /// <param name="tooltip">The builder of the tooltip.</param>
        /// <param name="planet">The celestial body.</param>
        /// <param name="time">The flight time in seconds.</param>
        /// <param name="startAtOne">Whether the first year and day are numbered 1 rather than 0.</param>
        private static void AppendBody(
            StringBuilder lines, StringBuilder tooltip, IPlanetNode planet, double time, bool startAtOne)
        {
            if (planet is null || !CelestialClock.TryFormatDate(planet, time, startAtOne, out var date, out var calendar))
                return;

            AppendLine(lines, tooltip, CelestialClock.GetName(planet), date, calendar);
        }

        /// <summary>
        /// Appends a line of the display, and the calendar it is on to the tooltip.
        /// </summary>
        /// <param name="lines">The builder of the displayed lines.</param>
        /// <param name="tooltip">The builder of the tooltip.</param>
        /// <param name="name">The name of the body the line is for.</param>
        /// <param name="date">The formatted date.</param>
        /// <param name="calendar">The formatted lengths of the body's year and day.</param>
        private static void AppendLine(
            StringBuilder lines, StringBuilder tooltip, string name, string date, string calendar)
        {
            if (lines.Length > 0)
                lines.Append('\n');
            else
                tooltip.Append('\n'); // Separates the description from the list of calendars.

            lines.Append(name).Append(' ').Append(date);
            tooltip.Append('\n').Append(name).Append(": ").Append(calendar);
        }
    }
}
