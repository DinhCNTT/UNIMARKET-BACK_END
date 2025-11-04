// ✨ Thay YourProjectName bằng namespace thực tế của dự án bạn
namespace YourProjectName.Helpers
{
    // Một class "static" chứa các hàm có thể được gọi trực tiếp mà không cần tạo đối tượng
    public static class UrlHelpers
    {
        // Hàm kiểm tra một URL có phải là video hay không dựa trên phần mở rộng
        public static bool IsVideoUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;

            // Danh sách các đuôi file video phổ biến
            var videoExtensions = new[] { ".mp4", ".mov", ".avi", ".wmv", ".mkv" };

            // Trả về true nếu url kết thúc bằng một trong các đuôi file trên
            return videoExtensions.Any(ext => url.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        }

        // Trong tương lai, bạn có thể thêm các hàm helper khác vào đây
        // public static bool IsImageUrl(string url) { ... }
    }
}