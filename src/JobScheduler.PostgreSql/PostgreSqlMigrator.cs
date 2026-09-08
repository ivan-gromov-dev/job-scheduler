using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobScheduler.PostgreSql;

public sealed class PostgreSqlMigrator(NpgsqlDataSource dataSource)
{
    private const long AdvisoryLockId = 0x4A53434845444C52;

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var context = new JobSchedulerDbContext(dataSource);
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock({AdvisoryLockId})", cancellationToken);
            await context.Database.MigrateAsync(cancellationToken);
        }
        finally
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock({AdvisoryLockId})", cancellationToken);
        }
    }
}
