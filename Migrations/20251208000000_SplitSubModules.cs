using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Server.Migrations
{
    public partial class SplitSubModules : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CourseModules_CourseModules_ParentModuleId",
                table: "CourseModules");

            migrationBuilder.DropIndex(
                name: "IX_CourseModules_ParentModuleId",
                table: "CourseModules");

            migrationBuilder.DropColumn(
                name: "ParentModuleId",
                table: "CourseModules");

            migrationBuilder.CreateTable(
                name: "CourseSubModules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CourseModuleId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    EstimatedHours = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseSubModules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseSubModules_CourseModules_CourseModuleId",
                        column: x => x.CourseModuleId,
                        principalTable: "CourseModules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourseSubModules_CourseModuleId",
                table: "CourseSubModules",
                column: "CourseModuleId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourseSubModules");

            migrationBuilder.AddColumn<int>(
                name: "ParentModuleId",
                table: "CourseModules",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourseModules_ParentModuleId",
                table: "CourseModules",
                column: "ParentModuleId");

            migrationBuilder.AddForeignKey(
                name: "FK_CourseModules_CourseModules_ParentModuleId",
                table: "CourseModules",
                column: "ParentModuleId",
                principalTable: "CourseModules",
                principalColumn: "Id");
        }
    }
}
