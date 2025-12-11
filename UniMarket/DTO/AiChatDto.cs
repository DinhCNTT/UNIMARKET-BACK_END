using System.Collections.Generic;

namespace UniMarket.DTO
{
    // Message item for conversation history
    public class AiChatMessageDto
    {
        public string Role { get; set; } = "user"; // "user" or "assistant"
        public string Content { get; set; } = string.Empty;
    }

    // Dữ liệu nhận từ React
    public class AiChatRequestDto
    {
        public string Message { get; set; } = string.Empty;
        public string? UserId { get; set; }
        // Thêm lịch sử chat (tối đa 5-10 tin gần nhất)
        public List<AiChatMessageDto>? History { get; set; }
    }

    // Dữ liệu trả về cho React
    public class AiChatResponseDto
    {
        public string ReplyText { get; set; } = string.Empty;
        public List<ProductSuggestionDto>? SuggestedProducts { get; set; }
        // If the assistant needs more info from the user, this will contain a short question
        public string? ClarifyingQuestion { get; set; }
        // Debug: raw LLM response (only used for diagnostics)
        public string? DebugRaw { get; set; }
        // Metadata about results
        public bool IsSingleSeller { get; set; } = false;  // All results from one seller
        public bool IsPopular { get; set; } = false;       // Results have high views/likes
    }

    public class ProductSuggestionDto
    {
        public int Id { get; set; }
        public string? Ten { get; set; }
        public decimal Gia { get; set; }
        public string? AnhDaiDien { get; set; }
        public string? TinhTrang { get; set; }
        public string? LinkVideo { get; set; }
        // Popularity metrics for AI ranking
        public int SoLuotXem { get; set; } = 0;  // View count
        public int SoLike { get; set; } = 0;     // Like/save count
        public string? MaNguoiBan { get; set; }  // Seller ID (to detect single-seller results)
        public bool IsHot { get; set; } = false; // Hot post (2+ likes/saves)
    }

    // Class phụ để hứng kết quả phân tích JSON từ AI
    public class AiIntentResult
    {
        public string[]? Keywords { get; set; }
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }
        public bool RequireVideo { get; set; }
        public string? Condition { get; set; } // e.g. "New", "Used", "LikeNew"
        public string? Sort { get; set; } // e.g. "relevance", "price_asc", "price_desc", "date"
        public int? CategoryId { get; set; }
        public decimal? Confidence { get; set; }
        public string? UserReply { get; set; }
        // Optional clarifying question suggested by the AI if input is underspecified
        public string? ClarifyingQuestion { get; set; }
        // Whether the assistant intends that the server perform a product search
        public bool? ShouldSearch { get; set; }
    }
}
