using System;
using System.Globalization;
using Assets.Scripts;
using ModApi;
using ModApi.Flight.Sim;

namespace Flight
{
    /// <summary>
    /// Turns the flight's elapsed time into a date and a solar time of day on a celestial body's
    /// own calendar, or on Earth's.
    /// </summary>
    /// <remarks>
    /// A body's day is its <em>solar</em> day, not its sidereal rotation period: the sun only
    /// returns to the same meridian once the body has rotated a full turn <em>plus</em> the angle it
    /// swept around the sun in the meantime. The sub-solar longitude therefore drifts at a synodic
    /// rate that combines the body's rotation with its motion around the sun, and the day length is
    /// <c>2 * pi / |synodic rate|</c>. For a moon, the heliocentric rate is its parent planet's
    /// orbital rate, so a tidally locked moon still has a (long) solar day even though it always
    /// shows the same face to its planet. A body's year is likewise the orbital period around the
    /// star, which for a moon is again its parent planet's.
    /// <para>
    /// Dates count from year 1, day 1 at flight time zero. Days roll over at the body's local
    /// midnight rather than at whole multiples of the day length, so a flight that starts in the
    /// middle of a day spends the rest of that day on day 1.
    /// </para>
    /// </remarks>
    internal static class CelestialClock
    {
        /// <summary>
        /// The name of the body whose calendar the flight time is always also shown on. It is a
        /// calendar rather than a body, so it works in solar systems that have no such planet.
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
        /// this the body is effectively locked to the sun and its day is longer than the solar
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
        /// Formats a flight time as an Earth date and time of day, <c>YYYY-MM-DD HH:MM:SS</c>.
        /// </summary>
        /// <param name="time">The flight time in seconds.</param>
        /// <returns>The formatted date.</returns>
        public static string FormatEarthDate(double time)
        {
            var seconds = Math.Min(Math.Max(time, 0.0), (DateTime.MaxValue - DateTime.MinValue).TotalSeconds);
            return DateTime.MinValue.AddSeconds(seconds).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Tries to format a flight time as a date and solar time of day on a body's own calendar,
        /// <c>YYYY-DD HH:MM:SS</c>. The body has no months, so the day is its day of the year, and
        /// its hours are stock hours, of which a day that is not 24 hours long simply has more or
        /// fewer than 24.
        /// </summary>
        /// <param name="planet">The celestial body.</param>
        /// <param name="time">The flight time in seconds.</param>
        /// <param name="date">The formatted date.</param>
        /// <param name="calendar">The formatted length of the body's year and day.</param>
        /// <returns><c>true</c> if a date could be computed; otherwise, <c>false</c>.</returns>
        public static bool TryFormatDate(IPlanetNode planet, double time, out string date, out string calendar)
        {
            date = null;
            calendar = null;

            var synodicRate = GetSynodicRate(planet);
            if (Math.Abs(synodicRate) < MinSynodicRate || !TryGetSubSolarLongitude(planet, out var subSolarLongitude))
                return false;

            // The sub-solar longitude advances at the synodic rate, so the day advances along with
            // it, or against it where the rate is negative. The half-day offset is because the
            // prime meridian faces the sun (noon) at sub-solar longitude 0.
            var dayLength = TwoPi / Math.Abs(synodicRate);
            var timeOfDay = Wrap01(0.5 + Math.Sign(synodicRate) * subSolarLongitude / TwoPi) * dayLength;

            // What is left once the current day is taken off is a whole number of days, give or
            // take the rounding of the geometry the time of day comes from.
            var day = Math.Max((long)Math.Round((time - timeOfDay) / dayLength), 0L);
            var year = 1L;

            // A year rarely holds a whole number of days, so a year starts on the first day that
            // begins after the orbit does, which leaves years one day longer than others now and then.
            var daysPerYear = GetYearLength(planet) / dayLength;
            if (HasYear(daysPerYear))
            {
                year = (long)(day / daysPerYear) + 1L;
                day -= (long)Math.Ceiling((year - 1L) * daysPerYear);
            }

            date = string.Format(
                CultureInfo.InvariantCulture, "{0:0000}-{1:00} {2}", year, day + 1L, FormatTimeOfDay(timeOfDay));
            calendar = FormatCalendar(daysPerYear, dayLength / 3600.0);
            return true;
        }

        /// <summary>
        /// Formats the length of a body's year and day for the tooltip.
        /// </summary>
        /// <param name="daysPerYear">The number of days in the year, if it has one.</param>
        /// <param name="hoursPerDay">The number of stock hours in the day.</param>
        /// <returns>The formatted lengths.</returns>
        public static string FormatCalendar(double daysPerYear, double hoursPerDay) =>
            HasYear(daysPerYear)
                ? string.Format(
                    CultureInfo.InvariantCulture, "{0:N1} days/year, {1:N2} hours/day", daysPerYear, hoursPerDay)
                : string.Format(CultureInfo.InvariantCulture, "{0:N2} hours/day", hoursPerDay);

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
        /// Gets the rate (rad/s) at which the sub-solar longitude drifts, that is, how fast the sun
        /// travels across the body's sky.
        /// </summary>
        /// <remarks>
        /// The body's rotation and its motion around the sun both carry the sub-solar longitude, so
        /// the synodic rate is their sum. They combine rather than cancel because the game gives a
        /// prograde rotation a negative angular velocity while prograde orbital motion advances
        /// longitude positively. A body tidally locked to the sun consequently has a rate of zero.
        /// </remarks>
        /// <param name="planet">The celestial body.</param>
        /// <returns>The synodic angular rate in rad/s, or zero if it cannot be determined.</returns>
        private static double GetSynodicRate(IPlanetNode planet)
        {
            var planetData = planet?.PlanetData;
            return planetData is null ? 0.0 : planetData.AngularVelocity + GetHeliocentricRate(planet);
        }

        /// <summary>
        /// Formats a time of day as <c>HH:MM:SS</c>. A body whose solar day is longer than 24 stock
        /// hours simply keeps counting hours past 24.
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
        /// sub-solar longitude. It is positive for a prograde orbit.
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
        /// <param name="longitude">The sub-solar longitude in radians.</param>
        /// <returns><c>true</c> if the longitude could be computed; otherwise, <c>false</c>.</returns>
        private static bool TryGetSubSolarLongitude(IPlanetNode planet, out double longitude)
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
