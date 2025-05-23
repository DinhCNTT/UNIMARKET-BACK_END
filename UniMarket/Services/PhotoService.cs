using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using UniMarket.Models;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;

namespace UniMarket.Services
{
    public class PhotoService
    {
        private readonly Cloudinary _cloudinary;

        public PhotoService(IOptions<CloudinarySettings> config)
        {
            var acc = new Account(
                config.Value.CloudName,
                config.Value.ApiKey,
                config.Value.ApiSecret
            );

            _cloudinary = new Cloudinary(acc);
        }

        // Upload ảnh (image)
        public async Task<ImageUploadResult> UploadPhotoAsync(IFormFile file)
        {
            var result = new ImageUploadResult();

            if (file.Length > 0)
            {
                using var stream = file.OpenReadStream();
                var uploadParams = new ImageUploadParams
                {
                    File = new FileDescription(file.FileName, stream),
                    Folder = "tin-dang",
                    Transformation = new Transformation().Height(800).Width(800).Crop("limit")
                };
                result = await _cloudinary.UploadAsync(uploadParams);

                if (result.Error != null)
                {
                    // Log lỗi chi tiết
                    Console.WriteLine($"Upload Error (image): {result.Error.Message}");
                }
                else
                {
                    Console.WriteLine($"Upload Success (image): {result.SecureUrl}");
                }
            }
            else
            {
                Console.WriteLine("File length is 0.");
            }

            return result;
        }

        // Upload video (video)
        public async Task<VideoUploadResult> UploadVideoAsync(IFormFile file)
        {
            var result = new VideoUploadResult();

            if (file.Length > 0)
            {
                using var stream = file.OpenReadStream();
                var uploadParams = new VideoUploadParams
                {
                    File = new FileDescription(file.FileName, stream),
                    Folder = "tin-dang"
                    // Bạn có thể thêm Transformation cho video nếu muốn
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
