using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LunchOrganizer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMenuPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "price",
                table: "menus",
                type: "decimal(10,2)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_menus_price_non_negative",
                table: "menus",
                sql: "price IS NULL OR price >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_menus_price_non_negative",
                table: "menus");

            migrationBuilder.DropColumn(
                name: "price",
                table: "menus");
        }
    }
}
