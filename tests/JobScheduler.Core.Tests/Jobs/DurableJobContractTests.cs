using JobScheduler.Core.Handlers;
using JobScheduler.Core.Jobs;
using JobScheduler.Core.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace JobScheduler.Core.Tests.Jobs;

public sealed class DurableJobContractTests
{
    [Fact]
    public async Task ExplicitTypeNameVersionAliasAndUpcasterSurviveContractRename()
    {
        var services = new ServiceCollection();
        services.AddJobHandler<CurrentMessage, CapturingHandler>("billing.invoice-issued", 2,
            type => type.AddAlias("Legacy.InvoiceMessage")
                .AddUpcaster(1, payload => payload.Replace("oldValue", "value", StringComparison.Ordinal)));
        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IJobClient>();
        var stored = await client.EnqueueAsync(new CurrentMessage("new"));

        Assert.Equal("billing.invoice-issued", stored.Type);
        Assert.Equal(2, stored.PayloadVersion);

        var legacy = stored with { Type = "Legacy.InvoiceMessage", Payload = "{\"oldValue\":\"legacy\"}", PayloadVersion = 1 };
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IJobDispatcher>().DispatchAsync(legacy, CancellationToken.None);
        Assert.Equal("legacy", scope.ServiceProvider.GetRequiredService<IJobHandler<CurrentMessage>>() is CapturingHandler handler ? handler.Value : null);
    }

    [Fact]
    public async Task ConfiguredSerializerIsUsedForWriteAndDispatch()
    {
        var services = new ServiceCollection();
        services.AddJobHandler<CurrentMessage, CapturingHandler>("message");
        services.AddJobPayloadSerializer<PrefixSerializer>();
        await using var provider = services.BuildServiceProvider();
        var job = await provider.GetRequiredService<IJobClient>().EnqueueAsync(new CurrentMessage("custom"));
        Assert.StartsWith("prefix:", job.Payload, StringComparison.Ordinal);

        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IJobDispatcher>().DispatchAsync(job, CancellationToken.None);
        Assert.Equal("custom", ((CapturingHandler)scope.ServiceProvider.GetRequiredService<IJobHandler<CurrentMessage>>()).Value);
    }

    [Fact]
    public async Task AttemptHistoryRecordsWorkerRenewalFailureAndRetryTiming()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 9, 8, 10, 0, 0, TimeSpan.Zero));
        var store = new InMemoryJobStore(clock);
        var job = await store.EnqueueAsync("job", "{}");
        var lease = await store.ClaimAsync(TimeSpan.FromMinutes(1), [], "worker-a");
        clock.Advance(TimeSpan.FromSeconds(20));
        lease = await store.RenewLeaseAsync(lease!, TimeSpan.FromMinutes(1));
        clock.Advance(TimeSpan.FromSeconds(10));
        var retryAt = clock.GetUtcNow().AddMinutes(2);
        await store.RetryAsync(lease!, new JobFailure(JobFailureKind.Transient, "network"), retryAt);

        var attempt = Assert.Single((await store.GetAsync(job.Id))!.AttemptHistory);
        Assert.Equal("worker-a", attempt.WorkerId);
        Assert.Equal(TimeSpan.FromSeconds(30), attempt.Duration);
        Assert.Equal(JobStatus.Pending, attempt.Outcome);
        Assert.Equal(JobFailureKind.Transient, attempt.FailureKind);
        Assert.Equal(retryAt, attempt.RetryAt);
        Assert.NotNull(attempt.LastRenewedAt);
    }

    public sealed record CurrentMessage(string Value);

    public sealed class CapturingHandler : IJobHandler<CurrentMessage>
    {
        public string? Value { get; private set; }
        public Task HandleAsync(CurrentMessage job, CancellationToken cancellationToken = default)
        {
            Value = job.Value;
            return Task.CompletedTask;
        }
    }

    public sealed class PrefixSerializer : IJobPayloadSerializer
    {
        public string Serialize<TJob>(TJob job) => "prefix:" + ((CurrentMessage)(object)job!).Value;
        public TJob Deserialize<TJob>(string payload) => (TJob)(object)new CurrentMessage(payload[7..]);
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now = now.Add(duration);
    }
}
