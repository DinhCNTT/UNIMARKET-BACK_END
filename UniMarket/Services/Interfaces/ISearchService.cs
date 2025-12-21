using System.Collections.Generic;
using System.Threading.Tasks;

namespace UniMarket.Services.Interfaces
{
    public interface ISearchService
    {
        Task LogSearchAsync(string keyword, string? userId, string? sessionId, int resultCount);

        Task<List<string>> GetTrendingKeywordsAsync(string? userId);
        Task<List<string>> GetSmartSuggestionsAsync(string keyword, string? userId);
        Task<List<string>> GetRelatedKeywordsAsync(string keyword);
    }
}