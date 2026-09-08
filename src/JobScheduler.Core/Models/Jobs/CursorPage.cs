namespace JobScheduler.Core.Jobs;

public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);
