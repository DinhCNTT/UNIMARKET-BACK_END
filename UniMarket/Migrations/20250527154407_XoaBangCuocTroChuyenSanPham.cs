using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniMarket.Migrations
{
    /// <inheritdoc />
    public partial class XoaBangCuocTroChuyenSanPham : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CuocTroChuyenSanPhams");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CuocTroChuyenSanPhams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaCuocTroChuyen = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    MaTinDang = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CuocTroChuyenSanPhams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CuocTroChuyenSanPhams_CuocTroChuyens_MaCuocTroChuyen",
                        column: x => x.MaCuocTroChuyen,
                        principalTable: "CuocTroChuyens",
                        principalColumn: "MaCuocTroChuyen",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CuocTroChuyenSanPhams_TinDangs_MaTinDang",
                        column: x => x.MaTinDang,
                        principalTable: "TinDangs",
                        principalColumn: "MaTinDang",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CuocTroChuyenSanPhams_MaCuocTroChuyen",
                table: "CuocTroChuyenSanPhams",
                column: "MaCuocTroChuyen");

            migrationBuilder.CreateIndex(
                name: "IX_CuocTroChuyenSanPhams_MaTinDang",
                table: "CuocTroChuyenSanPhams",
                column: "MaTinDang");
        }
    }
}
