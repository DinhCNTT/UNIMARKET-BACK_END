using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UniMarket.DataAccess;
using UniMarket.DTO;
using UniMarket.Models;

namespace UniMarket.Services
{
    public class AiService
    {
        private readonly ApplicationDbContext _context;
        private readonly AiClient _aiClient;
        private readonly ILogger<AiService> _logger;
        private string? _lastRawAiResponse;

        public AiService(ApplicationDbContext context, AiClient aiClient, ILogger<AiService> logger)
        {
            _context = context;
            _aiClient = aiClient;
            _logger = logger;
        }

        public async Task<AiChatResponseDto> ProcessChatAsync(AiChatRequestDto request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.Message))
                    throw new ArgumentException("request.Message is required", nameof(request));

                // DEBUG: Log lịch sử chat nhận được
                _logger.LogInformation($"[AI] Nhận tin nhắn: '{request.Message}'. History count: {request.History?.Count ?? 0}");
                if (request.History != null && request.History.Count > 0)
                {
                    foreach (var h in request.History.TakeLast(3))
                        _logger.LogInformation($"[AI] History: [{h.Role}] {h.Content}");
                }

                // BƯỚC 1: Phân tích intent với AI
                _logger.LogInformation("[AI] Starting AnalyzeIntentWithGemini...");
                var aiIntent = await AnalyzeIntentWithGemini(request.Message, request.History);
                _logger.LogInformation("[AI] AnalyzeIntentWithGemini completed successfully");

                var response = new AiChatResponseDto
                {
                    // Ưu tiên dùng UserReply thông minh từ AI. Nếu null mới dùng câu mặc định.
                    ReplyText = !string.IsNullOrEmpty(aiIntent?.UserReply) 
                                ? aiIntent.UserReply 
                                : "Dạ, để em tìm giúp bác nhé.",
                    SuggestedProducts = null,
                    ClarifyingQuestion = aiIntent?.ClarifyingQuestion
                };

                if (!string.IsNullOrEmpty(_lastRawAiResponse))
                {
                    response.DebugRaw = _lastRawAiResponse;
                }

