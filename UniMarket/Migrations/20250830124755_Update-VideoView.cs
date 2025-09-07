using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniMarket.Migrations
{
    /// <inheritdoc />
    public partial class UpdateVideoView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ViewedAt",
                table: "VideoViews",
                newName: "StartedAt");

            migrationBuilder.AddColumn<string>(
                name: "DeviceInfo",
                table: "VideoViews",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCompleted",
                table: "VideoViews",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RewatchCount",
                table: "VideoViews",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WatchedSeconds",
                table: "VideoViews",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeviceInfo",
                table: "VideoViews");

            migrationBuilder.DropColumn(
                name: "IsCompleted",
                table: "VideoViews");

            migrationBuilder.DropColumn(
                name: "RewatchCount",
                table: "VideoViews");

            migrationBuilder.DropColumn(
                name: "WatchedSeconds",
                table: "VideoViews");

            migrationBuilder.RenameColumn(
                name: "StartedAt",
                table: "VideoViews",
                newName: "ViewedAt");
        }
    }
}
