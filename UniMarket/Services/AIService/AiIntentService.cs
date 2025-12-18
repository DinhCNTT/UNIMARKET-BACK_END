using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        /// Build comprehensive category context for Gemini to understand all available categories
        /// Gồm: danh mục cha, danh mục con, synonyms, variations
        /// </summary>
        private async Task<string> BuildCategoryContextAsync()
        {
            try
            {
                // Load all parent and child categories
                var parentCategories = await _context.DanhMucChas
                    .AsNoTracking()
                    .Select(c => new { c.MaDanhMucCha, c.TenDanhMucCha })
                    .ToListAsync();

                var childCategories = await _context.DanhMucs
                    .AsNoTracking()
                    .Select(c => new { c.MaDanhMuc, c.TenDanhMuc, c.MaDanhMucCha })
                    .ToListAsync();

                var sb = new StringBuilder();
                sb.AppendLine("AVAILABLE CATEGORIES IN DATABASE:");
                sb.AppendLine("==================================");

                // Add parent categories with their children
                foreach (var parent in parentCategories)
                {
                    sb.AppendLine($"[PARENT] ID={parent.MaDanhMucCha}: {parent.TenDanhMucCha}");
                    
                    var children = childCategories
                        .Where(c => c.MaDanhMucCha == parent.MaDanhMucCha)
                        .ToList();
                    
                    if (children.Count > 0)
                    {
                        foreach (var child in children)
                        {
                            sb.AppendLine($"  └─ ID={child.MaDanhMuc}: {child.TenDanhMuc}");
                        }
                    }
                }

                // Add category synonyms/variations
                sb.AppendLine("\nCOMMON CATEGORY VARIATIONS & SYNONYMS:");
                sb.AppendLine("======================================");
                sb.AppendLine("- Điện thoại = Điện thoại di động = Mobile = Phone = iPhone = Android");
                sb.AppendLine("- Laptop = Máy tính = Computer = Notebook = Macbook = PC");
                sb.AppendLine("- Tivi = TV = Television = Ti vi");
                sb.AppendLine("- Tủ lạnh = Refrigerator = Fridge");
                sb.AppendLine("- Máy giặt = Washer = Washing Machine");
                sb.AppendLine("- Xe = Ô tô = Car = Auto = Automobile");
                sb.AppendLine("- Xe máy = Motorbike = Moto = Motorcycle");

                return sb.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError("[AiIntent] Error building category context: {msg}", ex.Message);
                return ""; // Fallback to empty, Gemini will work without context
            }
        }

        /// <summary>
        /// Phân tích intent từ tin nhắn người dùng.
        /// Sử dụng Gemini để phân tích với full category context, fallback về keyword extraction nếu lỗi.
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

            // 3. Load category context from database
            var categoryContext = await BuildCategoryContextAsync();
            
            // 4. CONTEXT INJECTION: Trích xuất sản phẩm từ lịch sử chat (Bước 1: Trí nhớ ngắn hạn)
            var previousProductsContext = ExtractProductContextFromHistory(history);

            // 5. Gọi Gemini API (với Context Injection)
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

                var prompt = $@"ROLE: Uni.AI - Trợ lý bán hàng công nghệ chuyên nghiệp, am hiểu kỹ thuật, thân thiện.

TASK: Phân tích ý định người dùng (User Intent) để chọn hành động: DÙNG TOOL, TÌM KIẾM, hay TRẢ LỜI NGỮCẢNH.

CONTEXT (Dữ liệu sản phẩm user vừa xem):
{previousProductsContext}

CATEGORY KNOWLEDGE:
{categoryContext}

MESSAGE: ""{message}""
HISTORY: {(string.IsNullOrEmpty(historyText) ? "None" : historyText)}

AVAILABLE TOOLS:
- CalculateShipping: Tính phí ship. Params: location (string).
- CheckWeather: Xem thời tiết. Params: location (string).
- CheckExchangeRate: Xem tỷ giá USD. Params: none.
- GetProductDetail: Lấy chi tiết kỹ thuật (màu, RAM, bảo hành) từ MongoDB. Params: productId (int).

