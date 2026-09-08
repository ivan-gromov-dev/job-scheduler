namespace JobScheduler.Core.Jobs;

public interface IJobAdministration
{
    ValueTask<Job?> GetAsync(Guid jobId, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<Job>> ListAsync(JobQuery query, CancellationToken cancellationToken = default);

    ValueTask<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken = default);

    ValueTask<bool> ReplayAsync(Guid jobId, CancellationToken cancellationToken = default);
}
