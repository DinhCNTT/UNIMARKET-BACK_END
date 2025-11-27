using Microsoft.ML.Data;

namespace UniMarket.Models.ML
{
    public class VideoRating
    {
        [LoadColumn(0)]
        public string UserId { get; set; } // Để nguyên string, AI tự map

        [LoadColumn(1)]
        public float VideoId { get; set; } // Float để AI xử lý (KeyType)

        [LoadColumn(2)]
        public float Label { get; set; } // Điểm số
    }

    public class VideoPrediction
    {
        public float Label { get; set; } // Kết quả dự đoán
        public float Score { get; set; }
    }
}