using System;
using System.Globalization;
using Assets.Scripts;
using ModApi;
using ModApi.Flight.Sim;

namespace Flight
{
    /// <summary>
    /// Formats universe time as calendar dates, or elapsed time as day counts, using the solar day
    /// and year of a celestial body or Earth.
    /// </summary>
    /// <remarks>
    /// A solar day runs from one local noon to the next. Its length comes from the synodic rate,
    /// which combines body rotation with motion around the sun: <c>2 * pi / |synodic rate|</c>.
    /// Moons use their parent planet's heliocentric rate and orbital period.
    /// <para>
    /// Universe calendar days turn over at local midnight and can be numbered from 1 or 0. Elapsed
    /// clocks count complete solar days from <c>000:00:00:00</c>.
    /// </para>
    /// </remarks>
    internal static class CelestialClock
    {
        /// <summary>
        /// The name of the Earth calendar line. It is available even when the system has no Earth.
        /// </summary>
        public const string EarthName = "Earth";

        /// <summary>
        /// The mean length of an Earth year in Earth days, that is, the Gregorian calendar's.
        /// </summary>
        public const double EarthDaysPerYear = 365.2425;

        /// <summary>
        /// The length of an Earth day in hours.
        /// </summary>
        public const double EarthHoursPerDay = 24.0;

        private const double TwoPi = 2.0 * Math.PI;

        /// <summary>
        /// The smallest synodic angular rate (rad/s) that still yields a meaningful clock. Below
        /// this the body is effectively locked to the sun, and its day is longer than the solar
        /// system is old, so no time of day is reported.
        /// </summary>
        private const double MinSynodicRate = 1e-9;

        /// <summary>
        /// Gets the home planet, or <c>null</c> if it cannot be identified.
        /// </summary>
        /// <remarks>
        /// ModApi exposes no home-planet accessor, so the planet is looked up by name, falling back
        /// to the selected launch location's planet for planetary systems without a stock home.
        /// </remarks>
        /// <param name="reference">Any node in the solar system to search from.</param>
        /// <returns>The home planet node, or <c>null</c>.</returns>
        public static IPlanetNode GetHomePlanet(IPlanetNode reference)
        {
            var star = GetStar(reference);
            if (star is null)
                return null;

            var homePlanet = star.FindPlanet(Constants.HomePlanetName);
            if (homePlanet is not null)
                return homePlanet;

            var launchPlanetName = Game.Instance.GameState?.SelectedLaunchLocation?.PlanetName;
            return string.IsNullOrEmpty(launchPlanetName) ? null : star.FindPlanet(launchPlanetName);
        }

        /// <summary>
        /// Gets a body's display name.
        /// </summary>
        /// <param name="planet">The celestial body.</param>
        /// <returns>The name, or <c>null</c> if the body is <c>null</c>.</returns>
        public static string GetName(IPlanetNode planet) => planet?.PlanetData?.Name ?? planet?.Name;

        /// <summary>
        /// Gets whether a body is Earth, whose date is always shown on its own line.
        /// </summary>
        /// <param name="planet">The celestial body.</param>
        /// <returns><c>true</c> if the body is Earth; otherwise, <c>false</c>.</returns>
        public static bool IsEarth(IPlanetNode planet) =>
            string.Equals(GetName(planet), EarthName, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Formats universe time as an Earth date and time of day, <c>YYYY-MM-DD HH:MM:SS</c>.
        /// </summary>
        /// <param name="time">The universe time in seconds.</param>
        /// <param name="startAtOne">Whether year, month, and day numbering begins at 1.</param>
        /// <returns>The formatted date.</returns>
        public static string FormatEarthDate(double time, bool startAtOne)
        {
            var seconds = Math.Min(Math.Max(time, 0.0), (DateTime.MaxValue - DateTime.MinValue).TotalSeconds);
            var date = DateTime.MinValue.AddSeconds(seconds);
            if (startAtOne)
                return date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            // Universe time starts on the calendar's own first day, so taking one off each field
            // leaves the whole years, months, and days that have passed.
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:0000}-{1:00}-{2:00} {3:00}:{4:00}:{5:00}",
                date.Year - 1,
                date.Month - 1,
                date.Day - 1,
                date.Hour,
                date.Minute,
                date.Second);
        }

