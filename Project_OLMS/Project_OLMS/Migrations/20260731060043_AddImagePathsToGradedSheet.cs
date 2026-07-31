using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Project_OLMS.Migrations
{
    /// <inheritdoc />
    public partial class AddImagePathsToGradedSheet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImagePathsJson",
                table: "GradedAnswerSheets",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImagePathsJson",
                table: "GradedAnswerSheets");
        }
    }
}
