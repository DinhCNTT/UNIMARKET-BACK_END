using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniMarket.Migrations
{
    /// <inheritdoc />
    public partial class ThemChatShare : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "StartedAt",
                table: "VideoViews",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "NgayLuu",
                table: "VideoTinDangSaves",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "VideoLikes",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "VideoComments",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "UserChatStates",
                type: "datetimeoffset",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "UserChatStates",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ThoiGianXem",
                table: "TinNhans",
                type: "datetimeoffset",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ThoiGianGui",
                table: "TinNhans",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "NgayTao",
                table: "TinDangYeuThichs",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "NgayDang",
                table: "TinDangs",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "NgayCapNhat",
                table: "TinDangs",
                type: "datetimeoffset",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "SearchHistories",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ThoiGianTao",
                table: "CuocTroChuyens",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "BlockedAt",
                table: "BlockedUsers",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "LastOnlineTime",
                table: "AspNetUsers",
                type: "datetimeoffset",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CodeGeneratedAt",
                table: "AspNetUsers",
                type: "datetimeoffset",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "CuocTroChuyenSocials",
                columns: table => new
                {
                    MaCuocTroChuyen = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ThoiGianTao = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NgayCapNhat = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsEmpty = table.Column<bool>(type: "bit", nullable: false),
                    IsBlocked = table.Column<bool>(type: "bit", nullable: false),
                    MaNguoiChan = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CuocTroChuyenSocials", x => x.MaCuocTroChuyen);
                });

            migrationBuilder.CreateTable(
                name: "Follows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FollowerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FollowingId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FollowedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Follows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Follows_AspNetUsers_FollowerId",
                        column: x => x.FollowerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Follows_AspNetUsers_FollowingId",
                        column: x => x.FollowingId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserActivities",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    IsOnline = table.Column<bool>(type: "bit", nullable: false),
                    LastActive = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserActivities", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "UserHiddenConversations",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    MaCuocTroChuyen = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ThoiGianAn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    HasReappeared = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserHiddenConversations", x => new { x.UserId, x.MaCuocTroChuyen });
                });

            migrationBuilder.CreateTable(
                name: "NguoiThamGiaSocials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaCuocTroChuyen = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    MaNguoiDung = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    IsOnline = table.Column<bool>(type: "bit", nullable: false),
                    LastSeen = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NguoiThamGiaSocials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NguoiThamGiaSocials_AspNetUsers_MaNguoiDung",
                        column: x => x.MaNguoiDung,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NguoiThamGiaSocials_CuocTroChuyenSocials_MaCuocTroChuyen",
                        column: x => x.MaCuocTroChuyen,
                        principalTable: "CuocTroChuyenSocials",
                        principalColumn: "MaCuocTroChuyen",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Shares",
                columns: table => new
                {
                    ShareId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ShareType = table.Column<int>(type: "int", nullable: false),
                    TargetType = table.Column<int>(type: "int", nullable: false),
                    DisplayMode = table.Column<int>(type: "int", nullable: false),
                    TinDangId = table.Column<int>(type: "int", nullable: true),
                    MaCuocTroChuyen = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ShareLink = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Platform = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SharedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PreviewTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PreviewImage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PreviewVideo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shares", x => x.ShareId);
                    table.ForeignKey(
                        name: "FK_Shares_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Shares_CuocTroChuyenSocials_MaCuocTroChuyen",
                        column: x => x.MaCuocTroChuyen,
                        principalTable: "CuocTroChuyenSocials",
                        principalColumn: "MaCuocTroChuyen");
                    table.ForeignKey(
                        name: "FK_Shares_TinDangs_TinDangId",
                        column: x => x.TinDangId,
                        principalTable: "TinDangs",
                        principalColumn: "MaTinDang");
                });

            migrationBuilder.CreateTable(
                name: "TinNhanSocials",
                columns: table => new
                {
                    MaTinNhan = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    MaCuocTroChuyen = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    MaNguoiGui = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    NoiDung = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MediaUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ThoiGianGui = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DaXem = table.Column<bool>(type: "bit", nullable: false),
                    ParentMessageId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    IsRecalled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TinNhanSocials", x => x.MaTinNhan);
                    table.ForeignKey(
                        name: "FK_TinNhanSocials_AspNetUsers_MaNguoiGui",
                        column: x => x.MaNguoiGui,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TinNhanSocials_CuocTroChuyenSocials_MaCuocTroChuyen",
                        column: x => x.MaCuocTroChuyen,
                        principalTable: "CuocTroChuyenSocials",
                        principalColumn: "MaCuocTroChuyen",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TinNhanSocials_TinNhanSocials_ParentMessageId",
                        column: x => x.ParentMessageId,
                        principalTable: "TinNhanSocials",
                        principalColumn: "MaTinNhan");
                });

            migrationBuilder.CreateTable(
                name: "DeletedMessagesForUsers",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    TinNhanSocialId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeletedMessagesForUsers", x => new { x.UserId, x.TinNhanSocialId });
                    table.ForeignKey(
                        name: "FK_DeletedMessagesForUsers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeletedMessagesForUsers_TinNhanSocials_TinNhanSocialId",
                        column: x => x.TinNhanSocialId,
                        principalTable: "TinNhanSocials",
                        principalColumn: "MaTinNhan",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeletedMessagesForUsers_TinNhanSocialId",
                table: "DeletedMessagesForUsers",
                column: "TinNhanSocialId");

            migrationBuilder.CreateIndex(
                name: "IX_Follows_FollowerId",
                table: "Follows",
                column: "FollowerId");

            migrationBuilder.CreateIndex(
                name: "IX_Follows_FollowingId",
                table: "Follows",
                column: "FollowingId");

            migrationBuilder.CreateIndex(
                name: "IX_NguoiThamGiaSocials_MaCuocTroChuyen",
                table: "NguoiThamGiaSocials",
                column: "MaCuocTroChuyen");

            migrationBuilder.CreateIndex(
                name: "IX_NguoiThamGiaSocials_MaNguoiDung",
                table: "NguoiThamGiaSocials",
                column: "MaNguoiDung");

            migrationBuilder.CreateIndex(
                name: "IX_Shares_MaCuocTroChuyen",
                table: "Shares",
                column: "MaCuocTroChuyen");

            migrationBuilder.CreateIndex(
                name: "IX_Shares_TinDangId",
                table: "Shares",
                column: "TinDangId");

            migrationBuilder.CreateIndex(
                name: "IX_Shares_UserId",
                table: "Shares",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TinNhanSocials_MaCuocTroChuyen",
                table: "TinNhanSocials",
                column: "MaCuocTroChuyen");

            migrationBuilder.CreateIndex(
                name: "IX_TinNhanSocials_MaNguoiGui",
                table: "TinNhanSocials",
                column: "MaNguoiGui");

            migrationBuilder.CreateIndex(
                name: "IX_TinNhanSocials_ParentMessageId",
                table: "TinNhanSocials",
                column: "ParentMessageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeletedMessagesForUsers");

            migrationBuilder.DropTable(
                name: "Follows");

            migrationBuilder.DropTable(
                name: "NguoiThamGiaSocials");

            migrationBuilder.DropTable(
                name: "Shares");

            migrationBuilder.DropTable(
                name: "UserActivities");

            migrationBuilder.DropTable(
                name: "UserHiddenConversations");

            migrationBuilder.DropTable(
                name: "TinNhanSocials");

            migrationBuilder.DropTable(
                name: "CuocTroChuyenSocials");

            migrationBuilder.AlterColumn<DateTime>(
                name: "StartedAt",
                table: "VideoViews",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AlterColumn<DateTime>(
                name: "NgayLuu",
                table: "VideoTinDangSaves",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "VideoLikes",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "VideoComments",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "UserChatStates",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "UserChatStates",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AlterColumn<DateTime>(
                name: "ThoiGianXem",
                table: "TinNhans",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "ThoiGianGui",
                table: "TinNhans",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AlterColumn<DateTime>(
                name: "NgayTao",
                table: "TinDangYeuThichs",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AlterColumn<DateTime>(
                name: "NgayDang",
                table: "TinDangs",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AlterColumn<DateTime>(
                name: "NgayCapNhat",
                table: "TinDangs",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "SearchHistories",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AlterColumn<DateTime>(
                name: "ThoiGianTao",
                table: "CuocTroChuyens",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AlterColumn<DateTime>(
                name: "BlockedAt",
                table: "BlockedUsers",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AlterColumn<DateTime>(
                name: "LastOnlineTime",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CodeGeneratedAt",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true);
        }
    }
}
