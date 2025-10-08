using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SOSCallParsial.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AlarmLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Account = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    EventCode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    GroupCode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ZoneCode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    PhoneNumber = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RawMessage = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlarmLogs", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlarmLogs");
        }
    }
}
