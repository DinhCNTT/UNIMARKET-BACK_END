using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniMarket.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Address = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Age = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Role = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmailVerificationCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CodeGeneratedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AvatarUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsOnline = table.Column<bool>(type: "bit", nullable: false),
                    LastOnlineTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BlockedUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BlockerId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BlockedId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BlockedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockedUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CuocTroChuyens",
                columns: table => new
                {
                    MaCuocTroChuyen = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ThoiGianTao = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsEmpty = table.Column<bool>(type: "bit", nullable: false),
                    MaTinDang = table.Column<int>(type: "int", nullable: false),
                    TieuDeTinDang = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AnhDaiDienTinDang = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GiaTinDang = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MaNguoiBan = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    IsPostDeleted = table.Column<bool>(type: "bit", nullable: false),
                    IsBlocked = table.Column<bool>(type: "bit", nullable: false),
                    MaNguoiChan = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CuocTroChuyens", x => x.MaCuocTroChuyen);
                });

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
                name: "DanhMucChas",
                columns: table => new
                {
                    MaDanhMucCha = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenDanhMucCha = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AnhDanhMucCha = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Icon = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DanhMucChas", x => x.MaDanhMucCha);
                });

            migrationBuilder.CreateTable(
                name: "TinhThanhs",
                columns: table => new
                {
                    MaTinhThanh = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenTinhThanh = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TinhThanhs", x => x.MaTinhThanh);
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
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RoleId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                name: "SearchHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Keyword = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SearchHistories_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "NguoiThamGias",
                columns: table => new
                {
                    MaThamGia = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaCuocTroChuyen = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    MaNguoiDung = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IsBlocked = table.Column<bool>(type: "bit", nullable: false)
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
                    ThoiGianGui = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DaXem = table.Column<bool>(type: "bit", nullable: false),
                    ThoiGianXem = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Loai = table.Column<int>(type: "int", nullable: false),
                    MediaUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsVisible = table.Column<bool>(type: "bit", nullable: false),
                    IsRecalled = table.Column<bool>(type: "bit", nullable: false),
                    ThoiGianThuHoi = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
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

            migrationBuilder.CreateTable(
                name: "UserChatStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ChatId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IsHidden = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserChatStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserChatStates_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserChatStates_CuocTroChuyens_ChatId",
                        column: x => x.ChatId,
                        principalTable: "CuocTroChuyens",
                        principalColumn: "MaCuocTroChuyen",
                        onDelete: ReferentialAction.Cascade);
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
                name: "DanhMucs",
                columns: table => new
                {
                    MaDanhMuc = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenDanhMuc = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MaDanhMucCha = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DanhMucs", x => x.MaDanhMuc);
                    table.ForeignKey(
                        name: "FK_DanhMucs_DanhMucChas_MaDanhMucCha",
                        column: x => x.MaDanhMucCha,
                        principalTable: "DanhMucChas",
                        principalColumn: "MaDanhMucCha",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuanHuyens",
                columns: table => new
                {
                    MaQuanHuyen = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenQuanHuyen = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MaTinhThanh = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuanHuyens", x => x.MaQuanHuyen);
                    table.ForeignKey(
                        name: "FK_QuanHuyens_TinhThanhs_MaTinhThanh",
                        column: x => x.MaTinhThanh,
                        principalTable: "TinhThanhs",
                        principalColumn: "MaTinhThanh",
                        onDelete: ReferentialAction.Cascade);
                });

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

            migrationBuilder.CreateTable(
                name: "TinDangs",
                columns: table => new
                {
                    MaTinDang = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaNguoiBan = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    MaDanhMuc = table.Column<int>(type: "int", nullable: false),
                    TieuDe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MoTa = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Gia = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CoTheThoaThuan = table.Column<bool>(type: "bit", nullable: false),
                    TinhTrang = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DiaChi = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    MaTinhThanh = table.Column<int>(type: "int", nullable: true),
                    MaQuanHuyen = table.Column<int>(type: "int", nullable: true),
                    NgayDang = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NgayCapNhat = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TrangThai = table.Column<int>(type: "int", nullable: false),
                    VideoUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SoLuotXem = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TinDangs", x => x.MaTinDang);
                    table.ForeignKey(
                        name: "FK_TinDangs_AspNetUsers_MaNguoiBan",
                        column: x => x.MaNguoiBan,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TinDangs_DanhMucs_MaDanhMuc",
                        column: x => x.MaDanhMuc,
                        principalTable: "DanhMucs",
                        principalColumn: "MaDanhMuc",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TinDangs_QuanHuyens_MaQuanHuyen",
                        column: x => x.MaQuanHuyen,
                        principalTable: "QuanHuyens",
                        principalColumn: "MaQuanHuyen");
                    table.ForeignKey(
                        name: "FK_TinDangs_TinhThanhs_MaTinhThanh",
                        column: x => x.MaTinhThanh,
                        principalTable: "TinhThanhs",
                        principalColumn: "MaTinhThanh");
                });

            migrationBuilder.CreateTable(
                name: "AnhTinDangs",
                columns: table => new
                {
                    MaAnh = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaTinDang = table.Column<int>(type: "int", nullable: false),
                    DuongDan = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    LoaiMedia = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnhTinDangs", x => x.MaAnh);
                    table.ForeignKey(
                        name: "FK_AnhTinDangs_TinDangs_MaTinDang",
                        column: x => x.MaTinDang,
                        principalTable: "TinDangs",
                        principalColumn: "MaTinDang",
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
                name: "TinDangYeuThichs",
                columns: table => new
                {
                    MaYeuThich = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaTinDang = table.Column<int>(type: "int", nullable: false),
                    MaNguoiDung = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    NgayTao = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TinDangYeuThichs", x => x.MaYeuThich);
                    table.ForeignKey(
                        name: "FK_TinDangYeuThichs_AspNetUsers_MaNguoiDung",
                        column: x => x.MaNguoiDung,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TinDangYeuThichs_TinDangs_MaTinDang",
                        column: x => x.MaTinDang,
                        principalTable: "TinDangs",
                        principalColumn: "MaTinDang",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VideoComments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaTinDang = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ParentCommentId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VideoComments_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VideoComments_TinDangs_MaTinDang",
                        column: x => x.MaTinDang,
                        principalTable: "TinDangs",
                        principalColumn: "MaTinDang",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VideoComments_VideoComments_ParentCommentId",
                        column: x => x.ParentCommentId,
                        principalTable: "VideoComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VideoLikes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaTinDang = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoLikes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VideoLikes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VideoLikes_TinDangs_MaTinDang",
                        column: x => x.MaTinDang,
                        principalTable: "TinDangs",
                        principalColumn: "MaTinDang",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VideoTinDangSaves",
                columns: table => new
                {
                    MaVideoSave = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaTinDang = table.Column<int>(type: "int", nullable: false),
                    MaNguoiDung = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    NgayLuu = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "VideoViews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaTinDang = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
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
                name: "IX_AnhTinDangs_MaTinDang",
                table: "AnhTinDangs",
                column: "MaTinDang");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DanhMucs_MaDanhMucCha",
                table: "DanhMucs",
                column: "MaDanhMucCha");

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
                name: "IX_NguoiThamGias_MaCuocTroChuyen",
                table: "NguoiThamGias",
                column: "MaCuocTroChuyen");

            migrationBuilder.CreateIndex(
                name: "IX_NguoiThamGias_MaNguoiDung",
                table: "NguoiThamGias",
                column: "MaNguoiDung");

            migrationBuilder.CreateIndex(
                name: "IX_NguoiThamGiaSocials_MaCuocTroChuyen",
                table: "NguoiThamGiaSocials",
                column: "MaCuocTroChuyen");

            migrationBuilder.CreateIndex(
                name: "IX_NguoiThamGiaSocials_MaNguoiDung",
                table: "NguoiThamGiaSocials",
                column: "MaNguoiDung");

            migrationBuilder.CreateIndex(
                name: "IX_QuanHuyens_MaTinhThanh",
                table: "QuanHuyens",
                column: "MaTinhThanh");

            migrationBuilder.CreateIndex(
                name: "IX_SearchHistories_UserId",
                table: "SearchHistories",
                column: "UserId");

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
                name: "IX_TinDangs_MaDanhMuc",
                table: "TinDangs",
                column: "MaDanhMuc");

            migrationBuilder.CreateIndex(
                name: "IX_TinDangs_MaNguoiBan",
                table: "TinDangs",
                column: "MaNguoiBan");

            migrationBuilder.CreateIndex(
                name: "IX_TinDangs_MaQuanHuyen",
                table: "TinDangs",
                column: "MaQuanHuyen");

            migrationBuilder.CreateIndex(
                name: "IX_TinDangs_MaTinhThanh",
                table: "TinDangs",
                column: "MaTinhThanh");

            migrationBuilder.CreateIndex(
                name: "IX_TinDangYeuThichs_MaNguoiDung",
                table: "TinDangYeuThichs",
                column: "MaNguoiDung");

            migrationBuilder.CreateIndex(
                name: "IX_TinDangYeuThichs_MaTinDang",
                table: "TinDangYeuThichs",
                column: "MaTinDang");

            migrationBuilder.CreateIndex(
                name: "IX_TinNhanDaXoas_TinNhanId",
                table: "TinNhanDaXoas",
                column: "TinNhanId");

            migrationBuilder.CreateIndex(
                name: "IX_TinNhans_MaCuocTroChuyen",
                table: "TinNhans",
                column: "MaCuocTroChuyen");

            migrationBuilder.CreateIndex(
                name: "IX_TinNhans_MaNguoiGui",
                table: "TinNhans",
                column: "MaNguoiGui");

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

            migrationBuilder.CreateIndex(
                name: "IX_UserChatState_UserId_ChatId",
                table: "UserChatStates",
                columns: new[] { "UserId", "ChatId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserChatStates_ChatId",
                table: "UserChatStates",
                column: "ChatId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoComments_MaTinDang",
                table: "VideoComments",
                column: "MaTinDang");

            migrationBuilder.CreateIndex(
                name: "IX_VideoComments_ParentCommentId",
                table: "VideoComments",
                column: "ParentCommentId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoComments_UserId",
                table: "VideoComments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoLikes_MaTinDang",
                table: "VideoLikes",
                column: "MaTinDang");

            migrationBuilder.CreateIndex(
                name: "IX_VideoLikes_UserId",
                table: "VideoLikes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoTinDangSaves_MaNguoiDung",
                table: "VideoTinDangSaves",
                column: "MaNguoiDung");

            migrationBuilder.CreateIndex(
                name: "IX_VideoTinDangSaves_MaTinDang",
                table: "VideoTinDangSaves",
                column: "MaTinDang");

            migrationBuilder.CreateIndex(
                name: "IX_VideoViews_MaTinDang",
                table: "VideoViews",
                column: "MaTinDang");

            migrationBuilder.CreateIndex(
                name: "IX_VideoViews_UserId",
                table: "VideoViews",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnhTinDangs");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "BlockedUsers");

            migrationBuilder.DropTable(
                name: "DeletedMessagesForUsers");

            migrationBuilder.DropTable(
                name: "Follows");

            migrationBuilder.DropTable(
                name: "NguoiThamGias");

            migrationBuilder.DropTable(
                name: "NguoiThamGiaSocials");

            migrationBuilder.DropTable(
                name: "SearchHistories");

            migrationBuilder.DropTable(
                name: "Shares");

            migrationBuilder.DropTable(
                name: "TinDangYeuThichs");

            migrationBuilder.DropTable(
                name: "TinNhanDaXoas");

            migrationBuilder.DropTable(
                name: "UserActivities");

            migrationBuilder.DropTable(
                name: "UserChatStates");

            migrationBuilder.DropTable(
                name: "UserHiddenConversations");

            migrationBuilder.DropTable(
                name: "VideoComments");

            migrationBuilder.DropTable(
                name: "VideoLikes");

            migrationBuilder.DropTable(
                name: "VideoTinDangSaves");

            migrationBuilder.DropTable(
                name: "VideoViews");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "TinNhanSocials");

            migrationBuilder.DropTable(
                name: "TinNhans");

            migrationBuilder.DropTable(
                name: "TinDangs");

            migrationBuilder.DropTable(
                name: "CuocTroChuyenSocials");

            migrationBuilder.DropTable(
                name: "CuocTroChuyens");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "DanhMucs");

            migrationBuilder.DropTable(
                name: "QuanHuyens");

            migrationBuilder.DropTable(
                name: "DanhMucChas");

            migrationBuilder.DropTable(
                name: "TinhThanhs");
        }
    }
}
