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
            _cloudinary = new Cloudinary(acc);
        }

        // 🔄 Hàm core upload ảnh
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
                        Folder = folder
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

        // ✅ Upload avatar vào thư mục "avatars"
        public async Task<ImageUploadResult> UploadAvatarAsync(IFormFile file)
        {
            return await UploadFileToCloudinaryAsync(file, "avatars");
        }

        // ✅ Upload ảnh cho chat
        public async Task<ImageUploadResult> UploadChatImageAsync(IFormFile file)
        {
            return await UploadFileToCloudinaryAsync(file, "doan-chat");
        }

        // ✅ Upload ảnh cho tin đăng
        public async Task<ImageUploadResult> UploadPhotoAsync(IFormFile file)
        {
            return await UploadFileToCloudinaryAsync(file, "tin-dang");
        }

        // ✅ Upload video tin đăng
        public async Task<VideoUploadResult> UploadVideoAsync(IFormFile file)
        {
            var result = new VideoUploadResult();

            if (file != null && file.Length > 0)
            {
                using var stream = file.OpenReadStream();
                var uploadParams = new VideoUploadParams
                {
                    File = new FileDescription(file.FileName, stream),
                    Folder = "tin-dang",
                    EagerTransforms = new List<Transformation>
                    {
                        new Transformation().Width(720).Height(480).Crop("limit").FetchFormat("mp4")
                    },
                    EagerAsync = true
                };

                result = await _cloudinary.UploadAsync(uploadParams);

                if (result.Error != null)
                    Console.WriteLine($"Upload Error (video): {result.Error.Message}");
                else
                {
                    Console.WriteLine($"Upload Success (video): {result.SecureUrl}");
                    Console.WriteLine($"Dung lượng: {Math.Round(result.Bytes / 1024.0 / 1024.0, 2)} MB");
                }
            }

            return result;
        }

        // ✅ Xóa ảnh/video theo publicId
        public async Task<DeletionResult> DeletePhotoAsync(string publicId, ResourceType resourceType = ResourceType.Image)
        {
            try
            {
                var deletionParams = new DeletionParams(publicId)
                {
                    ResourceType = resourceType
                };

                var result = await _cloudinary.DestroyAsync(deletionParams);
                Console.WriteLine($"Cloudinary deletion result for {publicId}: {result.Result}");
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting from Cloudinary: {ex.Message}");
                return new DeletionResult
                {
                    Result = "error",
                    Error = new Error { Message = ex.Message }
                };
            }
        }

        // ✅ Xóa media chat
        public async Task<DeletionResult> DeleteChatMediaAsync(string publicId, ResourceType resourceType = ResourceType.Image)
        {
            try
            {
                if (!publicId.StartsWith("doan-chat/"))
                    publicId = $"doan-chat/{publicId}";

                var deletionParams = new DeletionParams(publicId)
                {
                    ResourceType = resourceType
                };

                var result = await _cloudinary.DestroyAsync(deletionParams);
                Console.WriteLine($"Cloudinary chat media deletion result for {publicId}: {result.Result}");
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting chat media from Cloudinary: {ex.Message}");
                return new DeletionResult
                {
                    Result = "error",
                    Error = new Error { Message = ex.Message }
                };
            }
        }

        // ✅ Xóa theo URL
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

                if (publicId.StartsWith("doan-chat/"))
                    result = await DeleteChatMediaAsync(publicId, resourceType);
                else
                    result = await DeletePhotoAsync(publicId, resourceType);

                return result.Result == "ok";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting media by URL {mediaUrl}: {ex.Message}");
                return false;
            }
        }

        // ✅ Extract publicId từ Cloudinary URL
        private string ExtractPublicIdFromUrl(string cloudinaryUrl)
        {
            try
            {
                if (string.IsNullOrEmpty(cloudinaryUrl))
                    return null;

                var uri = new Uri(cloudinaryUrl);
                var path = uri.AbsolutePath;

                var lastDotIndex = path.LastIndexOf('.');
                if (lastDotIndex > 0)
                    path = path.Substring(0, lastDotIndex);

                var uploadIndex = path.IndexOf("/upload/");
                if (uploadIndex >= 0)
                {
                    var afterUpload = path.Substring(uploadIndex + "/upload/".Length);
                    var versionPattern = @"^v\d+/";
                    var match = System.Text.RegularExpressions.Regex.Match(afterUpload, versionPattern);
                    if (match.Success)
                        afterUpload = afterUpload.Substring(match.Length);

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
