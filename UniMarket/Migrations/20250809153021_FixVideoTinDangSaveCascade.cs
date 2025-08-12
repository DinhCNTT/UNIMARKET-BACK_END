using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniMarket.Migrations
{
    /// <inheritdoc />
    public partial class FixVideoTinDangSaveCascade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VideoTinDangSaves",
                columns: table => new
                {
                    MaVideoSave = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaTinDang = table.Column<int>(type: "int", nullable: false),
                    MaNguoiDung = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    NgayLuu = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoTinDangSaves", x => x.MaVideoSave);
                    table.ForeignKey(
                        name: "FK_VideoTinDangSaves_AspNetUsers_MaNguoiDung",
                        column: x => x.MaNguoiDung,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VideoTinDangSaves_TinDangs_MaTinDang",
                        column: x => x.MaTinDang,
                        principalTable: "TinDangs",
                        principalColumn: "MaTinDang",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VideoTinDangSaves_MaNguoiDung",
                table: "VideoTinDangSaves",
                column: "MaNguoiDung");

            migrationBuilder.CreateIndex(
                name: "IX_VideoTinDangSaves_MaTinDang",
                table: "VideoTinDangSaves",
                column: "MaTinDang");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VideoTinDangSaves");
        }
    }
}
