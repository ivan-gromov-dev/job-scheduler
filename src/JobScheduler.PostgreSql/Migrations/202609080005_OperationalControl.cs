using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace JobScheduler.PostgreSql.Migrations;

[DbContext(typeof(JobSchedulerDbContext))]
[Migration("202609080005_OperationalControl")]
public sealed class OperationalControl : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>("materialization_history", "job_scheduler_schedules", type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn("materialization_history", "job_scheduler_schedules");
}
