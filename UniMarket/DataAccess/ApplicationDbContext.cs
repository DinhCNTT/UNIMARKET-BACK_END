using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using UniMarket.Models;

namespace UniMarket.DataAccess
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }
        // 🔥 Thêm DbSet để chắc chắn ApplicationUser được ánh xạ vào database
        public DbSet<ApplicationUser> ApplicationUsers { get; set; }
        public DbSet<TinDang> TinDangs { get; set; }
        public DbSet<DanhMuc> DanhMucs { get; set; }
        public DbSet<AnhTinDang> AnhTinDangs { get; set; }
        public DbSet<TinhThanh> TinhThanhs { get; set; }
        public DbSet<QuanHuyen> QuanHuyens { get; set; }
        public DbSet<DanhMucCha> DanhMucChas { get; set; } // ✅ Kiểm tra có DbSet<DanhMuc> không
        public DbSet<CuocTroChuyen> CuocTroChuyens { get; set; }
        public DbSet<TinNhan> TinNhans { get; set; }
        public DbSet<TinNhanDaXoa> TinNhanDaXoas { get; set; }
        public DbSet<NguoiThamGia> NguoiThamGias { get; set; }
        public DbSet<BlockedUser> BlockedUsers { get; set; }
        public DbSet<VideoLike> VideoLikes { get; set; }
        public DbSet<VideoComment> VideoComments { get; set; }
        public DbSet<VideoView> VideoViews { get; set; }
        public DbSet<SearchHistory> SearchHistories { get; set; }
        public DbSet<TinDangYeuThich> TinDangYeuThichs { get; set; }
        public DbSet<VideoTinDangSave> VideoTinDangSaves { get; set; }
        public DbSet<UserChatState> UserChatStates { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Ngăn nhiều cascade từ TinDang
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

            modelBuilder.Entity<VideoView>()
                .HasOne(v => v.TinDang)
                .WithMany()
                .HasForeignKey(v => v.MaTinDang)
                .OnDelete(DeleteBehavior.Restrict);

            // ✅ Cấu hình quan hệ bình luận cha - con (replies)
            modelBuilder.Entity<VideoComment>()
                .HasOne(vc => vc.ParentComment)
                .WithMany(vc => vc.Replies)
                .HasForeignKey(vc => vc.ParentCommentId)
                .OnDelete(DeleteBehavior.Restrict);

            // ✅ Cấu hình TinDangYeuThich
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
                    .OnDelete(DeleteBehavior.Restrict); // Tắt cascade delete

                entity.HasOne(e => e.TinDang)
                    .WithMany()
                    .HasForeignKey(e => e.MaTinDang)
                    .OnDelete(DeleteBehavior.Restrict); // Tắt cascade delete
            });
            // Cấu hình UserChatState
            modelBuilder.Entity<UserChatState>(entity =>
            {
                entity.HasKey(e => e.Id);

                // Tạo unique constraint cho UserId + ChatId
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

                entity.Property(e => e.UserId)
                      .IsRequired()
                      .HasMaxLength(450);

                entity.Property(e => e.ChatId)
                      .IsRequired();
            });

        }


    }
}