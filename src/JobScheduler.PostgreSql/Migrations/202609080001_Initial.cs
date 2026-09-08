using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace JobScheduler.PostgreSql.Migrations;

[DbContext(typeof(JobSchedulerDbContext))]
[Migration("202609080001_Initial")]
public sealed class Initial : Migration
{
    private static readonly string[] PollColumns = ["scheduled_at", "enqueued_at", "id"];
    private static readonly string[] LeaseColumns = ["lease_expires_at", "id"];
    private static readonly string[] DeadLetterColumns = ["completed_at", "id"];
    private static readonly string[] DeduplicationColumns = ["type", "deduplication_key"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "job_scheduler_jobs", columns: table => new
        {
            id = table.Column<Guid>(nullable: false),
            type = table.Column<string>(nullable: false),
            payload = table.Column<string>(nullable: false),
            enqueued_at = table.Column<DateTimeOffset>(nullable: false),
            scheduled_at = table.Column<DateTimeOffset>(nullable: false),
            status = table.Column<short>(nullable: false),
            attempt = table.Column<int>(nullable: false, defaultValue: 0),
            failure = table.Column<string>(nullable: true),
            failure_kind = table.Column<short>(nullable: true),
            deduplication_key = table.Column<string>(nullable: true),
            completed_at = table.Column<DateTimeOffset>(nullable: true),
            lease_token = table.Column<Guid>(nullable: true),
            lease_expires_at = table.Column<DateTimeOffset>(nullable: true),
            row_version = table.Column<long>(nullable: false, defaultValue: 0L),
        }, constraints: table =>
        {
            table.PrimaryKey("pk_job_scheduler_jobs", x => x.id);
            table.CheckConstraint("ck_job_scheduler_status", "status BETWEEN 0 AND 5");
            table.CheckConstraint("ck_job_scheduler_attempt", "attempt >= 0");
            table.CheckConstraint("ck_job_scheduler_lease", "(status = 1 AND lease_token IS NOT NULL AND lease_expires_at IS NOT NULL) OR (status <> 1 AND lease_token IS NULL AND lease_expires_at IS NULL)");
        });
        migrationBuilder.CreateIndex("ix_job_scheduler_jobs_poll", "job_scheduler_jobs", PollColumns, filter: "status = 0");
        migrationBuilder.CreateIndex("ix_job_scheduler_jobs_expired_leases", "job_scheduler_jobs", LeaseColumns, filter: "status = 1");
        migrationBuilder.CreateIndex("ix_job_scheduler_jobs_dead_letters", "job_scheduler_jobs", DeadLetterColumns, filter: "status = 5");
        migrationBuilder.CreateIndex("ux_job_scheduler_jobs_deduplication", "job_scheduler_jobs", DeduplicationColumns, unique: true, filter: "deduplication_key IS NOT NULL AND status IN (0, 1, 2)");
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("job_scheduler_jobs");
}
