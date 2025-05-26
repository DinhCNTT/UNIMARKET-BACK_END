using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniMarket.Migrations
{
    /// <inheritdoc />
    public partial class ThemBangChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CuocTroChuyens",
                columns: table => new
                {
                    MaCuocTroChuyen = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ThoiGianTao = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CuocTroChuyens", x => x.MaCuocTroChuyen);
                });

            migrationBuilder.CreateTable(
                name: "NguoiThamGias",
                columns: table => new
                {
                    MaThamGia = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaCuocTroChuyen = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    MaNguoiDung = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NguoiThamGias", x => x.MaThamGia);
                    table.ForeignKey(
                        name: "FK_NguoiThamGias_AspNetUsers_MaNguoiDung",
                        column: x => x.MaNguoiDung,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NguoiThamGias_CuocTroChuyens_MaCuocTroChuyen",
                        column: x => x.MaCuocTroChuyen,
                        principalTable: "CuocTroChuyens",
                        principalColumn: "MaCuocTroChuyen",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TinNhans",
                columns: table => new
                {
                    MaTinNhan = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaCuocTroChuyen = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    MaNguoiGui = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    NoiDung = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ThoiGianGui = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TinNhans", x => x.MaTinNhan);
                    table.ForeignKey(
                        name: "FK_TinNhans_AspNetUsers_MaNguoiGui",
                        column: x => x.MaNguoiGui,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TinNhans_CuocTroChuyens_MaCuocTroChuyen",
                        column: x => x.MaCuocTroChuyen,
                        principalTable: "CuocTroChuyens",
                        principalColumn: "MaCuocTroChuyen",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NguoiThamGias_MaCuocTroChuyen",
                table: "NguoiThamGias",
                column: "MaCuocTroChuyen");

            migrationBuilder.CreateIndex(
                name: "IX_NguoiThamGias_MaNguoiDung",
                table: "NguoiThamGias",
                column: "MaNguoiDung");

            migrationBuilder.CreateIndex(
                name: "IX_TinNhans_MaCuocTroChuyen",
                table: "TinNhans",
                column: "MaCuocTroChuyen");

            migrationBuilder.CreateIndex(
                name: "IX_TinNhans_MaNguoiGui",
                table: "TinNhans",
                column: "MaNguoiGui");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NguoiThamGias");

            migrationBuilder.DropTable(
                name: "TinNhans");

            migrationBuilder.DropTable(
                name: "CuocTroChuyens");
        }
    }
}
