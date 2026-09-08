using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace JobScheduler.PostgreSql.Migrations;

[DbContext(typeof(JobSchedulerDbContext))]
[Migration("202609080002_Scheduling")]
public sealed class Scheduling : Migration
{
    private static readonly string[] DueColumns = ["next_occurrence", "id"];
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("job_scheduler_schedules", table => new
        {
            id = table.Column<Guid>(nullable: false),
            job_type = table.Column<string>(nullable: false),
            payload = table.Column<string>(nullable: false),
            cron_expression = table.Column<string>(nullable: true),
            time_zone_id = table.Column<string>(nullable: false),
            misfire_policy = table.Column<short>(nullable: false),
            next_occurrence = table.Column<DateTimeOffset>(nullable: false),
            is_paused = table.Column<bool>(nullable: false),
            version = table.Column<long>(nullable: false),
        }, constraints: table => table.PrimaryKey("pk_job_scheduler_schedules", x => x.id));
        migrationBuilder.CreateIndex("ix_job_scheduler_schedules_due", "job_scheduler_schedules", DueColumns, filter: "is_paused = false");
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("job_scheduler_schedules");
}
