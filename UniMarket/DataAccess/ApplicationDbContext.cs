using System.ComponentModel;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion; // Cần thiết cho DateTimeOffsetConverter
using UniMarket.Models;

namespace UniMarket.DataAccess
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // ==========================================================
        // 💾 DANH SÁCH ĐẦY ĐỦ CÁC DBSET
        // ==========================================================
        public DbSet<ApplicationUser> ApplicationUsers { get; set; }
        public DbSet<TinDang> TinDangs { get; set; }
        public DbSet<DanhMuc> DanhMucs { get; set; }
        public DbSet<AnhTinDang> AnhTinDangs { get; set; }
        public DbSet<TinhThanh> TinhThanhs { get; set; }
        public DbSet<QuanHuyen> QuanHuyens { get; set; }
        public DbSet<DanhMucCha> DanhMucChas { get; set; }

        public DbSet<CuocTroChuyen> CuocTroChuyens { get; set; }
        public DbSet<TinNhan> TinNhans { get; set; }
        public DbSet<TinNhanXoa> TinNhanXoas { get; set; }
        public DbSet<NguoiThamGia> NguoiThamGias { get; set; }
        public DbSet<BlockedUser> BlockedUsers { get; set; }

        public DbSet<VideoLike> VideoLikes { get; set; }
        public DbSet<VideoComment> VideoComments { get; set; }

        public DbSet<SearchHistory> SearchHistories { get; set; }

        // 💬 Social Chat
        public DbSet<CuocTroChuyenSocial> CuocTroChuyenSocials { get; set; }
        public DbSet<TinNhanSocial> TinNhanSocials { get; set; }
        public DbSet<NguoiThamGiaSocial> NguoiThamGiaSocials { get; set; }
        public DbSet<UserActivity> UserActivities { get; set; }

        // ✨ Thêm bảng mới cho "xóa tin nhắn 1 phía"
        public DbSet<DeletedMessageForUser> DeletedMessagesForUsers { get; set; }

        // ❤️ Favorite + Save
        public DbSet<TinDangYeuThich> TinDangYeuThichs { get; set; }
        public DbSet<VideoTinDangSave> VideoTinDangSaves { get; set; }

        // Reports (user-submitted reports for posts/videos)
        public DbSet<Report> Reports { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<UserNotification> UserNotifications { get; set; }

        // 🗨️ Chat state, Follow, Share, Hidden Chat
        public DbSet<UserChatState> UserChatStates { get; set; }
        public DbSet<UserSocialLink> UserSocialLinks { get; set; }
        public DbSet<Share> Shares { get; set; }
        public DbSet<Follow> Follows { get; set; }
        public DbSet<UserHiddenConversation> UserHiddenConversations { get; set; }
        public DbSet<QuickMessage> QuickMessages { get; set; }
        public DbSet<UserDevice> UserDevices { get; set; }

        // 🆕 Recommendation System (MỚI THÊM)
        public DbSet<UserAffinity> UserAffinities { get; set; }

        // ==========================================================
        // 🔧 CONFIGURATION
        // ==========================================================
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // =================================================================
            // 🛑 CẤU HÌNH GLOBAL QUERY FILTER (SOFT DELETE)
            // =================================================================

            // 1. Tự động ẩn User đã xóa (IsDeleted = true) khỏi mọi câu truy vấn
            modelBuilder.Entity<ApplicationUser>().HasQueryFilter(u => !u.IsDeleted);

            // 2. Tự động ẩn Tin Đăng đã xóa (IsDeleted = true) khỏi mọi câu truy vấn
            modelBuilder.Entity<TinDang>().HasQueryFilter(t => !t.IsDeleted);

            // =================================================================
            // 🆕 CẤU HÌNH USER AFFINITY (HỆ THỐNG GỢI Ý)
            // =================================================================
            modelBuilder.Entity<UserAffinity>(entity =>
            {
                entity.HasKey(e => e.Id);

                // Index Composite để tìm kiếm nhanh mối quan hệ giữa 2 người
                entity.HasIndex(e => new { e.SourceUserId, e.TargetUserId }).IsUnique();

                // Quan hệ 1: SourceUser (Người thực hiện hành động)
                // Nếu User bị xóa -> Xóa dữ liệu gợi ý của họ (Cascade OK)
                entity.HasOne(e => e.SourceUser)
                      .WithMany()
                      .HasForeignKey(e => e.SourceUserId)
                      .OnDelete(DeleteBehavior.Cascade);

                // Quan hệ 2: TargetUser (Người được tương tác)
                // Nếu User bị xóa -> KHÔNG tự động xóa dòng này bằng SQL Cascade (Restrict)
                // Lý do: Tránh lỗi "Multiple Cascade Paths" của SQL Server
                entity.HasOne(e => e.TargetUser)
                      .WithMany()
                      .HasForeignKey(e => e.TargetUserId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // =================================================================
            // ⚙️ CẤU HÌNH QUAN HỆ & KHÓA CHÍNH CŨ
            // =================================================================

            modelBuilder.Entity<UserHiddenConversation>(entity =>
            {
                entity.HasKey(e => new { e.UserId, e.MaCuocTroChuyen });
            });

            // Cấu hình chi tiết quan hệ cho DeletedMessageForUser
            modelBuilder.Entity<DeletedMessageForUser>(entity =>
            {
                // Định nghĩa khóa chính kép (Composite Key)
                entity.HasKey(e => new { e.UserId, e.TinNhanSocialId });

                // Quan hệ với TinNhanSocial
                entity.HasOne(d => d.TinNhanSocial)
                      .WithMany(t => t.DeletedForUsers)
                      .HasForeignKey(d => d.TinNhanSocialId)
                      .OnDelete(DeleteBehavior.Restrict); // Ngăn chặn xóa dây chuyền

                // Quan hệ với User (giữ nguyên Cascade)
                entity.HasOne(d => d.User)
                      .WithMany()
                      .HasForeignKey(d => d.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Cấu hình quan hệ tự tham chiếu cho TinNhanSocial (Reply)
            modelBuilder.Entity<TinNhanSocial>()
                .HasOne(m => m.ParentMessage)
                .WithMany()
                .HasForeignKey(m => m.ParentMessageId)
                .OnDelete(DeleteBehavior.ClientSetNull); // Tránh xóa cascade vòng lặp

            // =================================================================
            // 🕒 CẤU HÌNH CHUYỂN ĐỔI MÚI GIỜ (DateTime -> DateTimeOffset)
            // =================================================================
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                    {
                        modelBuilder.Entity(entityType.Name)
                                    .Property(property.Name)
                                    .HasConversion(typeof(DateTimeOffsetConverter));
                    }
                }
            }

            // =================================================================
            // 🔗 CÁC QUAN HỆ KHÁC
            // =================================================================
            modelBuilder.Entity<VideoLike>()
                .HasOne(v => v.TinDang)
                .WithMany()
                .HasForeignKey(v => v.MaTinDang)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<VideoComment>()
                .HasOne(v => v.TinDang)
                .WithMany()
                .HasForeignKey(v => v.MaTinDang)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<VideoComment>()
                .HasOne(vc => vc.ParentComment)
                .WithMany(vc => vc.Replies)
                .HasForeignKey(vc => vc.ParentCommentId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TinDangYeuThich>()
                .HasKey(t => t.MaYeuThich);

            modelBuilder.Entity<TinDangYeuThich>()
                .HasOne(t => t.TinDang)
                .WithMany(t => t.TinDangYeuThichs)
                .HasForeignKey(t => t.MaTinDang)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TinDangYeuThich>()
                .HasOne(t => t.NguoiDung)
                .WithMany()
                .HasForeignKey(t => t.MaNguoiDung)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<VideoTinDangSave>(entity =>
            {
                entity.HasKey(e => e.MaVideoSave);
                entity.HasOne(e => e.NguoiDung)
                      .WithMany()
                      .HasForeignKey(e => e.MaNguoiDung)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.TinDang)
                      .WithMany()
                      .HasForeignKey(e => e.MaTinDang)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // Reports configuration
            modelBuilder.Entity<Report>(entity =>
            {
                entity.HasKey(r => r.MaBaoCao);

                entity.HasOne(r => r.Reporter)
                    .WithMany()
                    .HasForeignKey(r => r.ReporterId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(r => new { r.TargetType, r.TargetId });
                entity.HasIndex(r => r.ReporterId);
                entity.HasIndex(r => r.CreatedAt);

                entity.Property(r => r.Reason).HasMaxLength(200);
                entity.Property(r => r.Details).HasMaxLength(2000);
            });

            // Notifications
            modelBuilder.Entity<Notification>(entity =>
            {
                entity.HasKey(n => n.Id);
                entity.Property(n => n.UserId).IsRequired().HasMaxLength(450);
                entity.Property(n => n.Title).IsRequired().HasMaxLength(200);
                entity.Property(n => n.Message).HasMaxLength(2000);
                entity.Property(n => n.Url).HasMaxLength(500);
                entity.Property(n => n.IsRead).HasDefaultValue(false);
                entity.HasIndex(n => n.UserId);
                entity.HasIndex(n => n.CreatedAt);
            });

            modelBuilder.Entity<UserChatState>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.UserId, e.ChatId })
                      .IsUnique()
                      .HasDatabaseName("IX_UserChatState_UserId_ChatId");
                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Chat)
                      .WithMany()
                      .HasForeignKey(e => e.ChatId)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.Property(e => e.UserId).IsRequired().HasMaxLength(450);
                entity.Property(e => e.ChatId).IsRequired();
            });

            // Cấu hình cho các trường decimal
            modelBuilder.Entity<CuocTroChuyen>()
                .Property(c => c.GiaTinDang)
                .HasColumnType("decimal(18,2)");

            modelBuilder.Entity<TinDang>()
                .Property(t => t.Gia)
                .HasColumnType("decimal(18,2)");

            modelBuilder.Entity<TinNhanXoa>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.UserId, e.MaTinNhan })
                      .IsUnique()
                      .HasDatabaseName("IX_TinNhanXoa_UserId_MaTinNhan");
                entity.HasOne(e => e.TinNhan)
                      .WithMany(t => t.MessageDeletions)
                      .HasForeignKey(e => e.MaTinNhan)
                      .OnDelete(DeleteBehavior.NoAction);
                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.NoAction);
                entity.Property(e => e.ThoiGianXoa)
                      .IsRequired()
                      .HasDefaultValueSql("GETUTCDATE()");
            });

            modelBuilder.Entity<Follow>(entity =>
            {
                entity.HasKey(f => f.Id);
                entity.HasOne(f => f.Follower)
                      .WithMany()
                      .HasForeignKey(f => f.FollowerId)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(f => f.Following)
                      .WithMany()
                      .HasForeignKey(f => f.FollowingId)
                      .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}