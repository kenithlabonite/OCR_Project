using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Project_OLMS.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentNameToGradedAnswerSheet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StudentName",
                table: "GradedAnswerSheets",
                type: "varchar(200)",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StudentName",
                table: "GradedAnswerSheets");
        }
    }
}
