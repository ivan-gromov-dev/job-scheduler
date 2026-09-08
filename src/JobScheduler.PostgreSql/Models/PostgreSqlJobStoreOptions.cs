namespace JobScheduler.PostgreSql;

public sealed class PostgreSqlJobStoreOptions
{
    public required string ConnectionString { get; set; }

    public bool AutoMigrate { get; set; } = true;

    public bool ValidateSchemaOnStartup { get; set; }
}
