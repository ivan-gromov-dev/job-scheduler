namespace JobScheduler.Core.Jobs;

internal sealed class JobAdministration(IJobStore store) : IJobAdministration
{
    public ValueTask<Job?> GetAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        store.GetAsync(jobId, cancellationToken);

    public ValueTask<IReadOnlyList<Job>> ListAsync(JobQuery query, CancellationToken cancellationToken = default) =>
        store.ListAsync(query, cancellationToken);

    public async ValueTask<CursorPage<Job>> ListPageAsync(JobQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Limit, 1);
        var fetched = await store.ListAsync(query with { Limit = checked(query.Limit + 1) }, cancellationToken);
        var items = fetched.Take(query.Limit).ToArray();
        var cursor = fetched.Count > query.Limit ? JobCursor.Encode(items[^1]) : null;
        return new CursorPage<Job>(items, cursor);
    }

    public ValueTask<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        store.CancelAsync(jobId, cancellationToken);

    public ValueTask<bool> ReplayAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        store.ReplayDeadLetterAsync(jobId, cancellationToken);

    public async ValueTask<int> CancelAsync(IReadOnlyCollection<Guid> jobIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobIds);
        var changed = 0;
        foreach (var id in jobIds) if (await store.CancelAsync(id, cancellationToken)) changed++;
        return changed;
    }

    public async ValueTask<int> ReplayAsync(IReadOnlyCollection<Guid> jobIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobIds);
        var changed = 0;
        foreach (var id in jobIds) if (await store.ReplayDeadLetterAsync(id, cancellationToken)) changed++;
        return changed;
    }
}
