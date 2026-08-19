using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LunchOrganizer.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_prices",
                columns: table => new
                {
                    price_date = table.Column<DateOnly>(type: "date", nullable: false),
                    price = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false, defaultValueSql: "CAST(SYSUTCDATETIME() AS datetimeoffset)"),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false, defaultValueSql: "CAST(SYSUTCDATETIME() AS datetimeoffset)"),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_prices", x => x.price_date);
                    table.CheckConstraint("ck_daily_prices_price_non_negative", "price >= 0");
                });

            migrationBuilder.CreateTable(
                name: "email_log",
                columns: table => new
                {
                    summary_date = table.Column<DateOnly>(type: "date", nullable: false),
                    sent_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", nullable: false),
                    recipients = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    booking_count = table.Column<int>(type: "int", nullable: false),
                    error_message = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_log", x => x.summary_date);
                });

            migrationBuilder.CreateTable(
                name: "employees",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    full_name = table.Column<string>(type: "nvarchar(200)", nullable: false, collation: "Latin1_General_CI_AS"),
                    email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false, defaultValueSql: "CAST(SYSUTCDATETIME() AS datetimeoffset)"),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false, defaultValueSql: "CAST(SYSUTCDATETIME() AS datetimeoffset)"),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employees", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "menus",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    menu_date = table.Column<DateOnly>(type: "date", nullable: false),
                    menu_number = table.Column<int>(type: "int", nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false, defaultValueSql: "CAST(SYSUTCDATETIME() AS datetimeoffset)"),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false, defaultValueSql: "CAST(SYSUTCDATETIME() AS datetimeoffset)"),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menus", x => x.id);
                    table.UniqueConstraint("AK_menus_id_menu_date", x => new { x.id, x.menu_date });
                    table.CheckConstraint("ck_menus_menu_number_positive", "menu_number >= 1");
                });

            migrationBuilder.CreateTable(
                name: "bookings",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    employee_id = table.Column<int>(type: "int", nullable: false),
                    booking_date = table.Column<DateOnly>(type: "date", nullable: false),
                    menu_id = table.Column<int>(type: "int", nullable: false),
                    price_snapshot = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false, defaultValueSql: "CAST(SYSUTCDATETIME() AS datetimeoffset)"),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false, defaultValueSql: "CAST(SYSUTCDATETIME() AS datetimeoffset)"),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bookings", x => x.id);
                    table.CheckConstraint("ck_bookings_price_snapshot_non_negative", "price_snapshot >= 0");
                    table.ForeignKey(
                        name: "FK_bookings_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bookings_menus_menu_id_booking_date",
                        columns: x => new { x.menu_id, x.booking_date },
                        principalTable: "menus",
                        principalColumns: new[] { "id", "menu_date" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_booking_date",
                table: "bookings",
                column: "booking_date");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_employee_date",
                table: "bookings",
                columns: new[] { "employee_id", "booking_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bookings_menu_id_booking_date",
                table: "bookings",
                columns: new[] { "menu_id", "booking_date" });

            migrationBuilder.CreateIndex(
                name: "ix_employees_full_name",
                table: "employees",
                column: "full_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_menus_menu_date",
                table: "menus",
                column: "menu_date");

            migrationBuilder.CreateIndex(
                name: "ix_menus_menu_date_menu_number",
                table: "menus",
                columns: new[] { "menu_date", "menu_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bookings");

            migrationBuilder.DropTable(
                name: "daily_prices");

            migrationBuilder.DropTable(
                name: "email_log");

            migrationBuilder.DropTable(
                name: "employees");

            migrationBuilder.DropTable(
                name: "menus");
        }
    }
}
