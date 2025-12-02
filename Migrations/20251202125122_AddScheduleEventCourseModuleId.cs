using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleEventCourseModuleId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CourseModuleId",
                table: "ScheduleEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleEvents_CourseModuleId",
                table: "ScheduleEvents",
                column: "CourseModuleId");

            migrationBuilder.AddForeignKey(
                name: "FK_ScheduleEvents_CourseModules_CourseModuleId",
                table: "ScheduleEvents",
                column: "CourseModuleId",
                principalTable: "CourseModules",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ScheduleEvents_CourseModules_CourseModuleId",
                table: "ScheduleEvents");

            migrationBuilder.DropIndex(
                name: "IX_ScheduleEvents_CourseModuleId",
                table: "ScheduleEvents");

            migrationBuilder.DropColumn(
                name: "CourseModuleId",
                table: "ScheduleEvents");
        }
    }
}
