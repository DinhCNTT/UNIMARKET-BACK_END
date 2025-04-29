using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniMarket.Migrations
{
    /// <inheritdoc />
    public partial class Update_DanhMucSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnhDanhMuc",
                table: "DanhMucs");

            migrationBuilder.DropColumn(
                name: "Icon",
                table: "DanhMucs");

            migrationBuilder.AddColumn<string>(
                name: "AnhDanhMucCha",
                table: "DanhMucChas",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Icon",
                table: "DanhMucChas",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnhDanhMucCha",
                table: "DanhMucChas");

            migrationBuilder.DropColumn(
                name: "Icon",
                table: "DanhMucChas");

            migrationBuilder.AddColumn<string>(
                name: "AnhDanhMuc",
                table: "DanhMucs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Icon",
                table: "DanhMucs",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
