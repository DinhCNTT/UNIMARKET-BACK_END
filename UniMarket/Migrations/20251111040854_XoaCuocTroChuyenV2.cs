using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniMarket.Migrations
{
    /// <inheritdoc />
    public partial class XoaCuocTroChuyenV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TinNhanDaXoas");

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "UserHiddenConversations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "TinNhanXoas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaTinNhan = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ThoiGianXoa = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TinNhanXoas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TinNhanXoas_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TinNhanXoas_TinNhans_MaTinNhan",
                        column: x => x.MaTinNhan,
                        principalTable: "TinNhans",
                        principalColumn: "MaTinNhan");
                });

            migrationBuilder.CreateIndex(
                name: "IX_TinNhanXoa_UserId_MaTinNhan",
                table: "TinNhanXoas",
                columns: new[] { "UserId", "MaTinNhan" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TinNhanXoas_MaTinNhan",
                table: "TinNhanXoas",
                column: "MaTinNhan");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TinNhanXoas");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "UserHiddenConversations");

            migrationBuilder.CreateTable(
                name: "TinNhanDaXoas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TinNhanId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TinNhanDaXoas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TinNhanDaXoas_TinNhans_TinNhanId",
                        column: x => x.TinNhanId,
                        principalTable: "TinNhans",
                        principalColumn: "MaTinNhan",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TinNhanDaXoas_TinNhanId",
                table: "TinNhanDaXoas",
                column: "TinNhanId");
        }
    }
}
