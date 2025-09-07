using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniMarket.Migrations
{
    /// <inheritdoc />
    public partial class UpdateVideoView49 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeviceInfo",
                table: "VideoViews");

            migrationBuilder.AddColumn<string>(
                name: "DeviceName",
                table: "VideoViews",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "VideoViews",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeviceName",
                table: "VideoViews");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "VideoViews");

            migrationBuilder.AddColumn<string>(
                name: "DeviceInfo",
                table: "VideoViews",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
