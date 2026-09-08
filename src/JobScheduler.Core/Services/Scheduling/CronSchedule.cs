using System.Globalization;

namespace JobScheduler.Core.Scheduling;

public sealed class CronSchedule
{
    private readonly Field minute;
    private readonly Field hour;
    private readonly Field day;
    private readonly Field month;
    private readonly Field dayOfWeek;

    private CronSchedule(Field minute, Field hour, Field day, Field month, Field dayOfWeek) =>
        (this.minute, this.hour, this.day, this.month, this.dayOfWeek) = (minute, hour, day, month, dayOfWeek);

    public static CronSchedule Parse(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        var fields = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5) throw new FormatException("Cron expressions must contain five fields: minute hour day-of-month month day-of-week.");
        return new(Field.Parse(fields[0], 0, 59), Field.Parse(fields[1], 0, 23), Field.Parse(fields[2], 1, 31), Field.Parse(fields[3], 1, 12), Field.Parse(fields[4], 0, 6, normalizeSeven: true));
    }

    public DateTimeOffset GetNextOccurrence(DateTimeOffset after, TimeZoneInfo timeZone)
    {
        var candidate = new DateTimeOffset(after.UtcDateTime.AddTicks(-(after.UtcDateTime.Ticks % TimeSpan.TicksPerMinute)), TimeSpan.Zero).AddMinutes(1);
        var limit = candidate.AddYears(5);
        while (candidate < limit)
        {
            var local = TimeZoneInfo.ConvertTime(candidate, timeZone);
            var domMatch = day.Contains(local.Day);
            var dowMatch = dayOfWeek.Contains((int)local.DayOfWeek);
            var dateMatch = day.IsWildcard && dayOfWeek.IsWildcard ? true : day.IsWildcard ? dowMatch : dayOfWeek.IsWildcard ? domMatch : domMatch || dowMatch;
            if (minute.Contains(local.Minute) && hour.Contains(local.Hour) && month.Contains(local.Month) && dateMatch) return candidate;
            candidate = candidate.AddMinutes(1);
        }
        throw new InvalidOperationException("Cron expression has no occurrence within five years.");
    }

    private sealed class Field(HashSet<int> values, bool wildcard)
    {
        public bool IsWildcard { get; } = wildcard;
        public bool Contains(int value) => values.Contains(value);

        public static Field Parse(string text, int min, int max, bool normalizeSeven = false)
        {
            var values = new HashSet<int>();
            foreach (var item in text.Split(','))
            {
                var stepParts = item.Split('/');
                if (stepParts.Length > 2 || !int.TryParse(stepParts.ElementAtOrDefault(1) ?? "1", NumberStyles.None, CultureInfo.InvariantCulture, out var step) || step <= 0) throw new FormatException($"Invalid cron field '{text}'.");
                var range = stepParts[0];
                int start, end;
                if (range == "*") (start, end) = (min, max);
                else if (range.Contains('-'))
                {
                    var bounds = range.Split('-');
                    if (bounds.Length != 2 || !int.TryParse(bounds[0], out start) || !int.TryParse(bounds[1], out end)) throw new FormatException($"Invalid cron field '{text}'.");
                }
                else if (int.TryParse(range, out start)) end = start;
                else throw new FormatException($"Invalid cron field '{text}'.");
                if (normalizeSeven && start == 7) start = 0;
                if (normalizeSeven && end == 7) end = 0;
                if (start < min || start > max || end < min || end > max || start > end) throw new FormatException($"Cron field '{text}' is outside {min}..{max}.");
                for (var value = start; value <= end; value += step) values.Add(value);
            }
            return new(values, text == "*");
        }
    }
}