        /// <summary>
        /// Formats elapsed time as a count of whole days followed by the time within the current day.
        /// </summary>
        /// <param name="time">The elapsed time in seconds.</param>
        /// <param name="dayLength">The length of a day in seconds.</param>
        /// <returns>The formatted elapsed time, <c>DDD:HH:MM:SS</c>.</returns>
        public static string FormatElapsedDays(double time, double dayLength)
        {
            var elapsedTime = Math.Max(time, 0.0);
            var day = (long)(elapsedTime / dayLength);
            return FormatElapsedDayCount(day, elapsedTime - day * dayLength);
        }

        /// <summary>
        /// Tries to format universe time as a date and solar time of day on a body's own calendar,
        /// <c>YYYY-DD HH:MM:SS</c>. Body calendars omit months, so the day field is the day of the
        /// year. Hours use stock one-hour units and may run past 23.
        /// </summary>
        /// <param name="planet">The celestial body.</param>
        /// <param name="time">The universe time in seconds.</param>
        /// <param name="startAtOne">Whether year and day numbering begins at 1.</param>
        /// <param name="date">The formatted date.</param>
        /// <param name="calendar">The formatted length of the body's year and day.</param>
        /// <returns><c>true</c> if a date could be computed; otherwise, <c>false</c>.</returns>
        public static bool TryFormatCalendarDate(
            IPlanetNode planet,
            double time,
            bool startAtOne,
            out string date,
            out string calendar)
        {
            date = null;
            calendar = null;

            if (!TryGetCalendarProperties(planet, out var dayLength, out var daysPerYear, out calendar) ||
                !TryGetSubsolarLongitude(planet, out var subsolarLongitude))
                return false;

            var synodicRate = GetSynodicRate(planet);
            // The subsolar longitude advances at the synodic rate. The half-day offset is because
            // the prime meridian faces the sun (noon) at subsolar longitude 0.
            var timeOfDay =
                Wrap01(0.5 + Math.Sign(synodicRate) * subsolarLongitude / TwoPi) * dayLength;

            // Removing the current partial day leaves a whole number of days, give or take the
            // rounding of the geometry the time of day comes from.
            var day = Math.Max((long)Math.Round((time - timeOfDay) / dayLength), 0L);
            var year = 0L;

            // A year rarely holds a whole number of days, so a year starts on the first day that
            // begins after the orbit does, which leaves years one day longer than others now and then.
            if (HasYear(daysPerYear))
            {
                year = (long)(day / daysPerYear);
                day -= (long)Math.Ceiling(year * daysPerYear);
            }

            var origin = startAtOne ? 1L : 0L;
            date = string.Format(
                CultureInfo.InvariantCulture,
                "{0:0000}-{1:00} {2}",
                year + origin,
                day + origin,
                FormatTimeOfDay(timeOfDay));
            return true;
        }

        /// <summary>
        /// Tries to format elapsed time in a body's solar days as <c>DDD:HH:MM:SS</c>.
        /// </summary>
        /// <param name="planet">The celestial body.</param>
        /// <param name="time">The time elapsed in seconds.</param>
        /// <param name="elapsed">The formatted elapsed time.</param>
        /// <param name="calendar">The formatted length of the body's year and day.</param>
        /// <returns><c>true</c> if the body's solar day could be computed; otherwise, <c>false</c>.</returns>
        public static bool TryFormatElapsedDays(
            IPlanetNode planet, double time, out string elapsed, out string calendar)
        {
            elapsed = null;
            calendar = null;
            if (!TryGetCalendarProperties(planet, out var dayLength, out _, out calendar))
                return false;

            elapsed = FormatElapsedDays(time, dayLength);
            return true;
        }

