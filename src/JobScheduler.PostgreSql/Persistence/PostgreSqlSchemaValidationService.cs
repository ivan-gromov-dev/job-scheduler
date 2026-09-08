using Microsoft.Extensions.Hosting;

namespace JobScheduler.PostgreSql;

internal sealed class PostgreSqlSchemaValidationService(PostgreSqlMigrator migrator) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => migrator.ValidateAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
