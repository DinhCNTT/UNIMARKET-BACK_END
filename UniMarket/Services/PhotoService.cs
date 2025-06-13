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

        // Xóa ảnh/video theo publicId - Updated method with better error handling
        public async Task<DeletionResult> DeletePhotoAsync(string publicId, ResourceType resourceType = ResourceType.Image)
        {
            try
            {
                var deletionParams = new DeletionParams(publicId)
                {
                    ResourceType = resourceType
                };

                var result = await _cloudinary.DestroyAsync(deletionParams);

                // Log result for debugging
                Console.WriteLine($"Cloudinary deletion result for {publicId}: {result.Result}");

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting from Cloudinary: {ex.Message}");

                // Return a failed result instead of throwing
                return new DeletionResult
                {
                    Result = "error",
                    Error = new Error { Message = ex.Message }
                };
            }
        }

        // 🆕 Method xóa ảnh/video từ thư mục doan-chat
        public async Task<DeletionResult> DeleteChatMediaAsync(string publicId, ResourceType resourceType = ResourceType.Image)
        {
            try
            {
                // Đảm bảo publicId bao gồm folder path "doan-chat/"
                if (!publicId.StartsWith("doan-chat/"))
                {
                    publicId = $"doan-chat/{publicId}";
                }

                var deletionParams = new DeletionParams(publicId)
                {
                    ResourceType = resourceType
                };

                var result = await _cloudinary.DestroyAsync(deletionParams);

                // Log result for debugging
                Console.WriteLine($"Cloudinary chat media deletion result for {publicId}: {result.Result}");

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting chat media from Cloudinary: {ex.Message}");

                // Return a failed result instead of throwing
                return new DeletionResult
                {
                    Result = "error",
                    Error = new Error { Message = ex.Message }
                };
            }
        }

        // 🆕 Method xóa theo URL (tự động detect folder và publicId)
        public async Task<bool> DeleteMediaByUrlAsync(string mediaUrl, ResourceType resourceType = ResourceType.Image)
        {
            try
            {
                var publicId = ExtractPublicIdFromUrl(mediaUrl);
                if (string.IsNullOrEmpty(publicId))
                {
                    Console.WriteLine($"Could not extract publicId from URL: {mediaUrl}");
                    return false;
                }

                DeletionResult result;

                // Nếu là từ thư mục doan-chat, dùng method chuyên biệt
                if (publicId.StartsWith("doan-chat/"))
                {
                    result = await DeleteChatMediaAsync(publicId, resourceType);
                }
                else
                {
                    result = await DeletePhotoAsync(publicId, resourceType);
                }

                return result.Result == "ok";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting media by URL {mediaUrl}: {ex.Message}");
                return false;
            }
        }

        // Helper method để extract publicId từ Cloudinary URL
        private string ExtractPublicIdFromUrl(string cloudinaryUrl)
        {
            try
            {
                if (string.IsNullOrEmpty(cloudinaryUrl))
                    return null;

                // Cloudinary URL format: https://res.cloudinary.com/{cloud_name}/{resource_type}/upload/v{version}/{folder}/{public_id}.{format}
                var uri = new Uri(cloudinaryUrl);
                var path = uri.AbsolutePath;

                // Remove file extension
                var lastDotIndex = path.LastIndexOf('.');
                if (lastDotIndex > 0)
                {
                    path = path.Substring(0, lastDotIndex);
                }

                // Extract public_id (includes folder path)
                var uploadIndex = path.IndexOf("/upload/");
                if (uploadIndex >= 0)
                {
                    var afterUpload = path.Substring(uploadIndex + "/upload/".Length);
                    // Remove version if exists (v1234567890/)
                    var versionPattern = @"^v\d+/";
                    var match = System.Text.RegularExpressions.Regex.Match(afterUpload, versionPattern);
                    if (match.Success)
                    {
                        afterUpload = afterUpload.Substring(match.Length);
                    }
                    return afterUpload;
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting publicId from URL {cloudinaryUrl}: {ex.Message}");
                return null;
            }
        }
    }
}