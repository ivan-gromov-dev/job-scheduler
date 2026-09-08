using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace JobScheduler.PostgreSql.Migrations;

[DbContext(typeof(JobSchedulerDbContext))]
[Migration("202609080003_Operations")]
public sealed class Operations : Migration
{
    private static readonly string[] PollColumns = ["queue", "priority", "scheduled_at", "enqueued_at", "id"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("queue", "job_scheduler_jobs", nullable: false, defaultValue: "default");
        migrationBuilder.AddColumn<int>("priority", "job_scheduler_jobs", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<string>("correlation_id", "job_scheduler_jobs", nullable: true);
        migrationBuilder.DropIndex("ix_job_scheduler_jobs_poll", "job_scheduler_jobs");
        migrationBuilder.CreateIndex("ix_job_scheduler_jobs_poll", "job_scheduler_jobs", PollColumns,
            descending: [false, true, false, false, false], filter: "status = 0");
        migrationBuilder.CreateIndex("ix_job_scheduler_jobs_correlation", "job_scheduler_jobs", "correlation_id",
            filter: "correlation_id IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("ix_job_scheduler_jobs_correlation", "job_scheduler_jobs");
        migrationBuilder.DropIndex("ix_job_scheduler_jobs_poll", "job_scheduler_jobs");
        migrationBuilder.DropColumn("correlation_id", "job_scheduler_jobs");
        migrationBuilder.DropColumn("priority", "job_scheduler_jobs");
        migrationBuilder.DropColumn("queue", "job_scheduler_jobs");
        migrationBuilder.CreateIndex("ix_job_scheduler_jobs_poll", "job_scheduler_jobs", ["scheduled_at", "enqueued_at", "id"], filter: "status = 0");
    }
}
