using System.Collections.Concurrent;

namespace JobScheduler.Core.Handlers;

internal sealed class JobHandlerRegistry
{
    private readonly ConcurrentDictionary<string, Registration> registrations = new(StringComparer.Ordinal);

    public void Add<TJob>() =>
        registrations[JobTypeName.For<TJob>()] = new Registration(
            static (dispatcher, payload, cancellationToken) =>
                ((JobDispatcher)dispatcher).DispatchTypedAsync<TJob>(payload, cancellationToken));

    public bool TryGet(string type, out Registration registration) =>
        registrations.TryGetValue(type, out registration!);

    internal sealed record Registration(Func<IJobDispatcher, string, CancellationToken, Task> Dispatch);
}
