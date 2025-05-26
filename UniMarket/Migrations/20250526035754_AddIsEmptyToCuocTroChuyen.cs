using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniMarket.Migrations
{
    /// <inheritdoc />
    public partial class AddIsEmptyToCuocTroChuyen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsEmpty",
                table: "CuocTroChuyens",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsEmpty",
                table: "CuocTroChuyens");
        }
    }
}
