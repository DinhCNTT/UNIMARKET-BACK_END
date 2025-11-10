using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniMarket.Migrations
{
    /// <inheritdoc />
    public partial class LuuTinNhanXoa02 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TinNhanXoas_CuocTroChuyens_MaCuocTroChuyen",
                table: "TinNhanXoas");

            migrationBuilder.DropForeignKey(
                name: "FK_TinNhanXoas_TinNhans_TinNhanMaTinNhan",
                table: "TinNhanXoas");

            migrationBuilder.DropIndex(
                name: "IX_TinNhanXoa_UserId_MaCuocTroChuyen",
                table: "TinNhanXoas");

            migrationBuilder.DropIndex(
                name: "IX_TinNhanXoas_MaCuocTroChuyen",
                table: "TinNhanXoas");

            migrationBuilder.DropIndex(
                name: "IX_TinNhanXoas_TinNhanMaTinNhan",
                table: "TinNhanXoas");

            migrationBuilder.DropColumn(
                name: "DeletedMessageIds",
                table: "TinNhanXoas");

            migrationBuilder.DropColumn(
                name: "MaCuocTroChuyen",
                table: "TinNhanXoas");

            migrationBuilder.DropColumn(
                name: "TinNhanMaTinNhan",
                table: "TinNhanXoas");

            migrationBuilder.AddColumn<int>(
                name: "MaTinNhan",
                table: "TinNhanXoas",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_TinNhanXoa_UserId_MaTinNhan",
                table: "TinNhanXoas",
                columns: new[] { "UserId", "MaTinNhan" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TinNhanXoas_MaTinNhan",
                table: "TinNhanXoas",
                column: "MaTinNhan");

            migrationBuilder.AddForeignKey(
                name: "FK_TinNhanXoas_TinNhans_MaTinNhan",
                table: "TinNhanXoas",
                column: "MaTinNhan",
                principalTable: "TinNhans",
                principalColumn: "MaTinNhan");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TinNhanXoas_TinNhans_MaTinNhan",
                table: "TinNhanXoas");

            migrationBuilder.DropIndex(
                name: "IX_TinNhanXoa_UserId_MaTinNhan",
                table: "TinNhanXoas");

            migrationBuilder.DropIndex(
                name: "IX_TinNhanXoas_MaTinNhan",
                table: "TinNhanXoas");

            migrationBuilder.DropColumn(
                name: "MaTinNhan",
                table: "TinNhanXoas");

            migrationBuilder.AddColumn<string>(
                name: "DeletedMessageIds",
                table: "TinNhanXoas",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "MaCuocTroChuyen",
                table: "TinNhanXoas",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "TinNhanMaTinNhan",
                table: "TinNhanXoas",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TinNhanXoa_UserId_MaCuocTroChuyen",
                table: "TinNhanXoas",
                columns: new[] { "UserId", "MaCuocTroChuyen" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TinNhanXoas_MaCuocTroChuyen",
                table: "TinNhanXoas",
                column: "MaCuocTroChuyen");

            migrationBuilder.CreateIndex(
                name: "IX_TinNhanXoas_TinNhanMaTinNhan",
                table: "TinNhanXoas",
                column: "TinNhanMaTinNhan");

            migrationBuilder.AddForeignKey(
                name: "FK_TinNhanXoas_CuocTroChuyens_MaCuocTroChuyen",
                table: "TinNhanXoas",
                column: "MaCuocTroChuyen",
                principalTable: "CuocTroChuyens",
                principalColumn: "MaCuocTroChuyen");

            migrationBuilder.AddForeignKey(
                name: "FK_TinNhanXoas_TinNhans_TinNhanMaTinNhan",
                table: "TinNhanXoas",
                column: "TinNhanMaTinNhan",
                principalTable: "TinNhans",
                principalColumn: "MaTinNhan");
        }
    }
}
