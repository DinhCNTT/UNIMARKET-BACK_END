using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniMarket.Migrations
{
    /// <inheritdoc />
    public partial class RemoveVideoViewTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- QUAN TRỌNG: Dùng SQL thuần để xóa bảng ---
            // Lệnh này sẽ xóa bảng ngay lập tức, kéo theo xóa luôn các Index/Foreign Key đang gây lỗi.
            // Chúng ta DROP cả 2 tên để chắc chắn sạch sẽ dù DB đang ở trạng thái nào.

            migrationBuilder.Sql("DROP TABLE IF EXISTS [VideoViews]");
            migrationBuilder.Sql("DROP TABLE IF EXISTS [VideoView]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nếu rollback lại migration này, chúng ta tạo lại bảng VideoViews như cũ
            migrationBuilder.CreateTable(
                name: "VideoViews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaTinDang = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WatchedSeconds = table.Column<int>(type: "int", nullable: false),
                    IsCompleted = table.Column<bool>(type: "bit", nullable: false),
                    RewatchCount = table.Column<int>(type: "int", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DeviceName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoViews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VideoViews_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VideoViews_TinDangs_MaTinDang",
                        column: x => x.MaTinDang,
                        principalTable: "TinDangs",
                        principalColumn: "MaTinDang",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VideoViews_MaTinDang",
                table: "VideoViews",
                column: "MaTinDang");

            migrationBuilder.CreateIndex(
                name: "IX_VideoViews_UserId",
                table: "VideoViews",
                column: "UserId");
        }
    }
}