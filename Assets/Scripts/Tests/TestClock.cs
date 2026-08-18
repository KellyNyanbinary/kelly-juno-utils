using System;
using Flight;
using NUnit.Framework;

namespace Tests
{
    public class TestClock
    {
        [TestCase(0.0, false, "0000-00-00 00:00:00")]
        [TestCase(0.0, true, "0001-01-01 00:00:00")]
        [TestCase(-1.0, true, "0001-01-01 00:00:00")]
        [TestCase(90061.0, true, "0001-01-02 01:01:01")]
        public void FormatEarthDate_UsesSelectedDateOrigin(
            double seconds, bool startAtOne, string expected) =>
            Assert.That(
                CelestialClock.FormatEarthDate(seconds, startAtOne),
                Is.EqualTo(expected));

        [TestCase(0.0, 86400.0, "000:00:00:00")]
        [TestCase(-1.0, 86400.0, "000:00:00:00")]
        [TestCase(90061.0, 86400.0, "001:01:01:01")]
        [TestCase(54001.0, 50400.0, "001:01:00:01")]
        [TestCase(3600.0, 360000.0, "000:01:00:00")]
        [TestCase(3600.0, 361800.0, "000:001:00:00")]
        [TestCase(360000.0, 361800.0, "000:100:00:00")]
        public void FormatElapsedDays_UsesSpecifiedDayLength(
            double seconds, double dayLength, string expected) =>
            Assert.That(
                CelestialClock.FormatElapsedDays(seconds, dayLength),
                Is.EqualTo(expected));

        [Test]
        public void FormatCalendar_ShowsYearAndDayToTwoDecimalPlaces() =>
            Assert.That(
                CelestialClock.FormatCalendar(365.2425, 24.0),
                Is.EqualTo("  Year: 365.24 days\n  Day: 24.00 hours"));

        [Test]
        public void FormatCalendar_ShowsEquivalentTwentyFourHourEarthDays() =>
            Assert.That(
                CelestialClock.FormatCalendar(99.071, 14.0, 57.7917),
                Is.EqualTo(
                    "  Year: 99.07 days (57.79 24-hour Earth days)\n" +
                    "  Day: 14.00 hours"));

        [Test]
        public void FormatCalendar_OmitsYearWhenOrbitIsShorterThanOneDay() =>
            Assert.That(
                CelestialClock.FormatCalendar(0.5, 12.0),
                Is.EqualTo("  Day: 12.00 hours"));

        [TestCase(0.0)]
        [TestCase(6.0)]
        [TestCase(12.0)]
        [TestCase(18.0)]
        public void CountElapsedSolarDays_IncrementsAtFirstMidnight(
            double epochHour)
        {
            const double dayLength = 86400.0;
            var epochTimeOfDay = epochHour * 3600.0;
            var firstMidnight = epochTimeOfDay == 0.0
                ? dayLength
                : dayLength - epochTimeOfDay;

            Assert.That(
                CountDaysAt(0.0, epochTimeOfDay, dayLength),
                Is.EqualTo(0L));
            Assert.That(
                CountDaysAt(firstMidnight - 1.0, epochTimeOfDay, dayLength),
                Is.EqualTo(0L));
            Assert.That(
                CountDaysAt(firstMidnight, epochTimeOfDay, dayLength),
                Is.EqualTo(1L));
            var eleventhMidnight = firstMidnight + 10.0 * dayLength;
            Assert.That(
                CountDaysAt(eleventhMidnight, epochTimeOfDay, dayLength),
                Is.EqualTo(11L));
        }

        [TestCase(0L, 99.071, 0L, 0L)]
        [TestCase(99L, 99.071, 0L, 99L)]
        [TestCase(100L, 99.071, 1L, 0L)]
        [TestCase(198L, 99.071, 1L, 98L)]
        [TestCase(199L, 99.071, 2L, 0L)]
        [TestCase(365L, 365.0, 1L, 0L)]
        public void SplitCalendarDate_StartsYearOnNextWholeDay(
            long elapsedDays,
            double daysPerYear,
            long expectedYear,
            long expectedDay)
        {
            CelestialClock.SplitCalendarDate(
                elapsedDays, daysPerYear, out var year, out var day);

            Assert.That(year, Is.EqualTo(expectedYear));
            Assert.That(day, Is.EqualTo(expectedDay));
        }

