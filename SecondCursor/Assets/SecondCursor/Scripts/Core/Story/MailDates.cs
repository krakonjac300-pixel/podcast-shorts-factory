using System;
using System.Globalization;

namespace SecondCursor.Core.Story
{
    /// <summary>
    /// Received dates of mail. Mail is authored with the time it "was sent", but mail that arrives during the shift is
    /// received at the shift clock: a message dated 2:31 AM that lands at 2:15 AM would read as a bug (Phase I). Mail from
    /// earlier days (the briefings, the 1987 message) keeps the date it was written with.
    /// </summary>
    public static class MailDates
    {
        /// <summary>The format of every authored mail date, e.g. "Wed 11/18/98 1:40 AM".</summary>
        public const string Format = "ddd MM/dd/yy h:mm tt";

        /// <summary>The calendar day of a night: Night 1 is Wed 11/18/98, Night 2 Thu 11/19/98, Night 3 Fri 11/20/98.</summary>
        public static DateTime NightDate(int night) => new DateTime(1998, 11, 17 + Math.Max(1, night));

        public static bool TryParse(string date, out DateTime parsed) =>
            DateTime.TryParseExact(date ?? "", Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed);

        /// <summary>
        /// The date a mail shows when it arrives at <paramref name="clockMinutes"/> (minutes since midnight) on
        /// <paramref name="night"/>: the authored one, unless it is tonight and later than the clock, in which case the same day
        /// at the clock's time. It is never later than the clock.
        /// </summary>
        public static string Received(string authored, int night, int clockMinutes)
        {
            if (!TryParse(authored, out var d) || d.Date != NightDate(night).Date) return authored;
            int minutes = d.Hour * 60 + d.Minute;
            if (minutes <= clockMinutes) return authored;
            return d.Date.AddMinutes(clockMinutes).ToString(Format, CultureInfo.InvariantCulture);
        }
    }
}
