using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobScheduler.PostgreSql;

public sealed class JobSchedulerDbContext(NpgsqlDataSource dataSource) : DbContext
{
    internal DbSet<JobEntity> Jobs => Set<JobEntity>();
    internal DbSet<ScheduleEntity> Schedules => Set<ScheduleEntity>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.UseNpgsql(dataSource);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var job = modelBuilder.Entity<JobEntity>();
        job.ToTable("job_scheduler_jobs", table =>
        {
            table.HasCheckConstraint("ck_job_scheduler_status", "status BETWEEN 0 AND 5");
            table.HasCheckConstraint("ck_job_scheduler_attempt", "attempt >= 0");
            table.HasCheckConstraint("ck_job_scheduler_lease", "(status = 1 AND lease_token IS NOT NULL AND lease_expires_at IS NOT NULL) OR (status <> 1 AND lease_token IS NULL AND lease_expires_at IS NULL)");
        });
        job.HasKey(x => x.Id).HasName("pk_job_scheduler_jobs");
        job.Property(x => x.Id).HasColumnName("id"); job.Property(x => x.Type).HasColumnName("type");
        job.Property(x => x.Payload).HasColumnName("payload"); job.Property(x => x.EnqueuedAt).HasColumnName("enqueued_at");
        job.Property(x => x.PayloadVersion).HasColumnName("payload_version");
        job.Property(x => x.AttemptHistory).HasColumnName("attempt_history").HasColumnType("jsonb");
        job.Property(x => x.ScheduledAt).HasColumnName("scheduled_at"); job.Property(x => x.Status).HasColumnName("status").HasConversion<short>();
        job.Property(x => x.Attempt).HasColumnName("attempt"); job.Property(x => x.Failure).HasColumnName("failure");
        job.Property(x => x.FailureKind).HasColumnName("failure_kind").HasConversion<short?>();
        job.Property(x => x.DeduplicationKey).HasColumnName("deduplication_key"); job.Property(x => x.CompletedAt).HasColumnName("completed_at");
        job.Property(x => x.Queue).HasColumnName("queue"); job.Property(x => x.Priority).HasColumnName("priority");
        job.Property(x => x.CorrelationId).HasColumnName("correlation_id");
        job.Property(x => x.LeaseToken).HasColumnName("lease_token"); job.Property(x => x.LeaseExpiresAt).HasColumnName("lease_expires_at");
        job.Property(x => x.RowVersion).HasColumnName("row_version").IsConcurrencyToken();
        job.HasIndex(x => new { x.Queue, x.Priority, x.ScheduledAt, x.EnqueuedAt, x.Id }, "ix_job_scheduler_jobs_poll").IsDescending(false, true, false, false, false).HasFilter("status = 0");
        job.HasIndex(x => new { x.LeaseExpiresAt, x.Id }, "ix_job_scheduler_jobs_expired_leases").HasFilter("status = 1");
        job.HasIndex(x => new { x.CompletedAt, x.Id }, "ix_job_scheduler_jobs_dead_letters").HasFilter("status = 5");
        job.HasIndex(x => new { x.Type, x.DeduplicationKey }, "ux_job_scheduler_jobs_deduplication").IsUnique()
            .HasFilter("deduplication_key IS NOT NULL AND status IN (0, 1, 2)");

        var schedule = modelBuilder.Entity<ScheduleEntity>();
        schedule.ToTable("job_scheduler_schedules");
        schedule.HasKey(x => x.Id).HasName("pk_job_scheduler_schedules");
        schedule.Property(x => x.Id).HasColumnName("id"); schedule.Property(x => x.JobType).HasColumnName("job_type");
        schedule.Property(x => x.Payload).HasColumnName("payload"); schedule.Property(x => x.CronExpression).HasColumnName("cron_expression");
        schedule.Property(x => x.PayloadVersion).HasColumnName("payload_version");
        schedule.Property(x => x.TimeZoneId).HasColumnName("time_zone_id"); schedule.Property(x => x.MisfirePolicy).HasColumnName("misfire_policy").HasConversion<short>();
        schedule.Property(x => x.NextOccurrence).HasColumnName("next_occurrence"); schedule.Property(x => x.IsPaused).HasColumnName("is_paused");
        schedule.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        schedule.Property(x => x.Queue).HasColumnName("queue"); schedule.Property(x => x.Priority).HasColumnName("priority");
        schedule.Property(x => x.DeduplicationKey).HasColumnName("deduplication_key"); schedule.Property(x => x.CorrelationId).HasColumnName("correlation_id");
        schedule.HasIndex(x => new { x.NextOccurrence, x.Id }, "ix_job_scheduler_schedules_due").HasFilter("is_paused = false");
    }
}
