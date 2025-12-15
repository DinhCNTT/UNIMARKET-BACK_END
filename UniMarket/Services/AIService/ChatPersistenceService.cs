using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UniMarket.DataAccess;
using UniMarket.Models;
using UniMarket.DTO;

namespace UniMarket.Services
{
    /// <summary>
    /// ChatPersistenceService: Chuyên trách lưu tin nhắn AI vào database.
    /// - Tạo hoặc cập nhật conversation
    /// - Lưu user message và AI response
    /// </summary>
    public class ChatPersistenceService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ChatPersistenceService> _logger;

        public ChatPersistenceService(ApplicationDbContext context, ILogger<ChatPersistenceService> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Lưu chat message vào database.
        /// </summary>
        public async Task SaveChatAsync(string userId, string userMessage, AiChatResponseDto aiResponse)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                _logger.LogWarning("[ChatPersistence] Cannot save chat: UserId is null or empty");
                return;
            }

            try
            {
                // 1. Get or create AI user
                var aiUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.NormalizedUserName == "UNI.AI");
                
                if (aiUser == null)
                {
                    _logger.LogWarning("[ChatPersistence] AI user 'UNI.AI' not found. Creating new one.");
                    aiUser = new ApplicationUser
                    {
                        Id = Guid.NewGuid().ToString(),
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
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("[ChatPersistence] Created new AI user: {userId}", aiUser.Id);
                }

                // 2. Get or create conversation
                var chatId = $"ai-assistant-{userId}";
                var chat = await _context.CuocTroChuyens
                    .FirstOrDefaultAsync(c => c.MaCuocTroChuyen == chatId);
                
                if (chat == null)
                {
                    _logger.LogInformation("[ChatPersistence] Creating new conversation: {chatId}", chatId);
                    chat = new CuocTroChuyen
                    {
                        MaCuocTroChuyen = chatId,
                        ThoiGianTao = DateTime.UtcNow,
                        MaTinDang = 0,
                        TieuDeTinDang = "Trợ lý ảo Uni.AI",
                        AnhDaiDienTinDang = "/images/uni-ai-avatar.svg",
                        MaNguoiBan = userId,
                        IsEmpty = false
                    };
                    _context.CuocTroChuyens.Add(chat);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("[ChatPersistence] Conversation created: {chatId}", chatId);
                }
                else
                {
                    chat.IsEmpty = false;
                    await _context.SaveChangesAsync();
                    _logger.LogDebug("[ChatPersistence] Conversation updated: {chatId}", chatId);
                }

                // 3. Verify user exists
                var userExistsInDb = await _context.Users.AnyAsync(u => u.Id == userId);
                if (!userExistsInDb)
                {
                    _logger.LogWarning("[ChatPersistence] User '{userId}' does not exist in database. Skipping message persistence.", userId);
                    return;
                }

                // 4. Add user message
                var userMsg = new TinNhan
                {
                    MaCuocTroChuyen = chat.MaCuocTroChuyen,
                    MaNguoiGui = userId,
                    NoiDung = userMessage,
                    ThoiGianGui = DateTime.UtcNow,
                    Loai = LoaiTinNhan.Text
                };
                _context.TinNhans.Add(userMsg);
                _logger.LogDebug("[ChatPersistence] User message added: '{msg}'", userMessage.Substring(0, Math.Min(50, userMessage.Length)));

                // 5. Add AI response message (serialized JSON with products & reply)
                var aiPayload = new
                {
                    replyText = aiResponse.ReplyText,
                    suggestedProducts = aiResponse.SuggestedProducts,
                    clarifyingQuestion = aiResponse.ClarifyingQuestion
                };

                var aiMsg = new TinNhan
                {
                    MaCuocTroChuyen = chat.MaCuocTroChuyen,
                    MaNguoiGui = aiUser.Id,
                    NoiDung = JsonSerializer.Serialize(aiPayload),
                    ThoiGianGui = DateTime.UtcNow.AddMilliseconds(500),
                    Loai = LoaiTinNhan.Text
                };
                _context.TinNhans.Add(aiMsg);
                _logger.LogDebug("[ChatPersistence] AI message added with {productCount} products", aiResponse.SuggestedProducts?.Count ?? 0);

                // 6. Save all
                await _context.SaveChangesAsync();
                _logger.LogInformation("[ChatPersistence] ✅ Chat messages persisted successfully for conversation: {chatId}", chat.MaCuocTroChuyen);
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, "[ChatPersistence] ❌ Database error while saving chat: {msg}", dbEx.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ChatPersistence] ❌ Unexpected error while saving chat: {msg}", ex.Message);
            }
        }
    }
}
