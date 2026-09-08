namespace JobScheduler.Core.Handlers;

internal static class JobTypeName
{
    public static string For<TJob>() =>
        typeof(TJob).FullName ?? throw new InvalidOperationException("Job types must have a full name.");
}
