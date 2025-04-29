using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniMarket.Migrations
{
    /// <inheritdoc />
    public partial class ExtendIdentityUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "TrangThai",
                table: "TinDangs",
                type: "int",
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "tinyint");

            migrationBuilder.CreateIndex(
                name: "IX_TinDangs_MaQuanHuyen",
                table: "TinDangs",
                column: "MaQuanHuyen");

            migrationBuilder.CreateIndex(
                name: "IX_TinDangs_MaTinhThanh",
                table: "TinDangs",
                column: "MaTinhThanh");

            migrationBuilder.AddForeignKey(
                name: "FK_TinDangs_QuanHuyens_MaQuanHuyen",
                table: "TinDangs",
                column: "MaQuanHuyen",
                principalTable: "QuanHuyens",
                principalColumn: "MaQuanHuyen");

            migrationBuilder.AddForeignKey(
                name: "FK_TinDangs_TinhThanhs_MaTinhThanh",
                table: "TinDangs",
                column: "MaTinhThanh",
                principalTable: "TinhThanhs",
                principalColumn: "MaTinhThanh");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TinDangs_QuanHuyens_MaQuanHuyen",
                table: "TinDangs");

            migrationBuilder.DropForeignKey(
                name: "FK_TinDangs_TinhThanhs_MaTinhThanh",
                table: "TinDangs");

            migrationBuilder.DropIndex(
                name: "IX_TinDangs_MaQuanHuyen",
                table: "TinDangs");

            migrationBuilder.DropIndex(
                name: "IX_TinDangs_MaTinhThanh",
                table: "TinDangs");

            migrationBuilder.AlterColumn<byte>(
                name: "TrangThai",
                table: "TinDangs",
                type: "tinyint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");
        }
    }
}
