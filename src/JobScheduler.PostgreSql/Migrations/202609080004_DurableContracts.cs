using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace JobScheduler.PostgreSql.Migrations;

[DbContext(typeof(JobSchedulerDbContext))]
[Migration("202609080004_DurableContracts")]
public sealed class DurableContracts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>("payload_version", "job_scheduler_jobs", nullable: false, defaultValue: 1);
        migrationBuilder.AddColumn<string>("attempt_history", "job_scheduler_jobs", type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb");
        migrationBuilder.AddColumn<int>("payload_version", "job_scheduler_schedules", nullable: false, defaultValue: 1);
        migrationBuilder.AddColumn<string>("queue", "job_scheduler_schedules", nullable: false, defaultValue: "default");
        migrationBuilder.AddColumn<int>("priority", "job_scheduler_schedules", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<string>("deduplication_key", "job_scheduler_schedules", nullable: true);
        migrationBuilder.AddColumn<string>("correlation_id", "job_scheduler_schedules", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("payload_version", "job_scheduler_jobs");
        migrationBuilder.DropColumn("attempt_history", "job_scheduler_jobs");
        migrationBuilder.DropColumn("payload_version", "job_scheduler_schedules");
        migrationBuilder.DropColumn("queue", "job_scheduler_schedules");
        migrationBuilder.DropColumn("priority", "job_scheduler_schedules");
        migrationBuilder.DropColumn("deduplication_key", "job_scheduler_schedules");
        migrationBuilder.DropColumn("correlation_id", "job_scheduler_schedules");
    }
}
