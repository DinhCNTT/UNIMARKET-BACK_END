using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using UniMarket.Models;

namespace UniMarket.Services
{
    public class PhotoService
    {
        private readonly Cloudinary _cloudinary;

        // Constructor nhận cấu hình Cloudinary từ IOptions<CloudinarySettings>
        public PhotoService(IOptions<CloudinarySettings> config)
        {
            var acc = new Account(
                config.Value.CloudName,
                config.Value.ApiKey,
                config.Value.ApiSecret
            );
            _cloudinary = new Cloudinary(acc); // Sử dụng một đối tượng Cloudinary duy nhất
        }

        // Upload file lên Cloudinary vào thư mục chỉ định
        public async Task<ImageUploadResult> UploadFileToCloudinaryAsync(IFormFile file, string folder)
        {

            var uploadResult = new ImageUploadResult();

            if (file != null && file.Length > 0)
            {
                try
                {
                    var uploadParams = new ImageUploadParams()
                    {
                        File = new FileDescription(file.FileName, file.OpenReadStream()),
                        Folder = folder  // Đặt thư mục
                    };

                    uploadResult = await _cloudinary.UploadAsync(uploadParams);
                }
                catch (Exception ex)
                {
                    throw new Exception("Lỗi upload lên Cloudinary: " + ex.Message);
                }
            }

            return uploadResult;
        }

        // Upload ảnh cho chat
        public async Task<ImageUploadResult> UploadChatImageAsync(IFormFile file)
        {
            return await UploadFileToCloudinaryAsync(file, "doan-chat"); // Sử dụng thư mục "doan-chat" cho ảnh chat
        }

        // Upload ảnh cho tin đăng
        public async Task<ImageUploadResult> UploadPhotoAsync(IFormFile file)
        {
            return await UploadFileToCloudinaryAsync(file, "tin-dang"); // Sử dụng thư mục "tin-dang" cho ảnh tin đăng
        }

        // Upload video cho tin đăng
        public async Task<VideoUploadResult> UploadVideoAsync(IFormFile file)
        {
            var result = new VideoUploadResult();

            if (file != null && file.Length > 0)
            {
                using var stream = file.OpenReadStream();
                var uploadParams = new VideoUploadParams
                {
                    File = new FileDescription(file.FileName, stream),
                    Folder = "tin-dang"  // Đặt thư mục cho video tin đăng
                };
                result = await _cloudinary.UploadAsync(uploadParams);

                if (result.Error != null)
                {
                    // Log lỗi chi tiết
                    Console.WriteLine($"Upload Error (video): {result.Error.Message}");
                }
                else
                {
                    Console.WriteLine($"Upload Success (video): {result.SecureUrl}");
                }
            }
            else
            {
                Console.WriteLine("File length is 0.");
            }

            return result;
        }

        // Xóa ảnh/video theo publicId
        public async Task<DeletionResult> DeletePhotoAsync(string publicId, ResourceType resourceType = ResourceType.Image)
        {
            var deletionParams = new DeletionParams(publicId)
            {
                ResourceType = resourceType
            };
            var result = await _cloudinary.DestroyAsync(deletionParams);
            return result;
        }
    }
}
