using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Assets.Scripts;
using ModApi.Craft;
using ModApi.Flight.Sim;
using ModApi.Ui;
using TMPro;
using UI.Xml;
using UnityEngine;

namespace Flight
{
    /// <summary>
    /// An extra row in the flight scene's time panel showing universe dates or elapsed time using
    /// Earth days and the solar calendars of the home planet and the body the craft is at.
    /// </summary>
    /// <remarks>
    /// The row is injected into the stock time panel's XML through ModApi's user interface build
    /// action. The panel's layout places and sizes it, while one persistent instance updates it.
    /// <para>
    /// The Gregorian Earth clock uses real-world calendar constants. A planetary system can define
    /// its Earth with different day or year lengths, or a different initial rotation, so the panel
    /// can also show a separate in-system Earth clock when the two disagree.
    /// </para>
    /// </remarks>
    internal class CelestialClockPanel : MonoBehaviour
    {
        private enum ClockOrigin
        {
            Universe,
            CraftLaunch,
            CraftSession
        }

        private readonly struct ClockContext
        {
            public ClockContext(
                double time, double universeTime, bool startAtOne, ClockOrigin origin)
            {
                Time = time;
                UniverseTime = universeTime;
                StartAtOne = startAtOne;
                Origin = origin;
            }

            public double Time { get; }
            public double UniverseTime { get; }
            public bool StartAtOne { get; }
            public ClockOrigin Origin { get; }
            public bool IsUniverseDate => Origin == ClockOrigin.Universe;
            public string Prefix => GetClockPrefix(Origin);
        }

        private sealed class ClockColumns
        {
            private readonly StringBuilder _labels = new();
            private readonly StringBuilder _middle = new();
            private readonly StringBuilder _values = new();
            private bool _hasRows;

            public string Labels => _labels.ToString();
            public string Middle => _middle.ToString();
            public string Values => _values.ToString();

            public void Append(string label, string middle, string value)
            {
                if (_hasRows)
                {
                    _labels.Append('\n');
                    _middle.Append('\n');
                    _values.Append('\n');
                }

                _labels.Append(label);
                _middle.Append(middle);
                _values.Append(value);
                _hasRows = true;
            }
        }

        private const string RowId = "kelly-utils-clock-row";
        private const string LabelColumnId = "kelly-utils-clock-labels";
        private const string MiddleColumnId = "kelly-utils-clock-middle";
        private const string ValueColumnId = "kelly-utils-clock-values";
        private const string FontSize = "14";
        private const int ColumnSpacing = 6;
        private const int HorizontalPadding = 6;

        // The tooltip attribute also makes the row a raycast target. Keep lines short because the
        // tooltip does not wrap automatically.
        private const string CalendarExplanation =
            "A day is a solar day: noon to noon instead of\n" +
            "one rotation. A year is one orbit around the\n" +
            "sun, and a moon keeps its planet's. A new year\n" +
            "starts on the next whole day rather than\n" +
            "partway through one.";

        // Vertical padding (px) added around the text to size the row, and the height the row is
        // built with before any text has been measured.
        private const int RowPadding = 8;
        private const int InitialRowHeight = 26;

        // How often the flight scene is searched for the injected row while it is not attached.
        private const float SearchInterval = 0.5f;

        private IXmlElement _row;
        private IXmlElement _labelColumnElement;
        private IXmlElement _middleColumnElement;
        private IXmlElement _valueColumnElement;
        private TextMeshProUGUI _labelColumn;
        private TextMeshProUGUI _middleColumn;
        private TextMeshProUGUI _valueColumn;

