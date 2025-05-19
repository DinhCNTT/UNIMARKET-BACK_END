using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using UniMarket.Models;

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
                    Console.WriteLine($"Upload Error: {result.Error.Message}");
                }
                else
                {
                    Console.WriteLine($"Upload Success: {result.SecureUrl}");
                }
            }
            else
            {
                Console.WriteLine("File length is 0.");
            }

            return result;
        }

        // PhotoService.cs
        public async Task<DeletionResult> DeletePhotoAsync(string publicId)
        {
            var deletionParams = new DeletionParams(publicId);
            var result = await _cloudinary.DestroyAsync(deletionParams);
            return result;
        }

    }
}