                // BƯỚC 2: Tìm sản phẩm nếu cần
                if (aiIntent != null && (aiIntent.ShouldSearch == true ||
                    (aiIntent.Keywords != null && aiIntent.Keywords.Length > 0) ||
                    aiIntent.MinPrice.HasValue || aiIntent.MaxPrice.HasValue || aiIntent.RequireVideo || aiIntent.CategoryId.HasValue))
                {
                    // Start query with Include FIRST for proper eager loading
                    var query = _context.TinDangs
                        .AsNoTracking()
                        .Include(p => p.AnhTinDangs)  // Include images FIRST
                        .AsQueryable()
                        .Where(p => p.TrangThai == TrangThaiTinDang.DaDuyet);
                    
                    bool usedFlexibleSearch = false;  // Track if fallback to flexible search

                    // Keywords filter - split multi-word keywords into individual words
                    if (aiIntent.Keywords != null && aiIntent.Keywords.Length > 0)
                    {
                        // Split keywords by space to handle "điện thoại Xiaomi" -> ["điện thoại", "Xiaomi"]
                        var allKeywords = aiIntent.Keywords
                            .SelectMany(k => k.Split(new[] { ' ', '-', '/' }, StringSplitOptions.RemoveEmptyEntries))
                            .Where(k => !string.IsNullOrWhiteSpace(k) && k.Trim().Length >= 2)  // Minimum 2 chars after split
                            .Select(k => k.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray();
                        
                        _logger.LogInformation("[AI] Keywords after split: {keywords}", string.Join(", ", allKeywords));
                        
                        if (allKeywords.Length > 0)
                        {
                            // ✅ FIX: Áp dụng ALL keywords (AND logic) thay vì ANY (OR logic)
                            // Mỗi keyword phải match ÍT NHẤT một field (Title, Description, hoặc JSON)
                            var strictQuery = query;
                            foreach (var kw in allKeywords)
                            {
                                var param = Expression.Parameter(typeof(TinDang), "p");
                                var pattern = Expression.Constant("%" + kw + "%");
                                var titleProp = Expression.PropertyOrField(param, nameof(TinDang.TieuDe));
                                var moTaProp = Expression.PropertyOrField(param, nameof(TinDang.MoTa));
                                var thongTinProp = Expression.PropertyOrField(param, nameof(TinDang.ThongTinChiTiet));

                                var efFunctionsProperty = typeof(EF).GetProperty("Functions", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                                var efFunctionsExpr = Expression.Property(null, efFunctionsProperty!);
                                var likeMethod = typeof(DbFunctionsExtensions).GetMethod("Like", new[] { typeof(DbFunctions), typeof(string), typeof(string) });

                                var titleLike = Expression.Call(likeMethod!, efFunctionsExpr, titleProp, pattern);
                                var moTaLike = Expression.Call(likeMethod!, efFunctionsExpr, moTaProp, pattern);
                                var thongLike = Expression.Call(likeMethod!, efFunctionsExpr, thongTinProp, pattern);

                                // Mỗi keyword: match title OR description OR JSON
                                var orKeyword = Expression.OrElse(titleLike, Expression.OrElse(moTaLike, thongLike));
                                var lambda = Expression.Lambda<Func<TinDang, bool>>(orKeyword, param);
                                
                                // AND với các keywords khác
                                strictQuery = strictQuery.Where(lambda);
                            }
                            
                            // Check if strict query (ALL keywords) has results
                            var strictCount = await strictQuery.CountAsync();
                            _logger.LogInformation("[AI] Strict query (ALL keywords) returned {count} products", strictCount);
                            
                            if (strictCount > 0)
                            {
                                query = strictQuery;
                            }
                            else if (allKeywords.Length > 1)
                            {
                                // ✅ FALLBACK: Nếu không tìm thấy với tất cả keywords, tìm theo bất kỳ keyword nào (OR logic)
                                _logger.LogWarning("[AI] No strict matches, falling back to flexible search (ANY keyword)");
                                usedFlexibleSearch = true;
                                
                                var flexibleParam = Expression.Parameter(typeof(TinDang), "p");
                                Expression? combinedOr = null;

                                var efFunctionsProperty = typeof(EF).GetProperty("Functions", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                                var efFunctionsExpr = Expression.Property(null, efFunctionsProperty!);
                                var likeMethod = typeof(DbFunctionsExtensions).GetMethod("Like", new[] { typeof(DbFunctions), typeof(string), typeof(string) });

                                foreach (var kw in allKeywords)
                                {
                                    // --- TỐI ƯU 3: Bỏ qua các từ khóa quá ngắn hoặc vô nghĩa khi search flexible ---
                                    if (kw.Length < 3 || kw == "tin" || kw == "đăng") 
                                    {
                                        _logger.LogInformation("[AI] Skipping low-quality keyword for flexible search: {kw}", kw);
                                        continue;
                                    }

                                    var pattern = Expression.Constant("%" + kw + "%");
                                    var titleProp = Expression.PropertyOrField(flexibleParam, nameof(TinDang.TieuDe));
                                    
                                    // CHỈ TÌM TRONG TIÊU ĐỀ (Bỏ MoTa và ThongTinChiTiet để tránh rác)
                                    var titleLike = Expression.Call(likeMethod!, efFunctionsExpr, titleProp, pattern);

                                    combinedOr = combinedOr == null ? titleLike : Expression.OrElse(combinedOr, titleLike);
                                    // -----------------------------------------------------------
                                }

                                if (combinedOr != null)
                                {
                                    var flexibleLambda = Expression.Lambda<Func<TinDang, bool>>(combinedOr, flexibleParam);
                                    query = query.Where(flexibleLambda);
                                    _logger.LogInformation("[AI] Applied flexible search with OR logic");
                                }
                            }
                        }
                    }

                    // Price filter
                    if (aiIntent.MinPrice.HasValue)
                        query = query.Where(p => p.Gia >= aiIntent.MinPrice.Value);
                    if (aiIntent.MaxPrice.HasValue)
                        query = query.Where(p => p.Gia <= aiIntent.MaxPrice.Value);

                    // Video filter
                    if (aiIntent.RequireVideo)
                        query = query.Where(p => !string.IsNullOrEmpty(p.VideoUrl));

                    // Category filter
                    if (aiIntent.CategoryId.HasValue)
                        query = query.Where(p => p.MaDanhMuc == aiIntent.CategoryId.Value);

                    // ✅ JSON ATTRIBUTE FILTERS - Filter theo Brand, Color, Storage, Warranty, Origin
                    // Fetch all theo query trên rồi filter ở C# vì EF Core không hỗ trợ JSON full tìm kiếm tốt
                    List<TinDang> fetchedProducts;
                    
                    if (!string.IsNullOrEmpty(aiIntent.Brand) || !string.IsNullOrEmpty(aiIntent.Color) || 
                        !string.IsNullOrEmpty(aiIntent.Storage) || !string.IsNullOrEmpty(aiIntent.Warranty) || 
                        !string.IsNullOrEmpty(aiIntent.Origin) || !string.IsNullOrEmpty(aiIntent.Condition))
                    {
                        _logger.LogInformation("[AI] Filtering by product specs: Brand={brand}, Color={color}, Storage={storage}", 
                            aiIntent.Brand, aiIntent.Color, aiIntent.Storage);
                        
                        // Fetch all ở trên rồi filter client-side
                        var allProductsUnfiltered = await query.ToListAsync();
                        var filtered = new List<TinDang>();
                        
                        foreach (var p in allProductsUnfiltered)
                        {
                            var specs = new DTO.ProductSpecDTO();
                            if (!string.IsNullOrEmpty(p.ThongTinChiTiet))
                            {
                                try
                                {
                                    specs = JsonSerializer.Deserialize<DTO.ProductSpecDTO>(p.ThongTinChiTiet, 
                                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? specs;
                                }
                                catch { /* Ignore parse errors */ }
                            }
                            
                            // Check if product matches all filters
                            bool matchesSpecs = true;
                            
                            if (!string.IsNullOrEmpty(aiIntent.Brand) && 
                                (string.IsNullOrEmpty(specs.Hang) || !specs.Hang.Contains(aiIntent.Brand, StringComparison.OrdinalIgnoreCase)))
                                matchesSpecs = false;
                            
                            if (!string.IsNullOrEmpty(aiIntent.Color) && 
                                (string.IsNullOrEmpty(specs.MauSac) || !specs.MauSac.Contains(aiIntent.Color, StringComparison.OrdinalIgnoreCase)))
                                matchesSpecs = false;
                            
                            if (!string.IsNullOrEmpty(aiIntent.Storage) && 
                                (string.IsNullOrEmpty(specs.DungLuong) || !specs.DungLuong.Contains(aiIntent.Storage, StringComparison.OrdinalIgnoreCase)))
                                matchesSpecs = false;
                            
                            if (!string.IsNullOrEmpty(aiIntent.Warranty) && 
                                (string.IsNullOrEmpty(specs.BaoHanh) || !specs.BaoHanh.Contains(aiIntent.Warranty, StringComparison.OrdinalIgnoreCase)))
                                matchesSpecs = false;
                            
                            if (!string.IsNullOrEmpty(aiIntent.Origin) && 
                                (string.IsNullOrEmpty(specs.XuatXu) || !specs.XuatXu.Contains(aiIntent.Origin, StringComparison.OrdinalIgnoreCase)))
                                matchesSpecs = false;
                            
                            if (!string.IsNullOrEmpty(aiIntent.Condition) && aiIntent.Condition != p.TinhTrang)
                                matchesSpecs = false;
                            
                            if (matchesSpecs)
                                filtered.Add(p);
                        }
                        
                        fetchedProducts = filtered.Take(12).ToList();
                    }
                    else
                    {
                        // Fetch data with images for AI suggestions display
                        fetchedProducts = await query.Take(12).ToListAsync();
                    }

                    _logger.LogInformation($"[AI] Database query returned {fetchedProducts.Count} products from {_context.TinDangs.Count()} total items");

                    // ✅ FILTER HOT POSTS - Lọc theo hot tin đăng (TinDangYeuThichs count >= 2)
                    if (aiIntent.FilterByHot)
                    {
                        fetchedProducts = fetchedProducts
                            .Where(p => p.TinDangYeuThichs != null && p.TinDangYeuThichs.Count >= 2)
                            .ToList();
                        _logger.LogInformation("[AI] Filtered by hot posts: {count}", fetchedProducts.Count);
                    }

                    // ✅ SORT BY - Theo Gemini intent
                    fetchedProducts = (aiIntent.SortBy?.ToLower() switch
                    {
                        "price_asc" => fetchedProducts.OrderBy(p => p.Gia).ToList(),
                        "price_desc" => fetchedProducts.OrderByDescending(p => p.Gia).ToList(),
                        "views_desc" => fetchedProducts.OrderByDescending(p => p.SoLuotXem).ToList(),
                        _ => fetchedProducts  // Default: recent (already NgayDang desc)
                            .OrderByDescending(p =>
                                p.SoLuotXem +
                                (DateTime.UtcNow.Subtract(p.NgayDang).TotalDays < 7 ? 100 :
                                 DateTime.UtcNow.Subtract(p.NgayDang).TotalDays < 14 ? 50 : 0)
                            )
                            .ThenByDescending(p => p.NgayDang)
                            .ToList()
                    });

                    _logger.LogInformation("[AI] Sorted by: {sort}", aiIntent.SortBy ?? "recent");

                    // Map to DTO - WITH ảnh đầu tiên
                    var allProducts = fetchedProducts.Select(p => new ProductSuggestionDto
                    {
                        Id = p.MaTinDang,
                        Ten = p.TieuDe,
                        Gia = p.Gia,
                        AnhDaiDien = p.AnhTinDangs != null && p.AnhTinDangs.Count > 0
                            ? (p.AnhTinDangs.FirstOrDefault()?.DuongDan.StartsWith("http", StringComparison.OrdinalIgnoreCase) ?? false
                                ? p.AnhTinDangs.First().DuongDan
                                : (p.AnhTinDangs.First().DuongDan.StartsWith("/")
                                    ? p.AnhTinDangs.First().DuongDan
                                    : $"/images/Posts/{p.AnhTinDangs.First().DuongDan}"))
                            : null,
                        TinhTrang = p.TinhTrang,
                        LinkVideo = p.VideoUrl,
                        SoLuotXem = p.SoLuotXem,
                        SoLike = 0,  // Not needed for AI analysis
                        MaNguoiBan = p.MaNguoiBan,
                        IsHot = false  // Not needed for AI analysis
                    }).ToList();

                    var products = allProducts.Take(8).ToList();

                    if (products.Count > 0)
                    {
                        response.SuggestedProducts = products;

                        // --- TỐI ƯU 2: Xử lý câu hỏi đếm số lượng ---
                        bool isAskingCount = request.Message.ToLower().Contains("bao nhiêu") || 
                                             request.Message.ToLower().Contains("số lượng") ||
                                             request.Message.ToLower().Contains("có mấy");

                        if (isAskingCount)
                        {
                            response.ReplyText = $"Dạ hiện tại em tìm thấy {fetchedProducts.Count} tin đăng phù hợp với yêu cầu của bác ạ.";
                            _logger.LogInformation("[AI] Detected count question - Reply: {reply}", response.ReplyText);
                        }
                        else
                        // -----------------------------------------------------------
                        {
                            // Build product context for natural reply
                            var topProducts = products.Take(3).Select(p => $"{p.Ten} ({p.Gia:N0}đ)");
                            var productContext = string.Join(", ", topProducts);

                            try
                            {
                                var generationPrompt = $@"ROLE: You are UniMarket friendly customer support (Vietnamese).
TASK: Write 1 natural, short reply (under 20 words) to customer asking to view products.

Context: Customer asked '{request.Message}'. System found: {productContext} (and more).
{(usedFlexibleSearch ? "Note: Flexible search was used because no exact matches were found." : "")}

RULES:
- Output ONLY the reply text, no explanations
- Be friendly but professional
- NEVER say 'Xin chào' (don't re-greet)
- Use casual Vietnamese: 'em', 'bác', 'bạn'

EXAMPLE: 'Dạ em tìm được mấy bé này xinh lắm nè, bác xem thử nhé.'";

                                var naturalReplyRaw = await _aiClient.SendPromptAsync(generationPrompt);
                                var naturalReply = naturalReplyRaw?.Trim().Trim('"', '\'', '\n', '\r');

                                if (!string.IsNullOrWhiteSpace(naturalReply) && naturalReply.Length > 5 && naturalReply.Length < 200)
                                {
                                    response.ReplyText = naturalReply;
                                    _logger.LogDebug("Generated natural reply: {reply}", naturalReply);
                                }
                                else
                                {
                                    _logger.LogWarning("AI reply invalid or empty: {reply}", naturalReplyRaw);
                                    response.ReplyText = "Dạ, em lục kho thấy mấy món này hợp ý bác nè:";
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error generating natural reply for message: {msg}", request.Message);
                                response.ReplyText = "Dạ có ngay! Mời bạn xem qua danh sách bên dưới nhé:";
                            }
                        }
                    }
                    else
                    {
                        // No products found - update reply to indicate search failed
                        response.SuggestedProducts = null;
                        response.ReplyText = "Dạ em chưa tìm thấy sản phẩm phù hợp với yêu cầu của bác.";
                        
                        if (!string.IsNullOrEmpty(aiIntent?.ClarifyingQuestion))
                        {
                            response.ClarifyingQuestion = aiIntent.ClarifyingQuestion;
                        }
                        else
                        {
                            response.ClarifyingQuestion = "Bác có muốn tôi mở rộng tìm kiếm hay hỏi lại theo tiêu chí khác không?";
                        }
                    }
                }

                // Persist chat history using CuocTroChuyen/TinNhan
                try
                {
                    // Kiểm tra UserId không được null
                    if (string.IsNullOrWhiteSpace(request.UserId))
                    {
                        _logger.LogWarning("[AI] Cannot persist chat: UserId is null or empty");
                        return response;
                    }

                    // --- FIX LỖI FOREIGN KEY: Kiểm tra và tạo User ảo nếu chưa có ---
                    // Luôn tìm user AI bằng normalized username để tránh ID mismatch
                    var aiUser = await _context.Users
                        .FirstOrDefaultAsync(u => u.NormalizedUserName == "UNI.AI");
                    
                    if (aiUser == null)
                    {
                        // Tạo user ảo mới
                        aiUser = new ApplicationUser
                        {
                            Id = Guid.NewGuid().ToString(), // Dùng GUID để tránh conflict
                            UserName = "uni.ai",
                            NormalizedUserName = "UNI.AI",
                            Email = "ai@unimarket.com",
                            NormalizedEmail = "AI@UNIMARKET.COM",
                            EmailConfirmed = true,
                            PasswordHash = "NO_PASSWORD",
                            SecurityStamp = Guid.NewGuid().ToString(),
                            ConcurrencyStamp = Guid.NewGuid().ToString(),
                            FullName = "Trợ lý ảo Uni.AI",
                            AvatarUrl = "/images/uni-ai-avatar.svg",
                            IsOnline = true
                        };
                        _context.Users.Add(aiUser);
                        try
                        {
                            await _context.SaveChangesAsync(); // Lưu user ảo vào DB ngay lập tức
                            _logger.LogInformation("[AI] AI user created successfully with ID: {id}", aiUser.Id);
                        }
                        catch (Exception ex)
                        {
                            // Duplicate key có thể xảy ra do race condition, bỏ qua
                            _logger.LogWarning(ex, "[AI] Failed to create AI user (likely duplicate), continuing");
                            _context.ChangeTracker.Clear(); // Clear pending changes
                            
                            // Thử lại find bằng username
                            aiUser = await _context.Users
                                .FirstOrDefaultAsync(u => u.NormalizedUserName == "UNI.AI");
                            
                            if (aiUser == null)
                            {
                                _logger.LogError("[AI] AI user still not found after failed creation - cannot persist chat");
                                return response;
                            }
                            _logger.LogInformation("[AI] Found existing AI user after duplicate error: {id}", aiUser.Id);
                        }
                    }
                    // ---------------------------------------------------------------

                    var chatId = $"ai-assistant-{request.UserId}";
                    var existingChat = await _context.CuocTroChuyens
                        .FirstOrDefaultAsync(c => c.MaCuocTroChuyen == chatId);

                    if (existingChat == null)
                    {
                        existingChat = new CuocTroChuyen
                        {
                            MaCuocTroChuyen = chatId,
                            ThoiGianTao = DateTime.UtcNow,
                            MaTinDang = 0,  // AI chat has no product ID
                            TieuDeTinDang = "Trợ lý ảo Uni.AI",
                            AnhDaiDienTinDang = "/images/uni-ai-avatar.svg",
                            MaNguoiBan = request.UserId,
                            IsEmpty = false
                        };
                        _context.CuocTroChuyens.Add(existingChat);
                        
                        try
                        {
                            await _context.SaveChangesAsync();
                            _logger.LogInformation($"[AI] New conversation created: {existingChat.MaCuocTroChuyen}");

                            // Verify both users exist before adding participants
                            var userExists = await _context.Users.AnyAsync(u => u.Id == request.UserId);
                            if (userExists)
                            {
                                _context.NguoiThamGias.Add(new NguoiThamGia { MaCuocTroChuyen = existingChat.MaCuocTroChuyen, MaNguoiDung = request.UserId });
                            }
                            else
                            {
                                _logger.LogWarning($"[AI] User '{request.UserId}' does not exist, skipping participant creation");
                            }
                            
                            _context.NguoiThamGias.Add(new NguoiThamGia { MaCuocTroChuyen = existingChat.MaCuocTroChuyen, MaNguoiDung = aiUser.Id });
                            await _context.SaveChangesAsync();
                        }
                        catch (Exception ex)
                        {
                            // Cuộc trò chuyện có thể đã được tạo bởi request khác (race condition)
                            _logger.LogWarning(ex, "[AI] Failed to create chat (likely duplicate), fetching existing");
                            _context.ChangeTracker.Clear();
                            
                            existingChat = await _context.CuocTroChuyens
                                .FirstOrDefaultAsync(c => c.MaCuocTroChuyen == chatId);
                            
                            if (existingChat == null)
                            {
                                _logger.LogError("[AI] Chat still not found after failed creation - cannot persist");
                                return response;
                            }
                        }
                    }
                    else
                    {
                        existingChat.IsEmpty = false;
                        await _context.SaveChangesAsync();
                    }

                    // Verify user exists in database before creating messages
                    var userExistsForMessages = await _context.Users.AnyAsync(u => u.Id == request.UserId);
                    if (!userExistsForMessages)
                    {
                        _logger.LogWarning($"[AI] User '{request.UserId}' does not exist in database. Skipping message persistence.");
                        return response;
                    }

                    // Add user message
                    var userMessage = new TinNhan
                    {
                        MaCuocTroChuyen = existingChat.MaCuocTroChuyen,
                        MaNguoiGui = request.UserId,
                        NoiDung = request.Message,
                        ThoiGianGui = DateTime.UtcNow,
                        Loai = LoaiTinNhan.Text
                    };
                    _context.TinNhans.Add(userMessage);
                    _logger.LogDebug($"[AI] User message added to context: '{request.Message}'");

                    // Add AI response message
                    // ✅ FIX: Serialize toàn bộ object response thành JSON để Frontend render được box sản phẩm
                    var aiPayload = new
                    {
                        replyText = response.ReplyText,
                        suggestedProducts = response.SuggestedProducts,
                        clarifyingQuestion = response.ClarifyingQuestion
                    };

                    var aiMessage = new TinNhan
                    {
                        MaCuocTroChuyen = existingChat.MaCuocTroChuyen,
                        MaNguoiGui = aiUser.Id,
                        NoiDung = JsonSerializer.Serialize(aiPayload),
                        ThoiGianGui = DateTime.UtcNow.AddMilliseconds(500),
                        Loai = LoaiTinNhan.Text
                    };
                    _context.TinNhans.Add(aiMessage);
                    _logger.LogDebug($"[AI] AI message added to context with suggestions: '{response.ReplyText}'");

                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"[AI] Successfully persisted chat messages for conversation: {existingChat.MaCuocTroChuyen}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[AI] CRITICAL ERROR: Failed to persist AI chat messages. Details: {Message} | StackTrace: {StackTrace}", ex.Message, ex.StackTrace);
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ProcessChatAsync failed completely. Exception: {msg}", ex.Message);
                return new AiChatResponseDto
                {
                    ReplyText = "Dạ, em gặp lỗi kỹ thuật. Vui lòng thử lại sau nhé.",
                    SuggestedProducts = null,
                    ClarifyingQuestion = null,
                    DebugRaw = $"Error: {ex.GetType().Name}: {ex.Message}"
                };
            }
        }
        public async Task<string> TestGeminiConnection(string prompt)
        {
            return await _aiClient.SendPromptAsync(prompt);
        }

        /// <summary>
        /// Trích xuất từ khóa thông minh khi AI API lỗi - tìm kiếm theo tiêu đề sản phẩm
        /// Nếu tìm thấy danh mục cha, sẽ lọc theo danh mục đó.
        /// Ví dụ: "Tìm máy giặt" → tìm tất cả sản phẩm có "máy giặt" trong tiêu đề
        /// </summary>
        private async Task<(string[] Keywords, int? CategoryId)> ExtractSmartKeywordsWithCategory(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return (Array.Empty<string>(), null);

            var lowerMsg = message.ToLower();
            
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
                  (new[] { "nike", "adidas", "puma", "giày", "dép", "giày dép", "sandal" }, "giày dép") }
            };

            // Check if message contains category keywords
            foreach (var (pattern, (brands, categoryPattern)) in categoryKeywordMappings)
            {
                var keywordPatterns = pattern.Split('|');
                foreach (var kw in keywordPatterns)
                {
                    if (lowerMsg.Contains(kw))
                    {
                        _logger.LogInformation("[AI] Detected category keyword: {kw} -> Pattern: {categoryPattern}", kw, categoryPattern);
                        
                        // Tìm danh mục cha từ DB theo categoryPattern
                        var parentCategory = await _context.DanhMucChas
                            .FirstOrDefaultAsync(c => c.TenDanhMucCha.ToLower().Contains(categoryPattern.ToLower()));
                        
                        if (parentCategory != null)
                        {
                            _logger.LogInformation("[AI] Found category: {catName} (ID: {catId})", parentCategory.TenDanhMucCha, parentCategory.MaDanhMucCha);
                            
                            // ✅ BƯỚC MỚI: Lấy danh sách brand ĐỘNG từ Database cho danh mục này
                            // Trích xuất từ TieuDe vì brand thường nằm trong tiêu đề sản phẩm
                            var productTitles = await _context.TinDangs
                                .Where(t => t.DanhMuc != null && t.DanhMuc.MaDanhMucCha == parentCategory.MaDanhMucCha && !string.IsNullOrEmpty(t.TieuDe))
                                .Select(t => t.TieuDe.ToLower())
                                .ToListAsync();

                            // Tách từ từ từng title để tìm brand
                            var brandWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var title in productTitles)
                            {
                                var titleWords = title.Split(new[] { ' ', '-', '/', '(', ')', ',', '.' }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var word in titleWords)
                                {
                                    // Chỉ lấy từ có độ dài > 2 ký tự (để bỏ những ký tự vô nghĩa)
                                    if (word.Length > 2 && word.Length < 30)
                                    {
                                        brandWords.Add(word);
                                    }
                                }
                            }

                            // Tìm brand có trong message
                            var foundBrands = brandWords
                                .Where(b => !string.IsNullOrEmpty(b) && lowerMsg.Contains(b.ToLower()))
                                .ToArray();

                            if (foundBrands.Length > 0)
                            {
                                _logger.LogInformation("[AI] Found specific brands from product titles: {brands}", string.Join(", ", foundBrands));
                                return (foundBrands, parentCategory.MaDanhMucCha);
                            }
                            
                            // Nếu không tìm thấy brand cụ thể, dùng chính danh mục làm keyword
                            return (new[] { parentCategory.TenDanhMucCha }, parentCategory.MaDanhMucCha);
                        }
                    }
                }
            }

            // BƯỚC 2: Fallback - tìm theo từ khóa thường
            var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // Động từ tìm kiếm
                "tìm", "kiếm", "tìm kiếm", "mua", "bán", "cần", "cần mua", "cần tìm",
                "muốn", "muốn mua", "tìm hiểu", "xem",
                
                // Từ giá cả
                "giá", "giá rẻ", "rẻ", "đắt", "tiền", "giá rẻ nhất", 
                "giá tốt", "giá thấp", "giá cao", "chi phí",
                
                // Từ phổ biến không mang nghĩa sản phẩm
                "có", "không", "gì", "cái", "chiếc", "thứ", "của", "với", 
                "trên", "trong", "đó", "này", "kia", "ai", "cái gì",
                
                // Đại từ nhân xưng
                "em", "bác", "bạn", "anh", "chị", "tôi", "tớ", "mình", 
                "chúng tôi", "chúng tớ", "ta", "nó", "họ", "cô", "ông",
                
                // Thời gian
                "hôm nay", "ngày mai", "tuần trước", "tuần này", "lần",
                "hôm qua", "tháng", "năm", "lúc", "khi",
                
                // Các từ liên kết
                "được", "là", "để", "có thể", "vậy", "thế",
                "làm", "làm sao", "sao", "tại sao",
                
                // Liên từ
                "và", "hay", "hoặc", "nhưng", "mà", "thì", "nếu", "khi", "như",
                "nên", "vì", "từ", "bởi", "do",
                
                // Từ mô tả chung không rõ ràng
                "tin", "thông tin", "loại", "kiểu", "dạng",
                "cái", "những", "việc", "chuyện",
                
                // Từ không rõ ràng hoặc quá ngắn
                "may"
            };

            // Tách từ
            var words = lowerMsg
                .Split(new[] { ' ', ',', '.', ';', ':', '?', '!' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2 && !stopWords.Contains(w))
                .ToList();

            _logger.LogInformation("[AI] ExtractSmartKeywords - Raw words: {words}", string.Join(", ", words));

            if (words.Count == 0)
                return (Array.Empty<string>(), null);

            // Nếu chỉ có 1 từ, dùng trực tiếp
            if (words.Count == 1)
                return (words.ToArray(), null);

            // Nếu có 2 từ trở lên, ưu tiên cụm từ
            var combinedKeywords = new List<string>();
            
            // Cụm 2 từ (highest priority)
            if (words.Count >= 2)
            {
                for (int i = 0; i < words.Count - 1; i++)
                {
                    var combined = $"{words[i]} {words[i + 1]}";
                    if (combined.Length < 50)
                        combinedKeywords.Add(combined);
                }
            }
            
            // Cụm 3 từ
            if (words.Count >= 3)
            {
                for (int i = 0; i < words.Count - 2; i++)
                {
                    var combined3 = $"{words[i]} {words[i + 1]} {words[i + 2]}";
                    if (combined3.Length < 50)
                        combinedKeywords.Add(combined3);
                }
            }
            
            // Thêm từng từ riêng lẻ
            combinedKeywords.AddRange(words);

            var result = combinedKeywords.Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(k => !stopWords.Contains(k))
                .Take(5)
                .ToArray();
            
            _logger.LogInformation("[AI] ExtractSmartKeywords - Final keywords: {keywords}", string.Join(", ", result));
            return (result, null);
        }
        
        private async Task<AiIntentResult> AnalyzeIntentWithGemini(string message, List<AiChatMessageDto>? history)
        {
            // Biến lưu kết quả fallback (mặc định tìm theo từ khóa cắt chuỗi)
            var (keywords, categoryId) = await ExtractSmartKeywordsWithCategory(message);
            _logger.LogInformation("[AI] Extracted keywords: {keywords}, CategoryId: {catId}", string.Join(", ", keywords ?? Array.Empty<string>()), categoryId);
            
            var fallbackResult = new AiIntentResult
            {
                ShouldSearch = true,
                UserReply = "Dạ, mạng hơi chập chờn nên em tìm theo từ khóa bác gửi nhé.",
                Keywords = keywords,
                CategoryId = categoryId,
                Confidence = 0.5m
            };

            try
            {
                // 1. Chuẩn bị lịch sử chat
                var historyText = "";
                if (history != null && history.Count > 0)
                {
                    var recentHistory = history.TakeLast(6);
                    historyText = string.Join("\n", recentHistory.Select(h => $"{h.Role}: {h.Content}"));
                }

                // 2. PROMPT - Chi tiết hơn để trích xuất thuộc tính sản phẩm
                var prompt = $@"ROLE: Uni.AI shopping assistant (Vietnamese market).
TASK: Analyze customer message and extract search intent with product attributes.

Message: ""{message}""
History: {(string.IsNullOrEmpty(historyText) ? "None" : historyText)}

Return ONLY valid JSON (no markdown, no explanation, no extra text):
{{
  ""ShouldSearch"": true,
  ""UserReply"": ""Dạ em tìm giúp bác."",
  ""Keywords"": [""keyword""],
  ""CategoryKeyword"": ""điện thoại"",
  ""Brand"": ""Samsung"",
  ""Model"": ""Galaxy S24"",
  ""Color"": null,
  ""Storage"": null,
  ""Warranty"": null,
  ""Origin"": null,
  ""Condition"": ""Moi"",
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

RULES:
- Return ONLY raw JSON
- No markdown, no code blocks
- Condition: only 'Moi' (new) or 'DaSuDung' (used)
- SortBy: 'recent', 'price_asc', 'price_desc', 'views_desc'
- Keep UserReply under 15 words
- Extract SPECIFIC attributes if mentioned (Brand, Model, Color, Storage, Warranty, Origin)
- If multiple prices mentioned, use Min and Max
";

                // 3. Gọi API
                var raw = await _aiClient.SendPromptAsync(prompt);
                
                if (string.IsNullOrWhiteSpace(raw))
                {
                    _logger.LogWarning("[AI] Gemini returned empty -> Using fallback.");
                    return fallbackResult;
                }

                _lastRawAiResponse = raw;

                // 4. Clean JSON
                var cleanJson = raw.Replace("```json", "").Replace("```", "").Trim();
                var jsonStart = cleanJson.IndexOf('{');
                var jsonEnd = cleanJson.LastIndexOf('}');
                
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    cleanJson = cleanJson.Substring(jsonStart, jsonEnd - jsonStart + 1);
                    try 
                    {
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var result = JsonSerializer.Deserialize<AiIntentResult>(cleanJson, options);
                        
                        // Validate kết quả
                        if (result != null)
                        {
                            if (!result.ShouldSearch)
                                result.ShouldSearch = (result.Keywords != null && result.Keywords.Length > 0);
                            
                            _logger.LogInformation("[AI] ✅ Gemini parsed successfully: Keywords={kw}, Brand={brand}, CategoryKeyword={cat}", 
                                string.Join(",", result.Keywords ?? Array.Empty<string>()), result.Brand, result.CategoryKeyword);
                            
                            // --- TỐI ƯU 1: Ánh xạ CategoryKeyword sang ID thật trong DB ---
                            if (!result.CategoryId.HasValue && !string.IsNullOrEmpty(result.CategoryKeyword))
                            {
                                // Tìm danh mục cha có tên khớp với từ khóa AI đưa ra
                                var category = await _context.DanhMucChas
                                    .AsNoTracking()
                                    .FirstOrDefaultAsync(c => EF.Functions.Like(c.TenDanhMucCha, $"%{result.CategoryKeyword}%"));
                                
                                if (category != null)
                                {
                                    result.CategoryId = category.MaDanhMucCha;
                                    _logger.LogInformation($"[AI FIX] Mapped keyword '{result.CategoryKeyword}' -> CategoryId: {result.CategoryId}");
                                }
                            }
                            // -----------------------------------------------------------
                            
                            return result; // <--- THÀNH CÔNG
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("[AI] JSON Parse Error: {ex} | Raw: {raw}", ex.Message, cleanJson);
                    }
                }
            }
            catch (Exception ex)
            {
                // Bắt lỗi API (401, 500, Timeout...)
                _logger.LogError(ex, "[AI] ❌ GEMINI API FAILED: {Message} | Falling back to keyword extraction", ex.Message);
                _logger.LogError("[AI] Stack: {Stack}", ex.StackTrace);
            }

            // NẾU CÓ BẤT KỲ LỖI GÌ Ở TRÊN -> TRẢ VỀ FALLBACK
            _logger.LogWarning("[AI] Using fallback result with keywords: {keywords}", string.Join(", ", fallbackResult.Keywords ?? Array.Empty<string>()));
            return fallbackResult;
        }
        
    }

    public class AiIntentResult
    {
        public bool ShouldSearch { get; set; }
        public string? UserReply { get; set; }
        public string[]? Keywords { get; set; }
        
        // ✅ THUỘC TÍNH SẢN PHẨM - Được Gemini trích xuất
        public string? CategoryKeyword { get; set; }   // "điện thoại", "laptop", "máy giặt"
        public string? Brand { get; set; }             // "Samsung", "Apple", "LG"
        public string? Model { get; set; }             // "Galaxy S24", "iPhone 15 Pro"
        public string? Color { get; set; }             // "Đen", "Trắng"
        public string? Storage { get; set; }           // "128GB", "256GB"
        public string? Warranty { get; set; }          // "12 tháng", "24 tháng"
        public string? Origin { get; set; }            // "Chính hãng", "Xách tay"
        public string? Condition { get; set; }         // "Moi" hoặc "DaSuDung"
        
        // CÁC FILTER BỔ SUNG
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }
        public bool RequireVideo { get; set; }
        public int? CategoryId { get; set; }
        public string? SortBy { get; set; }            // "recent", "price_asc", "price_desc", "views_desc"
        public bool FilterByHot { get; set; }          // Lọc theo hot tin đăng
        public bool FilterBySeller { get; set; }       // Lọc theo chủ tin đăng cụ thể (nếu có)
        
        public string? ClarifyingQuestion { get; set; }
        public decimal Confidence { get; set; }
        public string? Sort { get; set; }
    }
}
