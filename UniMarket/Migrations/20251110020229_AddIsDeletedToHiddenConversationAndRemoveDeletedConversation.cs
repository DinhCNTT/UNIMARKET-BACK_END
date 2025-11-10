using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniMarket.Migrations
{
    /// <inheritdoc />
    public partial class AddIsDeletedToHiddenConversationAndRemoveDeletedConversation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserDeletedConversations");

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "UserHiddenConversations",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "UserHiddenConversations");

            migrationBuilder.CreateTable(
                name: "UserDeletedConversations",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    MaCuocTroChuyen = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    HasReappeared = table.Column<bool>(type: "bit", nullable: false),
                    ThoiGianXoa = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDeletedConversations", x => new { x.UserId, x.MaCuocTroChuyen });
                });
        }
    }
}
