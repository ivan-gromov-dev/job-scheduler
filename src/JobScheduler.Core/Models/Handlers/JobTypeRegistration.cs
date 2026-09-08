namespace JobScheduler.Core.Handlers;

public sealed class JobTypeRegistration
{
    private readonly List<string> aliases = [];
    private readonly Dictionary<int, Func<string, string>> upcasters = [];

    public JobTypeRegistration(string name, int version = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        Name = name;
        Version = version;
    }

    public string Name { get; }
    public int Version { get; }
    internal IReadOnlyList<string> Aliases => aliases;
    internal IReadOnlyDictionary<int, Func<string, string>> Upcasters => upcasters;

    public JobTypeRegistration AddAlias(string alias)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        aliases.Add(alias);
        return this;
    }

    public JobTypeRegistration AddUpcaster(int fromVersion, Func<string, string> upcaster)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fromVersion, 1);
        ArgumentNullException.ThrowIfNull(upcaster);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(fromVersion, Version);
        upcasters.Add(fromVersion, upcaster);
        return this;
    }
}
