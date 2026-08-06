using System;
using Assets.Scripts;
using ModApi.Flight.Sim;

namespace Flight
{
    /// <summary>
    /// Computes the solar time of day at a celestial body's prime meridian.
    /// </summary>
    /// <remarks>
    /// A body's day is its <em>solar</em> day, not its sidereal rotation period: the sun only
    /// returns to the same meridian once the body has rotated a full turn <em>plus</em> the angle it
    /// swept around the sun in the meantime. The synodic rate is therefore the difference of the
    /// two angular rates, and the day length is <c>2 * pi / |rotation rate - heliocentric rate|</c>.
    /// For a moon, the heliocentric rate is its parent planet's orbital rate, so a tidally locked
    /// moon still has a (long) solar day even though it always shows the same face to its planet.
    /// </remarks>
    internal static class CelestialClock
    {
        /// <summary>
        /// The name of the stock home planet. ModApi exposes no home-planet accessor, so the
        /// planet is looked up by name, falling back to the selected launch location's planet.
        /// </summary>
        private const string StockHomePlanetName = "Droo";

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
        /// <param name="reference">Any node in the solar system to search from.</param>
        /// <returns>The home planet node, or <c>null</c>.</returns>
        public static IPlanetNode GetHomePlanet(IPlanetNode reference)
        {
            var star = GetStar(reference);
            if (star is null)
                return null;

            var homePlanet = star.FindPlanet(StockHomePlanetName);
            if (homePlanet is not null)
                return homePlanet;

            var launchPlanetName = Game.Instance.GameState?.SelectedLaunchLocation?.PlanetName;
            return string.IsNullOrEmpty(launchPlanetName) ? null : star.FindPlanet(launchPlanetName);
        }

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
        /// Gets the length of the body's solar day in seconds, or zero if it cannot be determined.
        /// </summary>
        /// <param name="planet">The celestial body.</param>
        /// <returns>The solar day length in seconds, or zero.</returns>
        public static double GetSolarDayLength(IPlanetNode planet)
        {
            var planetData = planet?.PlanetData;
            if (planetData is null)
                return 0.0;

            var synodicRate = Math.Abs(planetData.AngularVelocity - GetHeliocentricRate(planet));
            return synodicRate < MinSynodicRate ? 0.0 : TwoPi / synodicRate;
        }

        /// <summary>
        /// Tries to get the solar time of day at the body's prime meridian, that is, the time
        /// elapsed since the sun was last opposite the prime meridian (local midnight).
        /// </summary>
        /// <param name="planet">The celestial body.</param>
        /// <param name="timeOfDay">The time of day in seconds, in the range [0, day length).</param>
        /// <returns><c>true</c> if a time of day could be computed; otherwise, <c>false</c>.</returns>
        public static bool TryGetTimeOfDay(IPlanetNode planet, out double timeOfDay)
        {
            timeOfDay = 0.0;

            var dayLength = GetSolarDayLength(planet);
            if (dayLength <= 0.0 || !TryGetSubSolarLongitude(planet, out var subSolarLongitude))
                return false;

            // Longitude increases eastward, so on a prograde body the sub-solar point drifts
            // westward and the day advances as the sub-solar longitude decreases. The half-day
            // offset is because the prime meridian faces the sun (noon) at sub-solar longitude 0.
            var direction = planet.PlanetData.AngularVelocity < 0.0 ? -1.0 : 1.0;
            var fraction = Wrap01(0.5 - direction * subSolarLongitude / TwoPi);

            timeOfDay = fraction * dayLength;
            return true;
        }

        /// <summary>
        /// Formats a time of day as <c>HH:MM:SS</c>. A body whose solar day is longer than 24 stock
        /// hours simply keeps counting hours past 24.
        /// </summary>
        /// <param name="timeOfDay">The time of day in seconds.</param>
        /// <returns>The formatted time of day.</returns>
        public static string FormatTimeOfDay(double timeOfDay)
        {
            var totalSeconds = (long)timeOfDay;
            var hours = totalSeconds / 3600L;
            var minutes = totalSeconds / 60L % 60L;
            var seconds = totalSeconds % 60L;
            return $"{hours:00}:{minutes:00}:{seconds:00}";
        }

        /// <summary>
        /// Gets the angular rate (rad/s) at which the body travels around the star it orbits.
        /// </summary>
        /// <remarks>
        /// For a moon this is its parent planet's orbital rate, because orbiting the planet leaves
        /// the moon's mean heliocentric longitude unchanged.
        /// </remarks>
        /// <param name="planet">The celestial body.</param>
        /// <returns>The heliocentric angular rate in rad/s, or zero for the star itself.</returns>
        private static double GetHeliocentricRate(IPlanetNode planet)
        {
            // Walk up to the outermost ancestor that still orbits the star directly.
            var node = planet;
            while (node?.Parent?.Parent is not null)
                node = node.Parent;

            var orbit = node?.Orbit;
            if (node?.Parent is null || orbit is null || !orbit.IsValid)
                return 0.0;

            var rate = Math.Abs(orbit.MeanMotion);
            return orbit.IsPrograde ? rate : -rate;
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
