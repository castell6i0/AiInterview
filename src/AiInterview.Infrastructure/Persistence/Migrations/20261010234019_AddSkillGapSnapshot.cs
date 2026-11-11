using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiInterview.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSkillGapSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SkillGapSnapshotJson",
                table: "Sessions",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SkillGapSnapshotJson",
                table: "Sessions");
        }
    }
}
