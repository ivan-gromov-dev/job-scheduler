using System.Globalization;

namespace JobScheduler.Core.Jobs;

public static class JobCursor
{
    public static string Encode(Job job) => Convert.ToBase64String(
        System.Text.Encoding.UTF8.GetBytes($"{job.EnqueuedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}:{job.Id:N}"));

    public static (DateTimeOffset EnqueuedAt, Guid Id) Decode(string cursor)
    {
        try
        {
            var value = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = value.Split(':', 2);
            return (new DateTimeOffset(long.Parse(parts[0], CultureInfo.InvariantCulture), TimeSpan.Zero), Guid.ParseExact(parts[1], "N"));
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or OverflowException)
        {
            throw new ArgumentException("The cursor is invalid.", nameof(cursor), exception);
        }
    }
}
