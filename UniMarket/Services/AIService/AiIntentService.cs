using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UniMarket.DataAccess;
using UniMarket.DTO;
using UniMarket.Models;

namespace UniMarket.Services
{
    /// <summary>
    /// AiIntentService: Chuyên trách phân tích intent từ tin nhắn người dùng.
    /// - Gọi Gemini API để phân tích intent
    /// - Xử lý JSON response (có Smart Rescue nếu JSON lỗi)
    /// - Trích xuất từ khóa thông minh (fallback)
    /// - Ánh xạ Category sang ID
    /// </summary>
    public class AiIntentService
    {
        private readonly AiClient _aiClient;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AiIntentService> _logger;

        // Cache & Circuit Breaker
        private static Dictionary<string, int>? _categoryCache;
        private static DateTime _categoryCacheTime = DateTime.MinValue;
        private const int CATEGORY_CACHE_MINUTES = 30;
        
        private static int _geminiFailureCount = 0;
        private static DateTime _lastGeminiFailureTime = DateTime.MinValue;
        private const int GEMINI_FAILURE_THRESHOLD = 3;
        private const int GEMINI_CIRCUIT_BREAKER_MINUTES = 5;

        public string? LastRawResponse { get; private set; }

        public AiIntentService(AiClient aiClient, ApplicationDbContext context, ILogger<AiIntentService> logger)
        {
            _aiClient = aiClient;
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Phân tích intent từ tin nhắn người dùng.
        /// Sử dụng Gemini để phân tích, fallback về keyword extraction nếu lỗi.
        /// </summary>
        public async Task<AiIntentResult> AnalyzeIntentAsync(string message, List<AiChatMessageDto>? history)
        {
            // 1. Chuẩn bị fallback (Dự phòng cơ bản)
            var (fallbackKeywords, fallbackCategoryId) = await ExtractSmartKeywordsWithCategory(message);
            bool fbSearch = (fallbackKeywords != null && fallbackKeywords.Length > 0) || fallbackCategoryId.HasValue;

            var fallbackResult = new AiIntentResult
            {
                ShouldSearch = fbSearch,
                UserReply = fbSearch ? "Dạ, em đang tìm ngay đây ạ." : "Dạ, em nghe đây.",
                Keywords = fallbackKeywords,
                CategoryId = fallbackCategoryId,
                Confidence = 0.5m
            };

            // 2. Check Circuit Breaker
            if (_geminiFailureCount >= GEMINI_FAILURE_THRESHOLD)
            {
                var timeSinceLastFailure = DateTime.UtcNow.Subtract(_lastGeminiFailureTime).TotalMinutes;
                if (timeSinceLastFailure < GEMINI_CIRCUIT_BREAKER_MINUTES)
                {
                    _logger.LogWarning("[AiIntent] 🔌 CIRCUIT BREAKER ACTIVE: Gemini failed {count} times - using fallback", _geminiFailureCount);
                    return fallbackResult;
                }
                _geminiFailureCount = 0;
                _logger.LogInformation("[AiIntent] 🔌 CIRCUIT BREAKER: Reset (time elapsed)");
            }

            // 3. Gọi Gemini API
            try
            {
                var historyText = history != null && history.Count > 0 
                    ? string.Join("\n", history.TakeLast(6).Select(h => 
                    {
                        var content = h.Content;
                        if (content.StartsWith("{") || content.StartsWith("["))
                        {
                            try
                            {
                                using (var doc = JsonDocument.Parse(content))
                                {
                                    if (doc.RootElement.TryGetProperty("replyText", out var reply))
                                        content = reply.GetString() ?? content;
                                }
                            }
                            catch { /* Ignore JSON parse errors in history */ }
                        }
                        if (content.Length > 150) content = content.Substring(0, 150) + "...";
                        return $"{h.Role}: {content}";
                    })) 
                    : "";

                var prompt = $@"ROLE: Uni.AI shopping assistant (Vietnamese).
TASK: Analyze user message. Decide if it is SEARCH or just CHIT-CHAT.

Message: ""{message}""
History: {(string.IsNullOrEmpty(historyText) ? "None" : historyText)}

RULES:
1. **CHIT-CHAT / GREETING**: If user says 'hi', 'hello', 'who are you', 'thank you', 'bye', 'good morning', asks about you, or general questions
   -> Set ShouldSearch: false, reply directly with friendly Vietnamese greeting in UserReply.
   -> Example: {{ ""ShouldSearch"": false, ""UserReply"": ""Dạ chào bác! Em là trợ lý ảo Uni.AI, bác cần tìm mua gì cứ bảo em nha."", ... }}

2. **SEARCH**: If user mentions a product, category, brand, or buying intent
   -> Set ShouldSearch: true, extract Keywords/Attributes.
   -> Set UserReply to generic like ""Dạ để em tìm giúp bác."" (system generates specific reply later).

Return ONLY valid JSON (no markdown, no explanation):
{{
  ""ShouldSearch"": true,
  ""UserReply"": ""Dạ em tìm giúp bác."",
  ""Keywords"": [""keyword""],
  ""CategoryKeyword"": ""điện thoại"",
  ""Brand"": null,
  ""Model"": null,
  ""Color"": null,
  ""Storage"": null,
  ""Warranty"": null,
  ""Origin"": null,
  ""Condition"": null,
  ""MinPrice"": null,
  ""MaxPrice"": null,
  ""RequireVideo"": false,
  ""CategoryId"": null,
  ""SortBy"": ""recent"",
  ""FilterByHot"": false,
  ""FilterBySeller"": false,
  ""ClarifyingQuestion"": null,
  ""Confidence"": 0.8
}}

IMPORTANT:
- Keep UserReply under 15 words
- For CHIT-CHAT: Use friendly Vietnamese (Dạ chào bác, em là Uni.AI, etc.)
- For SEARCH: Reply can be generic
- Extract SPECIFIC attributes only if mentioned (Brand, Model, Color, Storage, Warranty, Origin)
- Return ONLY raw JSON, no code blocks
- Condition: only 'Moi' (new) or 'DaSuDung' (used)
- SortBy: 'recent', 'price_asc', 'price_desc', 'views_desc'
";

                _logger.LogInformation("[AiIntent] Calling Gemini with prompt for message: {msg}", message);
                
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));
                string? raw = null;
                
                try
                {
                    raw = await _aiClient.SendPromptAsync(prompt);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("[AiIntent] ⏱️ TIMEOUT: Gemini API exceeded 30 seconds - using fallback");
                    _geminiFailureCount++;
                    _lastGeminiFailureTime = DateTime.UtcNow;
                    return fallbackResult;
                }
                
                if (string.IsNullOrWhiteSpace(raw))
                {
                    _logger.LogWarning("[AiIntent] Gemini returned empty -> Using fallback.");
                    _geminiFailureCount++;
                    _lastGeminiFailureTime = DateTime.UtcNow;
                    return fallbackResult;
                }

                LastRawResponse = raw;
                _geminiFailureCount = 0; // ✅ Reset counter on success
                _logger.LogInformation("[AiIntent] Gemini responded: {rawResp}", raw.Substring(0, Math.Min(200, raw.Length)) + "...");

                // 4. Parse JSON (kèm cơ chế Cứu Hộ)
                var jsonStart = raw.IndexOf('{');
                var jsonEnd = raw.LastIndexOf('}');
                
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    var cleanJson = raw.Substring(jsonStart, jsonEnd - jsonStart + 1);
                    try 
                    {
                        var options = new JsonSerializerOptions 
                        { 
                            PropertyNameCaseInsensitive = true, 
                            ReadCommentHandling = JsonCommentHandling.Skip, 
                            AllowTrailingCommas = true,
                            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                        };
                        var result = JsonSerializer.Deserialize<AiIntentResult>(cleanJson, options);

                        if (result != null)
                        {
                            // ✅ CHẰN: Nếu AI bảo tìm mà không đưa từ khóa/category, chuyển về chit-chat
                            if (result.ShouldSearch && 
                                (result.Keywords == null || result.Keywords.Length == 0) && 
                                string.IsNullOrEmpty(result.CategoryKeyword) &&
                                !result.CategoryId.HasValue)
                            {
                                _logger.LogWarning("[AiIntent] ⚠️ AI said ShouldSearch but no keywords/category provided - Converting to CHIT-CHAT");
                                result.ShouldSearch = false;
                                result.UserReply = "Dạ em hiểu rồi, bác cần tìm sản phẩm gì thì cứ bảo em nha.";
                            }
                            
                            _logger.LogInformation("[AiIntent] ✅ Gemini parsed successfully: ShouldSearch={search}, Keywords={kw}, Brand={brand}, CategoryKeyword={cat}", 
                                result.ShouldSearch, 
                                string.Join(",", result.Keywords ?? Array.Empty<string>()), 
                                result.Brand, 
                                result.CategoryKeyword);
                            
                            // --- Map CategoryKeyword sang ID ---
                            await MapCategoryIdAsync(result, fallbackCategoryId);
                            
                            // --- Query Expansion: Gộp từ khóa fallback nếu AI tìm quá ít ---
                            if (result.ShouldSearch && fallbackKeywords != null && fallbackKeywords.Length > 0)
                            {
                                var currentKw = result.Keywords?.ToList() ?? new List<string>();
                                if (currentKw.Count <= 1)
                                {
                                    result.Keywords = currentKw.Union(fallbackKeywords, StringComparer.OrdinalIgnoreCase).ToArray();
                                    _logger.LogInformation("[AiIntent] Query Expansion: Added fallback keywords: {kw}", string.Join(",", fallbackKeywords));
                                }
                            }

                            return result;
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError("[AiIntent] JSON Parse Error: {msg}. Trying Regex Rescue.", ex.Message);
                        
                        // ✅ CƠ CHẾ CỨU HỘ: Nếu JSON lỗi, dùng Regex để móc UserReply ra
                        // Giúp Bot vẫn trả lời được câu "Dạ em là..." thay vì fallback về "Dạ em nghe đây"
                        var matchReply = Regex.Match(cleanJson, "\"UserReply\"\\s*:\\s*\"([^\"]+)\"");
                        if (matchReply.Success)
                        {
                            var rescuedReply = matchReply.Groups[1].Value;
                            _logger.LogInformation("[AiIntent] 🆘 Smart Rescue SUCCESS: Extracted UserReply: {reply}", rescuedReply);
                            
                            fallbackResult.UserReply = rescuedReply;
                            fallbackResult.ShouldSearch = false; // An toàn nhất là tắt search
                            return fallbackResult;
                        }
                        
                        _logger.LogWarning("[AiIntent] 🆘 Smart Rescue FAILED: Could not extract UserReply from JSON");
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                // Network error, API down, etc.
                _logger.LogError("[AiIntent] ❌ GEMINI API ERROR (HttpRequest): {Message} - Using fallback", ex.Message);
                _geminiFailureCount++;
                _lastGeminiFailureTime = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                // Bắt lỗi API (401, 500, Timeout...)
                _logger.LogError(ex, "[AiIntent] ❌ GEMINI API FAILED: {Message} | Falling back to keyword extraction", ex.Message);
                _geminiFailureCount++;
                _lastGeminiFailureTime = DateTime.UtcNow;
            }

            // NẾU CÓ BẤT KỲ LỖI GÌ Ở TRÊN -> TRẢ VỀ FALLBACK
            _logger.LogWarning("[AiIntent] Using fallback result with keywords: {keywords}", string.Join(", ", fallbackResult.Keywords ?? Array.Empty<string>()));
            return fallbackResult;
        }

        /// <summary>
        /// Ánh xạ CategoryKeyword (text) sang CategoryId (int) từ database hoặc fallback.
        /// </summary>
        private async Task MapCategoryIdAsync(AiIntentResult result, int? fallbackId)
        {
            if (!result.CategoryId.HasValue && fallbackId.HasValue)
            {
                result.CategoryId = fallbackId;
                _logger.LogInformation("[AiIntent] CategoryId mapped from fallback: {catId}", fallbackId);
                return;
            }

            if (!result.CategoryId.HasValue && !string.IsNullOrEmpty(result.CategoryKeyword))
            {
                // ✅ TỐI ƯU: Sử dụng cache nếu available
                if (_categoryCache == null || DateTime.UtcNow.Subtract(_categoryCacheTime).TotalMinutes > CATEGORY_CACHE_MINUTES)
                {
                    _logger.LogInformation("[AiIntent] 🔄 Refreshing category cache (expired or first load)");
                    var allCategories = await _context.DanhMucChas
                        .AsNoTracking()
                        .Select(c => new { c.MaDanhMucCha, c.TenDanhMucCha })
                        .ToListAsync();
                    
                    _categoryCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (var cat in allCategories)
                    {
                        _categoryCache[cat.TenDanhMucCha] = cat.MaDanhMucCha;
                    }
                    _categoryCacheTime = DateTime.UtcNow;
                    _logger.LogInformation("[AiIntent] ✅ Category cache loaded: {count} categories", _categoryCache.Count);
                }
                
                // Search in cache - try exact match first
                if (_categoryCache != null && _categoryCache.TryGetValue(result.CategoryKeyword, out var catId))
                {
                    result.CategoryId = catId;
                    _logger.LogInformation("[AiIntent] ✅ Mapped '{keyword}' -> CategoryId: {catId} (exact match)", result.CategoryKeyword, result.CategoryId);
                    return;
                }

                // Fallback: try partial match
                if (_categoryCache != null)
                {
                    var partialMatch = _categoryCache.FirstOrDefault(kvp => 
                        kvp.Key.Contains(result.CategoryKeyword, StringComparison.OrdinalIgnoreCase) ||
                        result.CategoryKeyword.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase)
                    );
                    
                    if (partialMatch.Value > 0)
                    {
                        result.CategoryId = partialMatch.Value;
                        _logger.LogInformation("[AiIntent] ✅ Mapped '{keyword}' -> CategoryId: {catId} (partial match: '{actualKey}')", 
                            result.CategoryKeyword, result.CategoryId, partialMatch.Key);
                        return;
                    }
                }

                // Last resort: direct database query
                _logger.LogInformation("[AiIntent] 🔎 Attempting direct database query for category '{keyword}'", result.CategoryKeyword);
                var directCategory = await _context.DanhMucChas
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => 
                        EF.Functions.Like(c.TenDanhMucCha, $"%{result.CategoryKeyword}%") ||
                        EF.Functions.Like(result.CategoryKeyword, $"%{c.TenDanhMucCha}%")
                    );
                
                if (directCategory != null)
                {
                    result.CategoryId = directCategory.MaDanhMucCha;
                    _logger.LogInformation("[AiIntent] ✅ Mapped via direct query: {catId}", result.CategoryId);
                }
                else
                {
                    _logger.LogWarning("[AiIntent] ⚠️ CategoryKeyword '{keyword}' NOT FOUND in database", result.CategoryKeyword);
                }
            }
        }