        /// <summary>
        /// Formats the length of a body's year and day for the tooltip.
        /// </summary>
        /// <param name="daysPerYear">The number of days in the year, if it has one.</param>
        /// <param name="hoursPerDay">The number of stock hours in the day.</param>
        /// <param name="earthDaysPerYear">The number of Earth days in the year, if it should be shown.</param>
        /// <returns>The formatted lengths.</returns>
        public static string FormatCalendar(
            double daysPerYear, double hoursPerDay, double? earthDaysPerYear = null) =>
            HasYear(daysPerYear)
                ? FormatYearAndDay(daysPerYear, hoursPerDay, earthDaysPerYear)
                : string.Format(CultureInfo.InvariantCulture, "{0:N2} hours/day", hoursPerDay);

        private static string FormatYearAndDay(
            double daysPerYear, double hoursPerDay, double? earthDaysPerYear)
        {
            var calendar = string.Format(
                CultureInfo.InvariantCulture, "{0:N1} days/year, {1:N2} hours/day", daysPerYear, hoursPerDay);
            return earthDaysPerYear.HasValue
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}\n  {1:N1} Earth days/year",
                    calendar,
                    earthDaysPerYear.Value)
                : calendar;
        }

        private static string FormatElapsedDayCount(long day, double timeOfDay) =>
            string.Format(CultureInfo.InvariantCulture, "{0:000}:{1}", day, FormatTimeOfDay(timeOfDay));

        private static bool TryGetCalendarProperties(
            IPlanetNode planet, out double dayLength, out double daysPerYear, out string calendar)
        {
            dayLength = 0.0;
            daysPerYear = 0.0;
            calendar = null;

            var synodicRate = GetSynodicRate(planet);
            if (Math.Abs(synodicRate) < MinSynodicRate)
                return false;

            dayLength = TwoPi / Math.Abs(synodicRate);
            var yearLength = GetYearLength(planet);
            daysPerYear = yearLength / dayLength;
            calendar = FormatCalendar(daysPerYear, dayLength / 3600.0, yearLength / 86400.0);
            return true;
        }

        /// <summary>
        /// Gets whether a body's orbit yields a year that can be counted in its own days.
        /// </summary>
        /// <param name="daysPerYear">The number of days in the year.</param>
        /// <returns><c>true</c> if the body has a year; otherwise, <c>false</c>.</returns>
        private static bool HasYear(double daysPerYear) => daysPerYear >= 1.0 && !double.IsInfinity(daysPerYear);

        /// <summary>
        /// Gets the star at the root of the solar system the body belongs to.
        /// </summary>
        /// <param name="planet">The celestial body.</param>
        /// <returns>The star node, or <c>null</c> if the body is <c>null</c>.</returns>
        private static IPlanetNode GetStar(IPlanetNode planet)
        {
            var star = planet;
            while (star?.Parent is not null)
                star = star.Parent;

            return star;
        }

        /// <summary>
        /// Gets the rate (rad/s) at which the subsolar longitude drifts, that is, how fast the sun
        /// travels across the body's sky.
        /// </summary>
        /// <remarks>
        /// The game stores prograde rotation as a negative angular velocity, while prograde orbital
        /// motion advances longitude positively. Adding the rates gives zero for a body locked to
        /// the sun.
        /// </remarks>
        /// <param name="planet">The celestial body.</param>
        /// <returns>The synodic angular rate in rad/s, or zero if it cannot be determined.</returns>
        private static double GetSynodicRate(IPlanetNode planet)
        {
            var planetData = planet?.PlanetData;
            return planetData is null ? 0.0 : planetData.AngularVelocity + GetHeliocentricRate(planet);
        }

        /// <summary>
        /// Formats a time of day as <c>HH:MM:SS</c>. Long solar days can run past hour 23.
        /// </summary>
        /// <param name="timeOfDay">The time of day in seconds.</param>
        /// <returns>The formatted time of day.</returns>
        private static string FormatTimeOfDay(double timeOfDay)
        {
            var totalSeconds = (long)timeOfDay;
            var hours = totalSeconds / 3600L;
            var minutes = totalSeconds / 60L % 60L;
            var seconds = totalSeconds % 60L;
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", hours, minutes, seconds);
        }

        /// <summary>
        /// Gets the length (seconds) of the body's year, that is, of its orbit around the star.
        /// </summary>
        /// <param name="planet">The celestial body.</param>
        /// <returns>The year length in seconds, or zero if it cannot be determined.</returns>
        private static double GetYearLength(IPlanetNode planet)
        {
            var orbit = GetHeliocentricOrbit(planet);
            return orbit is null ? 0.0 : Math.Abs(orbit.Period);
        }

        /// <summary>
        /// Gets the angular rate (rad/s) at which the body's motion around the star carries the
        /// subsolar longitude. It is positive for a prograde orbit.
        /// </summary>
        /// <param name="planet">The celestial body.</param>
        /// <returns>The heliocentric angular rate in rad/s, or zero for the star itself.</returns>
        private static double GetHeliocentricRate(IPlanetNode planet)
        {
            var orbit = GetHeliocentricOrbit(planet);
            if (orbit is null)
                return 0.0;

            var rate = Math.Abs(orbit.MeanMotion);
            return orbit.IsPrograde ? rate : -rate;
        }

        /// <summary>
        /// Gets the orbit the body follows around the star.
        /// </summary>
        /// <remarks>
        /// For a moon this is its parent planet's orbit, because orbiting the planet leaves the
        /// moon's mean heliocentric longitude, and so its year, unchanged.
        /// </remarks>
        /// <param name="planet">The celestial body.</param>
        /// <returns>The orbit, or <c>null</c> for the star itself.</returns>
        private static IOrbit GetHeliocentricOrbit(IPlanetNode planet)
        {
            // Walk up to the outermost ancestor that still orbits the star directly.
            var node = planet;
            while (node?.Parent?.Parent is not null)
                node = node.Parent;

            var orbit = node?.Orbit;
            return node?.Parent is null || orbit is null || !orbit.IsValid ? null : orbit;
        }

        /// <summary>
        /// Tries to get the longitude (radians) of the point on the body directly beneath the sun.
        /// </summary>
        /// <param name="planet">The celestial body.</param>
        /// <param name="longitude">The subsolar longitude in radians.</param>
        /// <returns><c>true</c> if the longitude could be computed; otherwise, <c>false</c>.</returns>
        private static bool TryGetSubsolarLongitude(IPlanetNode planet, out double longitude)
        {
            longitude = 0.0;

            var star = GetStar(planet);
            if (star is null || ReferenceEquals(star, planet))
                return false;

            var toStar = star.SolarPosition - planet.SolarPosition;
            if (toStar.sqrMagnitude <= 0.0)
                return false;

            // The solar frame and the planet-centered inertial frame share their orientation, so
            // the direction to the star only has to be rotated into the body's rotating frame,
            // which is what folds the body's current rotation angle into the longitude.
            var surfaceDirection = planet.PlanetVectorToSurfaceVector(toStar).normalized;
            planet.GetSurfaceCoordinates(surfaceDirection, out _, out longitude);
            return true;
        }

        /// <summary>
        /// Wraps a fraction into the range [0, 1).
        /// </summary>
        /// <param name="value">The value to wrap.</param>
        /// <returns>The wrapped value.</returns>
        private static double Wrap01(double value)
        {
            var wrapped = value - Math.Floor(value);
            return wrapped is >= 1.0 or < 0.0 ? 0.0 : wrapped;
        }
    }
}
