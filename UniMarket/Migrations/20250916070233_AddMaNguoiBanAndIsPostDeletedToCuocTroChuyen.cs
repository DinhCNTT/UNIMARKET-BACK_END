using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniMarket.Migrations
{
    /// <inheritdoc />
    public partial class AddMaNguoiBanAndIsPostDeletedToCuocTroChuyen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPostDeleted",
                table: "CuocTroChuyens",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MaNguoiBan",
                table: "CuocTroChuyens",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPostDeleted",
                table: "CuocTroChuyens");

            migrationBuilder.DropColumn(
                name: "MaNguoiBan",
                table: "CuocTroChuyens");
        }
    }
}