        private float _nextSearchTime;
        private long _second = long.MinValue;
        private bool _datesStartAtOne;
        private bool _showInSystemEarthClock;
        private bool _showEridReferenceClock;
        private ClockOrigin _clockOrigin;
        private ICraftNode _activeCraft;
        private double _sessionStartTime;
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
                new XAttribute(
                    "tooltip",
                    LeftAlignTooltip(
                        BuildTooltip(
                            ModSettings.Instance.DatesStartAtOne.Value,
                            ClockOrigin.Universe))),
                new XElement(
                    ns + "HorizontalLayout",
                    new XAttribute("spacing", ColumnSpacing),
                    new XAttribute(
                        "padding",
                        $"{HorizontalPadding} {HorizontalPadding} 0 0"),
                    new XAttribute("childForceExpandWidth", false),
                    new XAttribute("flexibleWidth", 1),
                    CreateTextColumn(ns, LabelColumnId, "TopLeft", flexible: true),
                    CreateTextColumn(ns, MiddleColumnId, "TopRight"),
                    CreateTextColumn(ns, ValueColumnId, "TopRight")));

            // Keep the row above the adjustment panel so it stays put when that panel toggles.
            var adjustPanel = container.Elements().FirstOrDefault(e => (string)e.Attribute("id") == "adjust-panel");
            if (adjustPanel is null)
                container.Add(row);
            else
                adjustPanel.AddBeforeSelf(row);
        }

        private static XElement CreateTextColumn(
            XNamespace ns, string id, string alignment, bool flexible = false) =>
            new(
                ns + "TextMeshPro",
                new XAttribute("id", id),
                new XAttribute("class", "value"),
                new XAttribute("fontSize", FontSize),
                new XAttribute("alignment", alignment),
                new XAttribute("flexibleWidth", flexible ? 1 : 0),
                new XAttribute("text", string.Empty));

        /// <summary>
        /// Looks for the injected row in the flight scene's user interface.
        /// </summary>
        /// <remarks>
        /// <c>TimePanelController.LayoutRebuilt</c> skips its base implementation, so ModApi rebuild
        /// callbacks never run for this layout. The injected id makes the fallback search unambiguous.
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
                var labelElement = layout.GetElementById(LabelColumnId);
                var middleElement = layout.GetElementById(MiddleColumnId);
                var valueElement = layout.GetElementById(ValueColumnId);
                var labels = layout.GetElementById<TextMeshProUGUI>(LabelColumnId);
                var middle = layout.GetElementById<TextMeshProUGUI>(MiddleColumnId);
                var values = layout.GetElementById<TextMeshProUGUI>(ValueColumnId);
                if (row == null ||
                    labelElement == null ||
                    middleElement == null ||
                    valueElement == null ||
                    labels == null ||
                    middle == null ||
                    values == null)
                    continue;

                _row = row;
                _labelColumnElement = labelElement;
                _middleColumnElement = middleElement;
                _valueColumnElement = valueElement;
                _labelColumn = labels;
                _middleColumn = middle;
                _valueColumn = values;
                _row.AddOnClickEvent(CycleClockOrigin);
                _rowVisible = false;
                _rowHeight = InitialRowHeight;
                _second = long.MinValue;
                return true;
            }

            return false;
        }

        private void Update()
        {
            var flightScene = Game.Instance.FlightScene;
            var universeTime = flightScene?.FlightState?.Time ?? 0.0;
            var craft = flightScene?.CraftNode;
            if (!ReferenceEquals(craft, _activeCraft))
            {
                _activeCraft = craft;
                _sessionStartTime = universeTime;
                _second = long.MinValue;
            }

            // The columns are destroyed with the flight scene, so this falls back to searching.
            if (_valueColumn == null && !TrySearchForRow()) return;

            // All formats show whole seconds. Refresh when the second or a clock setting changes.
            var time = GetClockTime(craft, universeTime);
            var second = (long)time;
            var datesStartAtOne = ModSettings.Instance.DatesStartAtOne.Value;
            var showInSystemEarthClock = ModSettings.Instance.ShowInSystemEarthClock.Value;
            var showEridReferenceClock = ModSettings.Instance.ShowEridReferenceClock.Value;
            if (second == _second &&
                datesStartAtOne == _datesStartAtOne &&
                showInSystemEarthClock == _showInSystemEarthClock &&
                showEridReferenceClock == _showEridReferenceClock)
                return;

            _second = second;
            _datesStartAtOne = datesStartAtOne;
            _showInSystemEarthClock = showInSystemEarthClock;
            _showEridReferenceClock = showEridReferenceClock;
            var context = new ClockContext(
                time, universeTime, datesStartAtOne, _clockOrigin);
            Refresh(
                craft?.Parent,
                context,
                showInSystemEarthClock,
                showEridReferenceClock);
        }

        private void CycleClockOrigin()
        {
            _clockOrigin = _clockOrigin switch
            {
                ClockOrigin.Universe => ClockOrigin.CraftLaunch,
                ClockOrigin.CraftLaunch => ClockOrigin.CraftSession,
                _ => ClockOrigin.Universe
            };
            _second = long.MinValue;
        }

        /// <summary>
        /// Gets the time from the selected origin. Merged craft use their earliest constituent
        /// launch; session time starts when the craft becomes active.
        /// </summary>
        /// <param name="craft">The active craft.</param>
        /// <param name="universeTime">The current universe time.</param>
        /// <returns>The non-negative time elapsed from the selected origin, in seconds.</returns>
        private double GetClockTime(ICraftNode craft, double universeTime)
        {
            var origin = _clockOrigin switch
            {
                ClockOrigin.CraftLaunch => GetLaunchTime(craft, universeTime),
                ClockOrigin.CraftSession => _sessionStartTime,
                _ => 0.0
            };

            return Math.Max(universeTime - origin, 0.0);
        }

        private static double GetLaunchTime(ICraftNode craft, double fallback)
        {
            var launchTime = fallback;
            if (craft is null)
                return launchTime;

            foreach (var data in craft.InitialCraftNodeData)
                launchTime = Math.Min(launchTime, data.LaunchTime);

            return launchTime;
        }

        /// <summary>
        /// Rebuilds the row's text and tooltip.
        /// </summary>
        /// <param name="localBody">The celestial body that the craft is at.</param>
        /// <param name="context">The selected clock mode and its time values.</param>
        /// <param name="showInSystemEarthClock">Whether a disagreeing physical Earth clock is shown.</param>
        /// <param name="showEridReferenceClock">Whether the fixed Erid reference clock is shown.</param>
        private void Refresh(
            IPlanetNode localBody,
            ClockContext context,
            bool showInSystemEarthClock,
            bool showEridReferenceClock)
        {
            var homePlanet = CelestialClock.GetHomePlanet(localBody);
            var earth = CelestialClock.GetEarth(localBody);
            var columns = new ClockColumns();
            var tooltip = new StringBuilder(
                BuildTooltip(context.StartAtOne, context.Origin));
            AppendEarth(columns, tooltip, earth, context, showInSystemEarthClock);
            if (showEridReferenceClock)
                AppendEridReference(columns, tooltip, context);

            // Earth's calendar is always shown, so a body that keeps it needs no line of its own.
            if (!CelestialClock.IsEarth(homePlanet))
                AppendBody(columns, tooltip, homePlanet, context);

            // The local body only adds a line of its own once the craft has left the home planet.
            if (!ReferenceEquals(localBody, homePlanet) &&
                !CelestialClock.IsEarth(localBody))
                AppendBody(columns, tooltip, localBody, context);

            SetRow(columns, tooltip.ToString());
        }

        /// <summary>
        /// Builds the part of the tooltip that explains the calendar.
        /// </summary>
        /// <param name="startAtOne">Whether universe-date numbering begins at 1.</param>
        /// <param name="origin">The time origin currently shown.</param>
        /// <returns>The explanation.</returns>
        private static string BuildTooltip(bool startAtOne, ClockOrigin origin)
        {
            var tooltip = new StringBuilder()
                .Append("Showing ").Append(GetClockOriginDescription(origin)).Append(".\n");
            if (origin == ClockOrigin.CraftLaunch)
                tooltip.Append("For merged craft, T+ uses the earliest\nconstituent launch.\n");

            tooltip.Append("Click to change the clock's time origin.\n\n");
            if (origin == ClockOrigin.Universe)
            {
                var first = startAtOne ? 1 : 0;
                tooltip.Append("Dates start at year ").Append(first)
                    .Append(", day ").Append(first).Append(" at this origin.\n");
            }
            else
            {
                tooltip.Append("Elapsed time starts at 000:00:00:00.\n");
            }

            return tooltip.Append(CalendarExplanation).ToString();
        }

        private static string GetClockOriginDescription(ClockOrigin origin)
        {
            return origin switch
            {
                ClockOrigin.CraftLaunch => "time since craft launch (T+)",
                ClockOrigin.CraftSession => "time since switching to this craft (S+)",
                _ => "time since the universe started"
            };
        }

        private static string GetClockPrefix(ClockOrigin origin)
        {
            return origin switch
            {
                ClockOrigin.CraftLaunch => "T+",
                ClockOrigin.CraftSession => "S+",
                _ => string.Empty
            };
        }

        private static string LeftAlignTooltip(string tooltip) =>
            "<align=\"left\">" + tooltip + "</align>";

        /// <summary>
        /// Shows the given text and tooltip, sizing the row to the text.
        /// </summary>
        /// <param name="columns">The synchronized label, middle, and value columns.</param>
        /// <param name="tooltip">The tooltip to show.</param>
        private void SetRow(ClockColumns columns, string tooltip)
        {
            var labels = columns.Labels;
            var middle = columns.Middle;
            var values = columns.Values;
            _labelColumn.text = labels;
            _middleColumn.text = middle;
            _valueColumn.text = values;
            _row.Tooltip = LeftAlignTooltip(tooltip);

            var labelSize = _labelColumn.GetPreferredValues(labels);
            var middleSize = _middleColumn.GetPreferredValues(middle);
            var valueSize = _valueColumn.GetPreferredValues(values);
            SetColumnWidth(_labelColumnElement, labelSize.x);
            SetColumnWidth(_middleColumnElement, middleSize.x);
            SetColumnWidth(_valueColumnElement, valueSize.x);

            var height =
                Mathf.CeilToInt(
                    Mathf.Max(labelSize.y, Mathf.Max(middleSize.y, valueSize.y))) +
                RowPadding;
            if (height != _rowHeight)
            {
                _rowHeight = height;
                _row.SetAndApplyAttribute("preferredHeight", height.ToString(CultureInfo.InvariantCulture));
            }

            if (!_rowVisible) _row.Show();
            _rowVisible = true;
        }

        private static void SetColumnWidth(IXmlElement column, float width)
        {
            var value = Mathf.CeilToInt(width).ToString(CultureInfo.InvariantCulture);
            if (column.GetAttribute("preferredWidth") != value)
                column.SetAndApplyAttribute("preferredWidth", value);
        }

        /// <summary>
        /// Appends a body's clock value and calendar details when available.
        /// </summary>
        /// <param name="columns">The builder of the displayed columns.</param>
        /// <param name="tooltip">The builder of the tooltip.</param>
        /// <param name="planet">The celestial body.</param>
        /// <param name="context">The selected clock mode and its time values.</param>
        private static void AppendBody(
            ClockColumns columns,
            StringBuilder tooltip,
            IPlanetNode planet,
            ClockContext context)
        {
            if (planet is null)
                return;

            var formatted = context.IsUniverseDate
                ? CelestialClock.TryFormatCalendarDate(
                    planet,
                    context.Time,
                    context.StartAtOne,
                    out var value,
                    out var calendar)
                : CelestialClock.TryFormatElapsedDays(
                    planet, context.Time, out value, out calendar);
            if (!formatted)
                return;

            AppendLine(
                columns,
                tooltip,
                CelestialClock.GetName(planet),
                context,
                value,
                calendar);
        }

        /// <summary>
        /// Appends the Gregorian Earth clock and, when materially different and enabled, the loaded
        /// planetary system's physical Earth clock. Its tooltip always describes those differences.
        /// </summary>
        private static void AppendEarth(
            ClockColumns columns,
            StringBuilder tooltip,
            IPlanetNode earth,
            ClockContext context,
            bool showInSystemClock)
        {
            var earthTime = context.IsUniverseDate
                ? CelestialClock.FormatEarthDate(context.Time, context.StartAtOne)
                : CelestialClock.FormatElapsedDays(
                    context.Time, CelestialClock.EarthSecondsPerDay);
            var earthCalendar = "  Calendar: Gregorian\n" +
                CelestialClock.FormatCalendar(
                    CelestialClock.EarthDaysPerYear,
                    CelestialClock.EarthHoursPerDay);
            AppendLine(
                columns,
                tooltip,
                CelestialClock.EarthName,
                context,
                earthTime,
                earthCalendar);

            if (earth is null ||
                !CelestialClock.TryGetEarthClockData(
                    earth, context.UniverseTime, out var earthData) ||
                !earthData.DiffersFromRealEarth)
                return;

            var inSystemCalendar = CelestialClock.FormatCalendar(
                earthData.DaysPerYear, earthData.SolarDayLength / 3600.0);
            if (earthData.EpochDiffersFromMidnight)
                inSystemCalendar += "\n  Epoch solar time: " + earthData.EpochSolarTime;
            AppendTooltipBlock(tooltip, "In-system Earth", inSystemCalendar);
            if (!showInSystemClock)
            {
                tooltip.Append("\n  Enable \"Show In-System Earth Clock\"\n")
                    .Append("  in Kelly Utils settings to show its clock.");
                return;
            }

            if (TryFormatInSystemEarthClock(
                    earth, context, earthData, out var inSystemTime))
                AppendDisplayLine(
                    columns, "In-system Earth", context, inSystemTime);
        }

        private static void AppendEridReference(
            ClockColumns columns,
            StringBuilder tooltip,
            ClockContext context)
        {
            var value = context.IsUniverseDate
                ? CelestialClock.FormatEridDate(context.Time, context.StartAtOne)
                : CelestialClock.FormatElapsedDays(
                    context.Time, CelestialClock.EridSecondsPerDay);
            var calendar = "  Calendar: fixed reference\n" +
                CelestialClock.FormatCalendar(
                    CelestialClock.EridDaysPerYear,
                    CelestialClock.EridHoursPerDay,
                    CelestialClock.EridEarthDaysPerYear);
            AppendLine(
                columns,
                tooltip,
                CelestialClock.EridReferenceName,
                context,
                value,
                calendar);
        }

        private static bool TryFormatInSystemEarthClock(
            IPlanetNode earth,
            ClockContext context,
            CelestialClock.EarthClockData earthData,
            out string value)
        {
            if (context.IsUniverseDate)
            {
                return CelestialClock.TryFormatCalendarDate(
                    earth,
                    context.Time,
                    context.StartAtOne,
                    out value,
                    out _);
            }

            value = earthData.DayLengthDiffers
                ? CelestialClock.FormatElapsedDays(
                    context.Time, earthData.SolarDayLength)
                : null;
            return value is not null;
        }

        /// <summary>
        /// Appends a line of the display, and the calendar it is on to the tooltip.
        /// </summary>
        /// <param name="columns">The builder of the displayed columns.</param>
        /// <param name="tooltip">The builder of the tooltip.</param>
        /// <param name="name">The name of the body the line is for.</param>
        /// <param name="context">The selected clock mode.</param>
        /// <param name="value">The formatted date or elapsed time.</param>
        /// <param name="calendar">The formatted lengths of the body's year and day.</param>
        private static void AppendLine(
            ClockColumns columns,
            StringBuilder tooltip,
            string name,
            ClockContext context,
            string value,
            string calendar)
        {
            AppendDisplayLine(columns, name, context, value);
            AppendTooltipBlock(tooltip, name, calendar);
        }

        private static void AppendDisplayLine(
            ClockColumns columns,
            string name,
            ClockContext context,
            string value)
        {
            // The middle column holds the date in universe mode and the T+/S+ marker otherwise.
            if (!context.IsUniverseDate)
            {
                columns.Append(name, context.Prefix, value);
                return;
            }

            var separator = value.LastIndexOf(' ');
            columns.Append(
                name,
                value.Substring(0, separator),
                value.Substring(separator + 1));
        }

        private static void AppendTooltipBlock(
            StringBuilder tooltip, string name, string calendar) =>
            tooltip.Append("\n\n").Append(name).Append(":\n").Append(calendar);
    }
}