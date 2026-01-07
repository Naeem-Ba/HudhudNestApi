using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Configuration; // مهم جداً لقراءة الإعدادات
using Microsoft.AspNetCore.Http;

namespace WohnungenApi.Services
{
    public class PhotoService : IPhotoService
    {
        private readonly Cloudinary _cloudinary;

        // قمنا بتغيير IOptions إلى IConfiguration لقراءة الرابط الموحد مباشرة
        public PhotoService(IConfiguration config)
        {
            var cloudinaryUrl = config["CLOUDINARY_URL"];

            if (string.IsNullOrEmpty(cloudinaryUrl))
            {
                throw new Exception("Cloudinary URL is missing in Environment Variables!");
            }

            // Cloudinary يستطيع التعرف على الإعدادات تلقائياً من الرابط الموحد
            _cloudinary = new Cloudinary(cloudinaryUrl);
            _cloudinary.Api.Secure = true;
        }

        public async Task<ImageUploadResult> AddPhotoAsync(IFormFile file)
        {
            var uploadResult = new ImageUploadResult();

            if (file != null && file.Length > 0)
            {
                using var stream = file.OpenReadStream();
                var uploadParams = new ImageUploadParams
                {
                    File = new FileDescription(file.FileName, stream),
                    // تحسين حجم الصورة لسرعة التحميل
                    Transformation = new Transformation().Height(800).Width(1200).Crop("limit"),
                    Folder = "wohnungen-bilder"
                };
                uploadResult = await _cloudinary.UploadAsync(uploadParams);
            }

            return uploadResult;
        }

        public async Task<DeletionResult> DeletePhotoAsync(string publicId)
        {
            var deleteParams = new DeletionParams(publicId);
            return await _cloudinary.DestroyAsync(deleteParams);
        }
    }
}