using JobScheduler.Core.Handlers;
using JobScheduler.Core.Jobs;
using JobScheduler.Core.Scheduling;
using JobScheduler.Core.Serialization;
using JobScheduler.Worker;
using Microsoft.Extensions.DependencyInjection;

namespace JobScheduler.ReleaseTests.Compatibility;

public sealed class PublicApiCompatibilityTests
{
    [Fact]
    public void StableClientContractsRetainTheirOnePointZeroSignatures()
    {
        AssertMethod<IJobClient>(nameof(IJobClient.EnqueueAsync), 3);
        AssertMethod<IJobAdministration>(nameof(IJobAdministration.ListPageAsync), 2);
        AssertMethod<IScheduleClient>(nameof(IScheduleClient.ScheduleAsync), 3);
        AssertMethod<IScheduleStore>(nameof(IScheduleStore.MaterializeDueAsync), 3);
        AssertMethod<IJobPayloadSerializer>(nameof(IJobPayloadSerializer.Serialize), 1);
        AssertMethod<IJobWorkerControl>(nameof(IJobWorkerControl.BeginDrain), 0);
    }

    [Fact]
    public void StableDependencyInjectionEntryPointsRemainAvailable()
    {
        IServiceCollection services = new ServiceCollection();
        services.AddJobScheduler();
        services.AddJobHandler<CompatibilityJob, CompatibilityHandler>("release.compatibility", 1);
        services.AddJobWorker();
        services.AddScheduleMaterializer();
        services.AddPostgreSqlJobStore(options => options.ConnectionString = "Host=localhost;Database=unused;Username=unused;Password=unused");

        Assert.NotEmpty(services);
    }

    private static void AssertMethod<T>(string name, int parameterCount) =>
        Assert.Contains(
            typeof(T).GetMethods(),
            method => method.Name == name && method.GetParameters().Length == parameterCount);

    private sealed record CompatibilityJob(string Value);

    private sealed class CompatibilityHandler : IJobHandler<CompatibilityJob>
    {
        public Task HandleAsync(CompatibilityJob job, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