        /// <summary>
        /// Trích xuất từ khóa thông minh khi AI API lỗi.
        /// ✅ IMPORTANT: ONLY extract from CURRENT message, NOT from history to avoid keyword bleeding
        /// </summary>
        private async Task<(string[] Keywords, int? CategoryId)> ExtractSmartKeywordsWithCategory(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return (Array.Empty<string>(), null);

            var lowerMsg = message.ToLower();
            _logger.LogInformation("[AiIntent] ExtractSmartKeywordsWithCategory - Processing message: {msg}", message);
            
            // BƯỚC 1: Ánh xạ từ khóa → Danh mục cha
            var categoryKeywordMappings = new Dictionary<string, (string[] Keywords, string CategoryPattern)>
            {
                // Điện thoại
                { "điện thoại|phone|iphone|mobile|dien thoai|điện thoại di động", 
                  (new[] { "iphone", "samsung", "xiaomi", "oppo", "vivo", "nokia", "redmi", "poco", "realme", "dien thoai", "phone", "điện thoại" }, "điện thoại") },
                
                // Laptop/Computer
                { "laptop|computer|máy tính|notebook|macbook|may tinh|máy vi tính", 
                  (new[] { "dell", "asus", "hp", "lenovo", "acer", "msi", "razer", "macbook", "laptop", "asus vivobook" }, "máy tính") },
                
                // Máy giặt
                { "máy giặt|washer|giặt|may giat|máy giặt tự động", 
                  (new[] { "electrolux", "lg", "samsung", "whirlpool", "aqua", "panasonic", "may giat", "máy giặt" }, "máy giặt") },
                
                // TV
                { "tivi|tv|television|ti vi|ti-vi|truyền hình", 
                  (new[] { "samsung", "lg", "sony", "panasonic", "tcl", "toshiba", "tivi", "tv" }, "tivi") },
                
                // Tủ lạnh
                { "tủ lạnh|refrigerator|tu lanh|tủ lạnh điện", 
                  (new[] { "samsung", "lg", "electrolux", "panasonic", "aqua", "tu lanh", "tủ lạnh" }, "tủ lạnh") },
                
                // Máy ảnh
                { "máy ảnh|camera|dslr|may anh|máy chụp ảnh", 
                  (new[] { "canon", "nikon", "sony", "fujifilm", "camera", "may anh", "máy ảnh" }, "máy ảnh") },
                
                // Đồ chơi
                { "đồ chơi|toy|trò chơi|do choi", 
                  (new[] { "lego", "barbie", "gundam", "robot", "xe", "búp bê", "đồ chơi" }, "đồ chơi") },
                
                // Quần áo
                { "quần áo|áo|quần|trang phục|áo sơ mi|áo phông|quan ao|áo khoác", 
                  (new[] { "áo", "quần", "váy", "áo sơ mi", "áo phông", "áo khoác", "quần áo" }, "quần áo") },
                
                // Giày dép
                { "giày|dép|giày dép|giày thể thao|giay|dép|sandal|sneaker", 
                  (new[] { "nike", "adidas", "puma", "giày", "dép", "giày dép", "sandal" }, "giày dép") },
                
                // Ô tô
                { "ô tô|xe hơi|oto|car|auto", 
                  (new[] { "kia", "hyundai", "toyota", "honda", "ford", "mazda", "ô tô", "xe", "vios", "city", "morning" }, "ô tô") },
                
                // Xe máy
                { "xe máy|motorbike|motorcycle|moto|mô tô", 
                  (new[] { "honda", "yamaha", "suzuki", "kawasaki", "exciter", "air blade", "winner", "vision", "sh", "xe máy", "motorbike" }, "xe máy") }
            };

            // Check if message contains category keywords
            foreach (var (pattern, (brands, categoryPattern)) in categoryKeywordMappings)
            {
                var keywordPatterns = pattern.Split('|');
                foreach (var kw in keywordPatterns)
                {
                    if (lowerMsg.Contains(kw))
                    {
                        _logger.LogInformation("[AiIntent] Detected category keyword: {kw} -> Pattern: {categoryPattern}", kw, categoryPattern);
                        
                        // Tìm danh mục cha từ DB
                        var parentCategory = await _context.DanhMucChas
                            .FirstOrDefaultAsync(c => c.TenDanhMucCha.ToLower().Contains(categoryPattern.ToLower()));
                        
                        if (parentCategory != null)
                        {
                            _logger.LogInformation("[AiIntent] Found category: {catName} (ID: {catId})", parentCategory.TenDanhMucCha, parentCategory.MaDanhMucCha);
                            return (new[] { parentCategory.TenDanhMucCha }, parentCategory.MaDanhMucCha);
                        }
                    }
                }
            }

            // BƯỚC 2: Fallback - tìm theo từ khóa thường
            var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "tìm", "kiếm", "tìm kiếm", "mua", "bán", "cần", "cần mua", "cần tìm",
                "muốn", "muốn mua", "tìm hiểu", "xem",
                "giá", "giá rẻ", "rẻ", "đắt", "tiền", "giá rẻ nhất", "giá tốt", "giá thấp", "giá cao", "chi phí",
                "có", "không", "gì", "cái", "chiếc", "thứ", "của", "với", "trên", "trong", "đó", "này", "kia", "ai", "cái gì",
                "em", "bác", "bạn", "anh", "chị", "tôi", "tớ", "mình", "chúng tôi", "chúng tớ", "ta", "nó", "họ", "cô", "ông",
                "hôm nay", "ngày mai", "tuần trước", "tuần này", "lần", "hôm qua", "tháng", "năm", "lúc", "khi",
                "được", "là", "để", "có thể", "vậy", "thế", "làm", "làm sao", "sao", "tại sao",
                "và", "hay", "hoặc", "nhưng", "mà", "thì", "nếu", "khi", "như", "nên", "vì", "từ", "bởi", "do",
                "tin", "thông tin", "loại", "kiểu", "dạng", "cái", "những", "việc", "chuyện", "may"
            };

