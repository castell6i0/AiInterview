using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiInterview.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountEmailConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConfirmationEmailRequestedAt",
                table: "AspNetUsers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailConfirmationRequired",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfirmationEmailRequestedAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "EmailConfirmationRequired",
                table: "AspNetUsers");
        }
    }
}
