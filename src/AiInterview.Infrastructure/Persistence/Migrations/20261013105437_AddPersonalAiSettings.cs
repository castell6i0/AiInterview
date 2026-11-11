using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiInterview.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalAiSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserAiSettings",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    UsePersonalSettings = table.Column<bool>(type: "bit", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProtectedGeminiApiKey = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true),
                    ProtectedOpenAiApiKey = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true),
                    GeminiModel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OpenAiModel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAiSettings", x => x.UserId);
                    table.CheckConstraint("CK_UserAiSettings_Provider", "[Provider] IN ('Gemini', 'OpenAI')");
                    table.ForeignKey(
                        name: "FK_UserAiSettings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserAiSettings");
        }
    }
}
