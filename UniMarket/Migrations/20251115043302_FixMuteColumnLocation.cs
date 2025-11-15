using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniMarket.Migrations
{
    /// <inheritdoc />
    public partial class FixMuteColumnLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsMuted",
                table: "TinNhanSocials");

            migrationBuilder.AddColumn<bool>(
                name: "IsMuted",
                table: "NguoiThamGiaSocials",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsMuted",
                table: "NguoiThamGiaSocials");

            migrationBuilder.AddColumn<bool>(
                name: "IsMuted",
                table: "TinNhanSocials",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
