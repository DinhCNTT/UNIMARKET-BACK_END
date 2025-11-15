using System.Collections.Generic;
using System.Threading.Tasks;
using UniMarket.DTO;

namespace UniMarket.Services
{
    public interface IQuickMessageService
    {
        Task<List<QuickMessageDto>> GetQuickMessagesByUserIdAsync(string userId);
        Task<QuickMessageDto> CreateQuickMessageAsync(string userId, CreateQuickMessageDto dto);
        Task<QuickMessageDto> UpdateQuickMessageAsync(string userId, UpdateQuickMessageDto dto);
        Task<bool> DeleteQuickMessageAsync(string userId, int messageId);
    }
}
