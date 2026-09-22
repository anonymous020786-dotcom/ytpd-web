using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YtpdWeb.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceUrl = table.Column<string>(type: "text", nullable: false),
                    Format = table.Column<string>(type: "text", nullable: false),
                    Quality = table.Column<string>(type: "text", nullable: false),
                    EmbedMetadata = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JobItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    VideoId = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Author = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Progress = table.Column<double>(type: "double precision", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    OutputFileName = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobItems_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobItems_JobId",
                table: "JobItems",
                column: "JobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobItems");

            migrationBuilder.DropTable(
                name: "Jobs");
        }
    }
}
