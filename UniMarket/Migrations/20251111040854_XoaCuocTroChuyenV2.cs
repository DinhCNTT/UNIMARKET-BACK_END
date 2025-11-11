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
            // Drop table only if it exists to make migration idempotent
            migrationBuilder.Sql("IF OBJECT_ID(N'[dbo].[TinNhanDaXoas]','U') IS NOT NULL DROP TABLE [dbo].[TinNhanDaXoas];");

            // Add IsDeleted column only if it does not already exist to avoid duplicate column errors
            migrationBuilder.Sql(
                "IF COL_LENGTH('dbo.UserHiddenConversations','IsDeleted') IS NULL ALTER TABLE [UserHiddenConversations] ADD [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit);");

            // Create TinNhanXoas only if it does not already exist (make migration idempotent)
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[TinNhanXoas]','U') IS NULL
BEGIN
    CREATE TABLE [dbo].[TinNhanXoas](
        [Id] int NOT NULL IDENTITY(1,1),
        [MaTinNhan] int NOT NULL,
        [UserId] nvarchar(450) NOT NULL,
        [ThoiGianXoa] datetimeoffset NOT NULL DEFAULT (GETUTCDATE()),
        CONSTRAINT [PK_TinNhanXoas] PRIMARY KEY ([Id])
    );
    ALTER TABLE [dbo].[TinNhanXoas] ADD CONSTRAINT [FK_TinNhanXoas_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers]([Id]);
    ALTER TABLE [dbo].[TinNhanXoas] ADD CONSTRAINT [FK_TinNhanXoas_TinNhans_MaTinNhan] FOREIGN KEY ([MaTinNhan]) REFERENCES [dbo].[TinNhans]([MaTinNhan]);
    CREATE UNIQUE INDEX [IX_TinNhanXoa_UserId_MaTinNhan] ON [dbo].[TinNhanXoas]([UserId],[MaTinNhan]);
    CREATE INDEX [IX_TinNhanXoas_MaTinNhan] ON [dbo].[TinNhanXoas]([MaTinNhan]);
END
");
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
