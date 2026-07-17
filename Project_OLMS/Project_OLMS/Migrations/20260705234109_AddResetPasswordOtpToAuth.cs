using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Project_OLMS.Migrations
{
    /// <inheritdoc />
    public partial class AddResetPasswordOtpToAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResetPasswordOtp",
                table: "Users",
                type: "varchar(6)",
                maxLength: 6,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResetPasswordOtp",
                table: "Users");
        }
    }
}
