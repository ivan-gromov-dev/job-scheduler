namespace JobScheduler.Core.Jobs;

internal sealed class JobAdministration(IJobStore store) : IJobAdministration
{
    public ValueTask<Job?> GetAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        store.GetAsync(jobId, cancellationToken);

    public ValueTask<IReadOnlyList<Job>> ListAsync(JobQuery query, CancellationToken cancellationToken = default) =>
        store.ListAsync(query, cancellationToken);

    public ValueTask<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        store.CancelAsync(jobId, cancellationToken);

    public ValueTask<bool> ReplayAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        store.ReplayDeadLetterAsync(jobId, cancellationToken);
}