/// --- QUY TẮC XỬ LÝ (Ưu tiên từ 1 -> 5) --- ///

1. **TOOL USAGE (Gọi Hàm)** - Priority 1:
   - Hỏi phí ship -> ToolName=""CalculateShipping"", ToolArgs=""<Location>"" (VD: ""Hà Nội"")
   - Hỏi thời tiết -> ToolName=""CheckWeather"", ToolArgs=""<Location>""
   - Hỏi tỷ giá -> ToolName=""CheckExchangeRate""
   - Hỏi chi tiết sản phẩm cụ thể (màu, RAM, pin, bảo hành) -> ToolName=""GetProductDetail"", ToolArgs=""<ID từ Context>"" (VD: ToolArgs=""32"" nếu hỏi sản phẩm #1 trong list)
   -> Set ShouldSearch=false.

2. **DEEP COMPARISON (So sánh)** - Priority 2:
   - Key: ""so sánh"", ""khác nhau chỗ nào"", ""nên mua con nào"", ""con nào hơn"".
   - Action: Đọc kỹ CONTEXT. So sánh các sản phẩm về: Giá, Chip, RAM, Camera, Pin.
   - Output: Viết một đoạn so sánh ngắn gọn hoặc kẻ bảng so sánh trong ProductContextReply. Khuyên user nên mua con nào tùy nhu cầu.
   -> Set IsAskingAboutProduct=true, ShouldSearch=false.

3. **CONTEXT QUERY (Hỏi về list cũ)** - Priority 3:
   - Key: ""con thứ 2"", ""cái nào đắt nhất"", ""con samsung kia"", ""màu gì"".
   - Action: 
     + Nếu hỏi ""đắt nhất/rẻ nhất"" trong list cũ -> Tìm trong CONTEXT và trả lời đích danh (VD: ""Dạ trong mấy con trên thì S24 Ultra đắt nhất ạ"").
     + Nếu hỏi chi tiết (màu, pin) -> Tìm trong CONTEXT.
       + Có thông tin -> Trả lời.
       + KHÔNG có thông tin -> Trả lời: ""Dạ trong mô tả shop chưa ghi rõ thông số này, bác bấm vào chi tiết để chat với shop nhé!"" (TUYỆT ĐỐI KHÔNG BỊA RA MÀU/THÔNG SỐ).
   -> Set IsAskingAboutProduct=true, ShouldSearch=false.

4. **RE-SEARCH (Tìm kiếm nâng cao)** - Priority 4:
   - Key: ""tìm cái khác"", ""đắt quá"", ""tìm màu đen"", ""giá rẻ hơn"".
   - Action: 
     + Giữ lại Keywords/Category từ lịch sử.
     + Thêm điều kiện mới (SortBy, MinPrice...).
     + Nếu user chê (tìm cái khác) -> Set NeedsShuffle=true.
   -> Set ShouldSearch=true.

5. **NEW SEARCH (Tìm mới)** - Priority 5:
   - Action: Trích xuất Keywords, CategoryId, Attributes.
   - Attributes: Storage (256GB...), Location (Hà Nội...), Brand.
   -> Set ShouldSearch=true.

6. **PRICE EXTRACTION (Lọc giá)** - Important:
   - ""dưới 5 triệu"" -> MaxPrice = 5000000
   - ""trên 10 triệu"" -> MinPrice = 10000000
   - ""tầm 3 đến 5 triệu"" -> MinPrice = 3000000, MaxPrice = 5000000
   - ""giá rẻ"" -> SortBy = ""price_asc""
   - ""giá mắc"", ""đắt"" -> SortBy = ""price_desc""

/// --- JSON OUTPUT FORMAT --- ///
Return ONLY valid JSON:
{{ 
  ""ShouldSearch"": true, 
  ""IsAskingAboutProduct"": false, 
  ""NeedsShuffle"": false, 
  ""ToolName"": null, 
  ""ToolArgs"": null, 
  ""UserReply"": ""Dạ em tìm giúp bác."", 
  ""ProductContextReply"": null, 
  ""Keywords"": [""keyword""], 
  ""CategoryKeyword"": ""điện thoại"", 
  ""CategoryId"": 10, 
  ""Storage"": null, 
  ""Location"": null, 
  ""MinPrice"": null,
  ""MaxPrice"": null,
  ""SortBy"": ""recent"",
  ""Confidence"": 0.9 
}}
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
                            
                            _logger.LogInformation("[AiIntent] ✅ Gemini parsed successfully: ShouldSearch={search}, Keywords={kw}, Brand={brand}, CategoryId={catId}, Confidence={conf}", 
                                result.ShouldSearch, 
                                string.Join(",", result.Keywords ?? Array.Empty<string>()), 
                                result.Brand, 
                                result.CategoryId ?? -1,
                                result.Confidence);
                            
                            // ✅ BƯỚC 1: Validate new fields (Context Injection)
                            // NeedsShuffle and IsAskingAboutProduct are mutually exclusive
                            if (result.NeedsShuffle && result.IsAskingAboutProduct)
                            {
                                _logger.LogWarning("[AiIntent] ⚠️ Both NeedsShuffle and IsAskingAboutProduct are true - resetting IsAskingAboutProduct");
                                result.IsAskingAboutProduct = false;
                            }
                            
                            // If asking about product details, don't search
                            if (result.IsAskingAboutProduct)
                            {
                                result.ShouldSearch = false;
                                _logger.LogInformation("[AiIntent] 📝 User asking about product details - IsAskingAboutProduct=true, ShouldSearch=false");
                            }
                            
                            // If shuffling, keep previous criteria but randomize results
                            if (result.NeedsShuffle)
                            {
                                _logger.LogInformation("[AiIntent] 🎲 Shuffle requested - will randomize results in ProductSearchService");
                            }
                            
                            // --- HYBRID STRATEGY: Fill-in missing Location/Storage using Regex Fallback ---
                            if (result.ShouldSearch)
                            {
                                // If AI missed Location, try Regex detection
                                if (string.IsNullOrEmpty(result.Location))
                                {
                                    var detectedLocation = DetectLocationFromMessage(message);
                                    if (!string.IsNullOrEmpty(detectedLocation))
                                    {
                                        result.Location = detectedLocation;
                                        _logger.LogInformation("[AiIntent] 🔧 HYBRID: Filled Location from Regex: {location}", detectedLocation);
                                    }
                                }
                                
                                // If AI missed Storage, try Regex detection
                                if (string.IsNullOrEmpty(result.Storage))
                                {
                                    var detectedStorage = DetectStorageFromMessage(message);
                                    if (!string.IsNullOrEmpty(detectedStorage))
                                    {
                                        result.Storage = detectedStorage;
                                        _logger.LogInformation("[AiIntent] 🔧 HYBRID: Filled Storage from Regex: {storage}", detectedStorage);
                                    }
                                }
                            }
                            
                            // --- Map CategoryKeyword sang ID ---
                            await MapCategoryIdAsync(result, fallbackCategoryId);
                            
                            // --- Map Location (vị trí) ---
                            await MapLocationAsync(result);
                            
                            // ❌ REMOVED: Query Expansion (gộp từ khóa fallback) 
                            // Lý do: Nó khiến "Tìm điện thoại" + fallback từ khóa -> "Tìm (điện thoại VÀ iPhone VÀ Samsung)" 
                            // -> Không sản phẩm nào thỏa mãn tất cả -> Lỗi 0 kết quả
                            // Gemini đủ thông minh để trích xuất từ khóa chính xác, không cần gộp thêm

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
        /// ✅ FIX: Tìm cả DanhMucChas (cha) và DanhMucs (con)
        /// </summary>
        /// <summary>
        /// Map Location (tên tỉnh/thành phố) sang MaTinhThanh để filter sản phẩm theo vị trí
        /// </summary>
        private async Task MapLocationAsync(AiIntentResult result)
        {
            if (string.IsNullOrEmpty(result.Location))
            {
                _logger.LogInformation("[AiIntent] No location specified");
                return;
            }

            try
            {
                var locationLower = result.Location.ToLower().Trim();
                
                // Tìm tỉnh/thành phố khớp
                var tinhThanh = await _context.TinhThanhs
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => 
                        t.TenTinhThanh.ToLower() == locationLower ||
                        t.TenTinhThanh.ToLower().Contains(locationLower) ||
                        locationLower.Contains(t.TenTinhThanh.ToLower())
                    );
                
                if (tinhThanh != null)
                {
                    // Lưu MaTinhThanh vào Location field (sẽ convert sang int trong ProductSearchService)
                    result.Location = tinhThanh.MaTinhThanh.ToString();
                    _logger.LogInformation("[AiIntent] ✅ Mapped location '{input}' -> MaTinhThanh: {id}", result.Location, tinhThanh.MaTinhThanh);
                }
                else
                {
                    _logger.LogWarning("[AiIntent] ⚠️ Location '{location}' NOT FOUND in database", result.Location);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[AiIntent] Error mapping location: {msg}", ex.Message);
            }
        }

        private async Task MapCategoryIdAsync(AiIntentResult result, int? fallbackId)
        {
            // ✅ NEW: If Gemini already provided CategoryId (with high confidence), trust it
            if (result.CategoryId.HasValue && result.Confidence >= 0.7m)
            {
                _logger.LogInformation("[AiIntent] Using CategoryId from Gemini: {catId} (Confidence: {conf})", result.CategoryId, result.Confidence);
                return;
            }

            // Fallback 1: Use fallback ID if available
            if (!result.CategoryId.HasValue && fallbackId.HasValue)
            {
                result.CategoryId = fallbackId;
                _logger.LogInformation("[AiIntent] CategoryId mapped from fallback: {catId}", fallbackId);
                return;
            }

            // Fallback 2: Try to find via CategoryKeyword (if Gemini only gave keyword, not ID)
            if (!result.CategoryId.HasValue && !string.IsNullOrEmpty(result.CategoryKeyword))
            {
                // ✅ TỐI ƯU: Sử dụng cache nếu available
                if (_categoryCache == null || DateTime.UtcNow.Subtract(_categoryCacheTime).TotalMinutes > CATEGORY_CACHE_MINUTES)
                {
                    _logger.LogInformation("[AiIntent] 🔄 Refreshing category cache (expired or first load)");
                    
                    _categoryCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    
                    // ✅ BƯỚC 1: Thêm danh mục cha (DanhMucChas)
                    var parentCategories = await _context.DanhMucChas
                        .AsNoTracking()
                        .Select(c => new { c.MaDanhMucCha, c.TenDanhMucCha })
                        .ToListAsync();
                    
                    foreach (var cat in parentCategories)
                    {
                        _categoryCache[cat.TenDanhMucCha] = cat.MaDanhMucCha;
                    }
                    
                    // ✅ BƯỚC 2: Thêm danh mục con (DanhMucs) - QUAN TRỌNG!
                    var childCategories = await _context.DanhMucs
                        .AsNoTracking()
                        .Select(c => new { c.MaDanhMuc, c.TenDanhMuc })
                        .ToListAsync();
                    
                    foreach (var cat in childCategories)
                    {
                        // Nếu chưa có, thêm vào cache
                        if (!_categoryCache.ContainsKey(cat.TenDanhMuc))
                        {
                            _categoryCache[cat.TenDanhMuc] = cat.MaDanhMuc;
                        }
                    }
                    
                    _categoryCacheTime = DateTime.UtcNow;
                    _logger.LogInformation("[AiIntent] ✅ Category cache loaded: {parentCount} parent + {childCount} child = {total} total", 
                        parentCategories.Count, childCategories.Count, _categoryCache.Count);
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
                
                // BƯỚC 1: Tìm trong danh mục cha
                var directCategory = await _context.DanhMucChas
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => 
                        EF.Functions.Like(c.TenDanhMucCha, $"%{result.CategoryKeyword}%") ||
                        EF.Functions.Like(result.CategoryKeyword, $"%{c.TenDanhMucCha}%")
                    );
                
                if (directCategory != null)
                {
                    result.CategoryId = directCategory.MaDanhMucCha;
                    _logger.LogInformation("[AiIntent] ✅ Mapped via direct query (parent): {catId}", result.CategoryId);
                    return;
                }

                // BƯỚC 2: Nếu không tìm thấy cha, tìm trong danh mục con
                var directChildCategory = await _context.DanhMucs
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => 
                        EF.Functions.Like(c.TenDanhMuc, $"%{result.CategoryKeyword}%") ||
                        EF.Functions.Like(result.CategoryKeyword, $"%{c.TenDanhMuc}%")
                    );
                
                if (directChildCategory != null)
                {
                    result.CategoryId = directChildCategory.MaDanhMuc;
                    _logger.LogInformation("[AiIntent] ✅ Mapped via direct query (child): {catId}", result.CategoryId);
                    return;
                }

                _logger.LogWarning("[AiIntent] ⚠️ CategoryKeyword '{keyword}' NOT FOUND in database", result.CategoryKeyword);
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
                        
                        // BƯỚC 1: Tìm danh mục cha (DanhMucChas)
                        var parentCategory = await _context.DanhMucChas
                            .FirstOrDefaultAsync(c => c.TenDanhMucCha.ToLower().Contains(categoryPattern.ToLower()));
                        
                        if (parentCategory != null)
                        {
                            _logger.LogInformation("[AiIntent] Found parent category: {catName} (ID: {catId})", parentCategory.TenDanhMucCha, parentCategory.MaDanhMucCha);
                            return (new[] { parentCategory.TenDanhMucCha }, parentCategory.MaDanhMucCha);
                        }

                        // BƯỚC 2: Nếu không tìm thấy cha, tìm danh mục con (DanhMucs)
                        var childCategory = await _context.DanhMucs
                            .FirstOrDefaultAsync(c => c.TenDanhMuc.ToLower().Contains(categoryPattern.ToLower()));
                        
                        if (childCategory != null)
                        {
                            _logger.LogInformation("[AiIntent] Found child category: {catName} (ID: {catId})", childCategory.TenDanhMuc, childCategory.MaDanhMuc);
                            return (new[] { childCategory.TenDanhMuc }, childCategory.MaDanhMuc);
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

        /// <summary>
        /// Detect Storage (dung lượng bộ nhớ) từ tin nhắn người dùng
        /// </summary>
        private string? DetectStorageFromMessage(string message)
        {
            var lowerMsg = message.ToLower();
            
            // Pattern: "64GB", "128GB", "256GB", "512GB", "1TB"
            var storagePatterns = new[] { 
                @"(\d+)\s*(gb|tb|gigabyte|terabyte)", // 128GB, 256 GB, 1TB
                @"dung\s*lượng.*?(\d+\s*(?:gb|tb))", // "dung lượng 256GB"
                @"bộ\s*nhớ.*?(\d+\s*(?:gb|tb))", // "bộ nhớ 128GB"
                @"(\d+\s*(?:gb|tb))" // Just "256GB"
            };
            
            foreach (var pattern in storagePatterns)
            {
                var match = System.Text.RegularExpressions.Regex.Match(lowerMsg, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    // Extract the matched storage value (e.g., "256GB", "512 GB")
                    var storage = match.Groups[match.Groups.Count - 1].Value.Trim().ToUpper();
                    _logger.LogInformation("[AiIntent] Detected storage: {storage}", storage);
                    return storage;
                }
            }
            
            return null;
        }

        /// <summary>
        /// Detect Location (vị trí) từ tin nhắn người dùng
        /// </summary>
        private string? DetectLocationFromMessage(string message)
        {
            var lowerMsg = message.ToLower();
            
            // Common Vietnamese cities/provinces
            var locationPatterns = new Dictionary<string, string>
            {
                { "tphcm|hồ chí minh|sài gòn|saigon|tp hcm", "TPHCM" },
                { "hà nội|hanoi|ha noi|thủ đô", "Hà Nội" },
                { "đà nẵng|da nang|danang", "Đà Nẵng" },
                { "hải phòng|hai phong|haiphong", "Hải Phòng" },
                { "hồ chí minh|tp.hcm|tpхcm", "TPHCM" },
                { "cần thơ|can tho|cantho", "Cần Thơ" },
                { "quy nhơn|quynhon|quy nhơn", "Quy Nhơn" },
                { "nha trang|nhatrang", "Nha Trang" },
                { "hà tĩnh|ha tinh|hatinh", "Hà Tĩnh" },
                { "nghệ an|nghe an|nghean", "Nghệ An" },
                { "hải dương|hai duong|haiduong", "Hải Dương" },
                { "hưng yên|hung yen|hungyen", "Hưng Yên" }
            };
            
            foreach (var (pattern, cityName) in locationPatterns)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(lowerMsg, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    _logger.LogInformation("[AiIntent] Detected location: {location}", cityName);
                    return cityName;
                }
            }
            
            return null;
        }

        /// <summary>
        /// BƯỚC 1: CONTEXT INJECTION - Trích xuất sản phẩm từ lịch sử chat
        /// Giúp AI "nhớ" danh sách sản phẩm vừa gợi ý để trả lời câu hỏi chi tiết
        /// </summary>
        private string ExtractProductContextFromHistory(List<AiChatMessageDto>? history)
        {
            try
            {
                if (history == null || history.Count == 0)
                    return "";

                // Tìm tin nhắn AI cuối cùng có chứa sản phẩm
                for (int i = history.Count - 1; i >= 0; i--)
                {
                    var msg = history[i];
                    if (msg.Role != "assistant")
                        continue;

                    // Kiểm tra nếu content chứa JSON (thường có suggestedProducts)
                    if (string.IsNullOrEmpty(msg.Content))
                        continue;

                    try
                    {
                        // Thử parse JSON để lấy suggestedProducts
                        if (msg.Content.StartsWith("{"))
                        {
                            using (var doc = JsonDocument.Parse(msg.Content))
                            {
                                if (doc.RootElement.TryGetProperty("suggestedProducts", out var productsElement))
                                {
                                    var productsJson = productsElement.GetRawText();
                                    // Tạo context string từ danh sách sản phẩm
                                    var productList = new List<string>();
                                    foreach (var product in productsElement.EnumerateArray())
                                    {
                                        var name = product.GetProperty("ten").GetString() ?? "Unknown";
                                        var price = product.GetProperty("gia").GetDecimal();
                                        var id = product.GetProperty("id").GetInt32();
                                        
                                        // ✅ THÊM: Lấy mô tả (nếu có)
                                        var desc = "Không có mô tả";
                                        if (product.TryGetProperty("shortDescription", out var descEl) && descEl.ValueKind != JsonValueKind.Null)
                                        {
                                            desc = descEl.GetString() ?? "Không có mô tả";
                                        }
                                        
                                        // ✅ GỬI KÈM MÔ TẢ VÀO CONTEXT:
                                        productList.Add($"[ID={id}] {name} - Giá: {price:N0}đ\n   Mô tả: {desc}");
                                    }

                                    if (productList.Count > 0)
                                    {
                                        var context = "PREVIOUSLY_SUGGESTED_PRODUCTS (User just saw these):\n" +
                                                     string.Join("\n", productList.Select((p, idx) => $"#{idx + 1}. {p}"));
                                        
                                        _logger.LogInformation("[AiIntent] 📝 Context Injection: Found {count} previous products", productList.Count);
                                        return context;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug("[AiIntent] Could not parse product context: {msg}", ex.Message);
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[AiIntent] Error extracting product context: {msg}", ex.Message);
            }

            return "";
        }
    }
}
