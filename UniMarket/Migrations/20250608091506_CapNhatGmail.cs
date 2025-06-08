using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniMarket.Migrations
{
    /// <inheritdoc />
    public partial class CapNhatGmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AvatarUrl",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CodeGeneratedAt",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailVerificationCode",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvatarUrl",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CodeGeneratedAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "EmailVerificationCode",
                table: "AspNetUsers");
        }
    }
}