        [Test]
        public void SplitCalendarDate_KeepsDayCountWithoutUsableYear()
        {
            CelestialClock.SplitCalendarDate(
                42L, double.PositiveInfinity, out var year, out var day);

            Assert.That(year, Is.EqualTo(0L));
            Assert.That(day, Is.EqualTo(42L));
        }

        [TestCase(0L, 99.0, "00")]
        [TestCase(0L, 99.071, "000")]
        [TestCase(98L, 99.071, "098")]
        [TestCase(365L, 365.2425, "365")]
        [TestCase(0L, 999.183, "0000")]
        [TestCase(998L, 999.183, "0998")]
        [TestCase(1523L, 2183.264, "1523")]
        public void FormatDayOfYear_FitsLongestPossibleYear(
            long day, double daysPerYear, string expected) =>
            Assert.That(
                CelestialClock.FormatDayOfYear(day, daysPerYear),
                Is.EqualTo(expected));

        [TestCase(10000.99, false)]
        [TestCase(10001.0, false)]
        [TestCase(10001.01, true)]
        [TestCase(9998.99, true)]
        public void DiffersByEarthTolerance_UsesStrictPointZeroOnePercentMargin(
            double actual, bool expected) =>
            Assert.That(
                CelestialClock.DiffersByEarthTolerance(actual, 10000.0),
                Is.EqualTo(expected));

        [TestCase(0.0, false)]
        [TestCase(1.0, false)]
        [TestCase(1.001, true)]
        [TestCase(86399.000, false)]
        [TestCase(86389.999, true)]
        [TestCase(-1.0, false)]
        [TestCase(-1.001, true)]
        [TestCase(86401.0, false)]
        [TestCase(86401.001, true)]
        public void EarthEpochDiffersFromMidnight_UsesCircularOneSecondMargin(
            double epochTimeOfDay, bool expected) =>
            Assert.That(
                CelestialClock.EarthEpochDiffersFromMidnight(
                    epochTimeOfDay, CelestialClock.EarthSecondsPerDay),
                Is.EqualTo(expected));

        [TestCase(false, false, false, false)]
        [TestCase(true, false, false, true)]
        [TestCase(false, true, false, true)]
        [TestCase(false, false, true, true)]
        public void EarthClockData_CombinesMaterialDifferences(
            bool dayDiffers, bool yearDiffers, bool epochDiffers, bool expected)
        {
            var data = new CelestialClock.EarthClockData(
                CelestialClock.EarthSecondsPerDay,
                CelestialClock.EarthDaysPerYear,
                dayDiffers,
                yearDiffers,
                epochDiffers,
                "00:00:00");

            Assert.That(data.DiffersFromRealEarth, Is.EqualTo(expected));
        }

        [TestCase(0.0)]
        [TestCase(1e-10)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void TryGetDayLength_RejectsInvalidSynodicRate(double rate) =>
            Assert.That(
                CelestialClock.TryGetDayLength(rate, out _),
                Is.False);

        [TestCase(1.0)]
        [TestCase(-1.0)]
        public void TryGetDayLength_ComputesSolarDayInEitherDirection(
            double direction)
        {
            var rate =
                direction * 2.0 * Math.PI / CelestialClock.EarthSecondsPerDay;

            var result = CelestialClock.TryGetDayLength(rate, out var dayLength);

            Assert.That(result, Is.True);
            Assert.That(
                dayLength,
                Is.EqualTo(CelestialClock.EarthSecondsPerDay).Within(1e-9));
        }

        private static long CountDaysAt(
            double time, double epochTimeOfDay, double dayLength)
        {
            var timeOfDay = (time + epochTimeOfDay) % dayLength;
            if (timeOfDay < 0.0)
                timeOfDay += dayLength;
            return CelestialClock.CountElapsedSolarDays(
                time, timeOfDay, dayLength);
        }
    }
}