using System.Collections.Concurrent;

namespace JobScheduler.Core.Handlers;

internal sealed class JobHandlerRegistry
{
    private readonly ConcurrentDictionary<string, Registration> registrations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Type, Registration> registrationsByClrType = [];

    public void Add<TJob>(JobTypeRegistration type)
    {
        var registration = new Registration(
            type.Name,
            type.Version,
            type.Upcasters,
            static (dispatcher, payload, cancellationToken) =>
                ((JobDispatcher)dispatcher).DispatchTypedAsync<TJob>(payload, cancellationToken));
        if (!registrations.TryAdd(type.Name, registration) || !registrationsByClrType.TryAdd(typeof(TJob), registration))
            throw new InvalidOperationException($"Job type '{type.Name}' is already registered.");
        foreach (var alias in type.Aliases)
            if (!registrations.TryAdd(alias, registration)) throw new InvalidOperationException($"Job alias '{alias}' is already registered.");
    }

    public bool TryGet(string type, out Registration registration) =>
        registrations.TryGetValue(type, out registration!);

    public Registration Get<TJob>() => registrationsByClrType.TryGetValue(typeof(TJob), out var registration)
        ? registration
        : throw new InvalidOperationException($"No handler is registered for '{typeof(TJob).FullName}'.");

    internal sealed record Registration(string Name, int Version, IReadOnlyDictionary<int, Func<string, string>> Upcasters, Func<IJobDispatcher, string, CancellationToken, Task> Dispatch)
    {
        public string Upcast(string payload, int payloadVersion)
        {
            if (payloadVersion > Version) throw new InvalidOperationException($"Payload version {payloadVersion} is newer than version {Version} for '{Name}'.");
            for (var version = payloadVersion; version < Version; version++)
            {
                if (!Upcasters.TryGetValue(version, out var upcaster)) throw new InvalidOperationException($"No upcaster from version {version} is registered for '{Name}'.");
                payload = upcaster(payload);
            }
            return payload;
        }
    }
}
