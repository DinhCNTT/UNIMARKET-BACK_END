using Microsoft.Extensions.DependencyInjection;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers;
using UniMarket.Models.ML;

namespace UniMarket.Services.Recommendation
{
    public class RecommendationEngine
    {
        private readonly MLContext _mlContext;
        private ITransformer? _model;
        private PredictionEngine<VideoRating, VideoPrediction>? _predictionEngine;

        // Sử dụng ScopeFactory để tạo scope con khi cần truy cập Database
        private readonly IServiceScopeFactory _scopeFactory;

        // Đường dẫn lưu file model
        private readonly string _modelPath;

        // Object dùng để khóa luồng (Thread-safety)
        private readonly object _lock = new object();

        public RecommendationEngine(IServiceScopeFactory scopeFactory)
        {
            // Seed = 0 để kết quả huấn luyện nhất quán qua các lần chạy
            _mlContext = new MLContext(seed: 0);
            _scopeFactory = scopeFactory;

            // Thiết lập đường dẫn lưu model tại wwwroot/AIModels/video_recommender.zip
            string contentRoot = Directory.GetCurrentDirectory();
            _modelPath = Path.Combine(contentRoot, "wwwroot", "AIModels", "video_recommender.zip");

            // Tạo thư mục nếu chưa tồn tại
            string dir = Path.GetDirectoryName(_modelPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            // Cố gắng load model cũ lên ngay khi khởi động ứng dụng
            LoadModel();
        }

        // ============================================================
        // 1. LOAD MODEL TỪ Ổ CỨNG
        // ============================================================
        private void LoadModel()
        {
            if (File.Exists(_modelPath))
            {
                try
                {
                    lock (_lock)
                    {
                        // Load model schema và data
                        _model = _mlContext.Model.Load(_modelPath, out var schema);
                        CreatePredictionEngine();
                    }
                    Console.WriteLine($"✅ [AI Engine] Model loaded successfully from: {_modelPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ [AI Engine] Failed to load model: {ex.Message}. System will wait for next training.");
                }
            }
            else
            {
                Console.WriteLine("ℹ️ [AI Engine] No existing model found. Please trigger training.");
            }
        }

        // ============================================================
        // 2. HUẤN LUYỆN (TRAINING)
        // ============================================================
        public async Task TrainModel()
        {
            Console.WriteLine("🔄 [AI Engine] Starting training process...");

            // Tạo Scope mới để lấy UserBehaviorService (vì Service này là Scoped, dùng DbContext)
            using (var scope = _scopeFactory.CreateScope())
            {
                try
                {
                    var behaviorService = scope.ServiceProvider.GetRequiredService<UserBehaviorService>();

                    // Lấy dữ liệu thô từ DB (đã được tính điểm hành vi)
                    var data = await behaviorService.GetTrainingDataAsync();

                    // Kiểm tra dữ liệu đầu vào
                    if (data == null || !data.Any())
                    {
                        Console.WriteLine("⚠️ [AI Engine] No training data available. Aborting.");
                        return;
                    }

                    Console.WriteLine($"📊 [AI Engine] Training on {data.Count} interaction records...");

                    // Chuyển đổi List sang IDataView (Định dạng của ML.NET)
                    IDataView trainingDataView = _mlContext.Data.LoadFromEnumerable(data);

                    // --- CẤU HÌNH PIPELINE ---
                    // 1. MapValueToKey: Chuyển UserId (String) -> Key (Số nội bộ)
                    // 2. MapValueToKey: Chuyển VideoId (Float) -> Key (Số nội bộ)
                    // 3. MatrixFactorization: Thuật toán gợi ý
                    var options = new MatrixFactorizationTrainer.Options
                    {
                        MatrixColumnIndexColumnName = "UserIdEncoded",
                        MatrixRowIndexColumnName = "VideoIdEncoded",
                        LabelColumnName = nameof(VideoRating.Label),

                        // Hyperparameters (Tinh chỉnh để AI học tốt hơn)
                        LossFunction = MatrixFactorizationTrainer.LossFunctionType.SquareLossOneClass,
                        Alpha = 0.01,
                        Lambda = 0.025,
                        NumberOfIterations = 100, // Tăng số vòng lặp để học kỹ hơn
                        ApproximationRank = 100   // Độ phức tạp của ma trận
                    };

                    var pipeline = _mlContext.Transforms.Conversion.MapValueToKey(
                            outputColumnName: "UserIdEncoded",
                            inputColumnName: nameof(VideoRating.UserId))
                        .Append(_mlContext.Transforms.Conversion.MapValueToKey(
                            outputColumnName: "VideoIdEncoded",
                            inputColumnName: nameof(VideoRating.VideoId))
                        )
                        .Append(_mlContext.Recommendation().Trainers.MatrixFactorization(options));

                    // --- BẮT ĐẦU TRAIN ---
                    var newModel = pipeline.Fit(trainingDataView);

                    // --- LƯU MODEL ---
                    _mlContext.Model.Save(newModel, trainingDataView.Schema, _modelPath);

                    // --- CẬP NHẬT MODEL ĐANG CHẠY (Hot Swap) ---
                    lock (_lock)
                    {
                        _model = newModel;
                        CreatePredictionEngine();
                    }

                    Console.WriteLine("✅ [AI Engine] Training Completed & Model Saved.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ [AI Engine] Error during training: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                }
            }
        }

        // Helper: Tạo engine dự đoán đơn lẻ từ Model tổng
        private void CreatePredictionEngine()
        {
            if (_model != null)
            {
                _predictionEngine = _mlContext.Model.CreatePredictionEngine<VideoRating, VideoPrediction>(_model);
            }
        }

        // ============================================================
        // 3. DỰ ĐOÁN (PREDICT)
        // ============================================================
        public float PredictScore(string userId, int videoId)
        {
            // Nếu chưa có model (lần đầu chạy chưa train), trả về 0
            if (_predictionEngine == null || _model == null) return 0;

            var input = new VideoRating
            {
                UserId = userId,
                VideoId = (float)videoId
            };

            // Sử dụng lock để đảm bảo an toàn luồng (Thread-Safe)
            // Vì PredictionEngine không hỗ trợ đa luồng đồng thời
            lock (_lock)
            {
                try
                {
                    var prediction = _predictionEngine.Predict(input);

                    // Chuẩn hóa điểm số (Tránh NaN hoặc Infinity)
                    if (float.IsNaN(prediction.Score) || float.IsInfinity(prediction.Score))
                    {
                        return 0;
                    }

                    return prediction.Score;
                }
                catch
                {
                    // Trường hợp COLD START:
                    // Nếu User mới hoặc Video mới chưa có trong Key Mapping của Model
                    // ML.NET sẽ ném lỗi (hoặc trả về KeyNotFound).
                    // Ta trả về 0 để hệ thống fallback sang thuật toán Trending.
                    return 0;
                }
            }
        }
    }
}