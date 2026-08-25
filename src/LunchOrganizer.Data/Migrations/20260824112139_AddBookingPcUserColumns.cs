using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LunchOrganizer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingPcUserColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "user_email",
                table: "bookings",
                type: "nvarchar(320)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "user_fullname",
                table: "bookings",
                type: "nvarchar(256)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "user_name",
                table: "bookings",
                type: "nvarchar(256)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "user_email",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "user_fullname",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "user_name",
                table: "bookings");
        }
    }
}
