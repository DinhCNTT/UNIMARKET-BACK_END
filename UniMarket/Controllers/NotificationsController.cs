using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniMarket.DataAccess;
using UniMarket.Models;

namespace UniMarket.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class NotificationsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public NotificationsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/notifications?page=1&pageSize=50
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetNotifications([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0 || pageSize > 500) pageSize = 50;

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            try
            {
                var query = _context.Notifications
                    .Where(n => n.UserId == userId)
                    .OrderByDescending(n => n.CreatedAt);

                var total = await query.CountAsync();
                var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

                return Ok(new { total, page, pageSize, items });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"NotificationsController: error fetching notifications: {ex.Message}");
                return Ok(new { total = 0, page, pageSize, items = new List<Notification>() });
            }
        }

        // PUT: api/notifications/{id}/mark-read
        [HttpPut("{id}/mark-read")]
        [Authorize]
        public async Task<IActionResult> MarkRead(int id)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            try
            {
                var notif = await _context.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
                if (notif == null) return NotFound();
                notif.IsRead = true;
                await _context.SaveChangesAsync();
                return Ok(new { message = "Marked as read." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"NotificationsController: error marking read: {ex.Message}");
                return StatusCode(500, new { message = "Internal error marking notification." });
            }
        }
    }
}
