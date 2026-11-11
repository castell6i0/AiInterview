using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiInterview.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionAiProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AiProvider",
                table: "Sessions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Gemini");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiProvider",
                table: "Sessions");
        }
    }
}
