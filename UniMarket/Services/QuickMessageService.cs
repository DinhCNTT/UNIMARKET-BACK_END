using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using UniMarket.DataAccess;
using UniMarket.DTO;
using UniMarket.Models;

namespace UniMarket.Services
{
    public class QuickMessageService : IQuickMessageService
    {
        private readonly ApplicationDbContext _context;

        public QuickMessageService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<QuickMessageDto>> GetQuickMessagesByUserIdAsync(string userId)
        {
            var messages = await _context.QuickMessages
                .Where(qm => qm.UserId == userId)
                .OrderBy(qm => qm.Order)
                .ToListAsync();

            return messages.Select(qm => new QuickMessageDto
            {
                Id = qm.Id,
                Content = qm.Content,
                Order = qm.Order,
                CreatedAt = qm.CreatedAt,
                UpdatedAt = qm.UpdatedAt
            }).ToList();
        }

        public async Task<QuickMessageDto> CreateQuickMessageAsync(string userId, CreateQuickMessageDto dto)
        {
            // Validate
            if (string.IsNullOrWhiteSpace(dto.Content))
                throw new ArgumentException("Nội dung tin nhắn không thể trống");

            if (dto.Order < 1 || dto.Order > 5)
                throw new ArgumentException("Thứ tự phải từ 1 đến 5");

            // Check if user already has a message at this order
            var existingMessage = await _context.QuickMessages
                .FirstOrDefaultAsync(qm => qm.UserId == userId && qm.Order == dto.Order);

            if (existingMessage != null)
                throw new InvalidOperationException($"Vị trí {dto.Order} đã có tin nhắn nhanh");

            // Check total count (max 5)
            var count = await _context.QuickMessages.CountAsync(qm => qm.UserId == userId);
            if (count >= 5)
                throw new InvalidOperationException("Chỉ có thể tạo tối đa 5 tin nhắn nhanh");

            var message = new QuickMessage
            {
                UserId = userId,
                Content = dto.Content.Trim(),
                Order = dto.Order,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _context.QuickMessages.Add(message);
            await _context.SaveChangesAsync();

            return new QuickMessageDto
            {
                Id = message.Id,
                Content = message.Content,
                Order = message.Order,
                CreatedAt = message.CreatedAt,
                UpdatedAt = message.UpdatedAt
            };
        }

        public async Task<QuickMessageDto> UpdateQuickMessageAsync(string userId, UpdateQuickMessageDto dto)
        {
            // Validate
            if (string.IsNullOrWhiteSpace(dto.Content))
                throw new ArgumentException("Nội dung tin nhắn không thể trống");

            if (dto.Order < 1 || dto.Order > 5)
                throw new ArgumentException("Thứ tự phải từ 1 đến 5");

            var message = await _context.QuickMessages
                .FirstOrDefaultAsync(qm => qm.Id == dto.Id && qm.UserId == userId);

            if (message == null)
                throw new KeyNotFoundException("Tin nhắn nhanh không tìm thấy");

            // Check if new order position is already taken by another message
            if (message.Order != dto.Order)
            {
                var existingAtNewOrder = await _context.QuickMessages
                    .FirstOrDefaultAsync(qm => qm.UserId == userId && qm.Order == dto.Order && qm.Id != dto.Id);

                if (existingAtNewOrder != null)
                    throw new InvalidOperationException($"Vị trí {dto.Order} đã có tin nhắn nhanh khác");
            }

            message.Content = dto.Content.Trim();
            message.Order = dto.Order;
            message.UpdatedAt = DateTimeOffset.UtcNow;

            _context.QuickMessages.Update(message);
            await _context.SaveChangesAsync();

            return new QuickMessageDto
            {
                Id = message.Id,
                Content = message.Content,
                Order = message.Order,
                CreatedAt = message.CreatedAt,
                UpdatedAt = message.UpdatedAt
            };
        }

        public async Task<bool> DeleteQuickMessageAsync(string userId, int messageId)
        {
            var message = await _context.QuickMessages
                .FirstOrDefaultAsync(qm => qm.Id == messageId && qm.UserId == userId);

            if (message == null)
                return false;

            _context.QuickMessages.Remove(message);
            await _context.SaveChangesAsync();

            return true;
        }
    }
}
