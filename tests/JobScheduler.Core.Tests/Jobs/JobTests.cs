using JobScheduler.Core.Jobs;

namespace JobScheduler.Core.Tests.Jobs;

public sealed class JobTests
{
    [Fact]
    public void CreateInitializesPendingJobForImmediateExecution()
    {
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

        var job = Job.Create("send-email", "{}", now);

        Assert.NotEqual(Guid.Empty, job.Id);
        Assert.Equal("send-email", job.Type);
        Assert.Equal(now, job.EnqueuedAt);
        Assert.Equal(now, job.ScheduledAt);
        Assert.Equal(JobStatus.Pending, job.Status);
        Assert.Equal(0, job.Attempt);
    }

    [Fact]
    public void CreateRejectsEmptyJobType()
    {
        var action = () => Job.Create(" ", "{}", DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void CreateNormalizesTimestampsToUtc()
    {
        var local = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.FromHours(2));

        var job = Job.Create("test", "{}", local, local.AddHours(1));

        Assert.Equal(TimeSpan.Zero, job.EnqueuedAt.Offset);
        Assert.Equal(TimeSpan.Zero, job.ScheduledAt.Offset);
        Assert.Equal(local.UtcDateTime, job.EnqueuedAt.UtcDateTime);
    }
}