            var words = lowerMsg
                .Split(new[] { ' ', ',', '.', ';', ':', '?', '!' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2 && !stopWords.Contains(w))
                .ToList();

            _logger.LogInformation("[AiIntent] Extracted words (after filtering): {words}", string.Join(", ", words));

            if (words.Count == 0)
                return (Array.Empty<string>(), null);

            if (words.Count == 1)
                return (words.ToArray(), null);

            // Gộp cụm từ 2 từ (highest priority)
            var combinedKeywords = new List<string>();
            
            if (words.Count >= 2)
            {
                for (int i = 0; i < words.Count - 1; i++)
                {
                    var combined = $"{words[i]} {words[i + 1]}";
                    if (combined.Length < 50)
                        combinedKeywords.Add(combined);
                }
            }
            
            if (words.Count >= 3)
            {
                for (int i = 0; i < words.Count - 2; i++)
                {
                    var combined3 = $"{words[i]} {words[i + 1]} {words[i + 2]}";
                    if (combined3.Length < 50)
                        combinedKeywords.Add(combined3);
                }
            }
            
            combinedKeywords.AddRange(words);

            var result = combinedKeywords.Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(k => !stopWords.Contains(k))
                .Take(5)
                .ToArray();
            
            _logger.LogInformation("[AiIntent] Final keywords: {keywords}", string.Join(", ", result));
            return (result, null);
        }

        public async Task<string> TestGeminiConnection(string prompt) => await _aiClient.SendPromptAsync(prompt);
    }
}
