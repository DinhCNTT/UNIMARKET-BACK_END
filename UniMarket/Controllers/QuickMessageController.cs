using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniMarket.DTO;
using UniMarket.Services;

namespace UniMarket.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class QuickMessageController : ControllerBase
    {
        private readonly IQuickMessageService _quickMessageService;

        public QuickMessageController(IQuickMessageService quickMessageService)
        {
            _quickMessageService = quickMessageService;
        }

        // Helper: try multiple claim types to find the authenticated user id
        private string? GetUserIdFromClaims()
        {
            if (User == null) return null;
            // Common JWT claim names and ASP.NET mappings
            var claimKeys = new[] { "sub", "uid", "id", "nameid", System.Security.Claims.ClaimTypes.NameIdentifier, "unique_name" };
            foreach (var key in claimKeys)
            {
                var claim = User.FindFirst(key);
                if (claim != null && !string.IsNullOrEmpty(claim.Value))
                    return claim.Value;
            }

            // Fallback: try any claim that looks like an identifier
            var idClaim = User.FindFirst(c => c.Type.EndsWith("id", StringComparison.OrdinalIgnoreCase) || c.Type.EndsWith("identifier", StringComparison.OrdinalIgnoreCase));
            return idClaim?.Value;
        }

        /// <summary>
        /// Lấy danh sách tin nhắn nhanh của người dùng hiện tại
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetMyQuickMessages()
        {
            try
            {
                // Debug: Log request headers
                var authHeader = Request.Headers["Authorization"].ToString();
                Console.WriteLine($"[QuickMessageController] Authorization header: {authHeader}");

                var userId = GetUserIdFromClaims();
                Console.WriteLine($"[QuickMessageController] UserId from claims: {userId}");

                if (string.IsNullOrEmpty(userId))
                {
                    Console.WriteLine("[QuickMessageController] UserId is empty - returning 401");
                    return Unauthorized(new { message = "Không thể xác định người dùng" });
                }

                var messages = await _quickMessageService.GetQuickMessagesByUserIdAsync(userId);
                return Ok(new { success = true, data = messages });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[QuickMessageController] Error: {ex.Message}");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Thêm tin nhắn nhanh mới
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateQuickMessage([FromBody] CreateQuickMessageDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                var userId = GetUserIdFromClaims();
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { message = "Không thể xác định người dùng" });

                var result = await _quickMessageService.CreateQuickMessageAsync(userId, dto);
                return Ok(new { success = true, data = result, message = "Tạo tin nhắn nhanh thành công" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[QuickMessageController] Unexpected error in CreateQuickMessage: {ex}");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Cập nhật tin nhắn nhanh
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateQuickMessage(int id, [FromBody] UpdateQuickMessageDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                var userId = GetUserIdFromClaims();
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { message = "Không thể xác định người dùng" });

                dto.Id = id;
                var result = await _quickMessageService.UpdateQuickMessageAsync(userId, dto);
                return Ok(new { success = true, data = result, message = "Cập nhật tin nhắn nhanh thành công" });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Xóa tin nhắn nhanh
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteQuickMessage(int id)
        {
            try
            {
                var userId = GetUserIdFromClaims();
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { message = "Không thể xác định người dùng" });

                var result = await _quickMessageService.DeleteQuickMessageAsync(userId, id);

                if (!result)
                    return NotFound(new { success = false, message = "Tin nhắn nhanh không tìm thấy" });

                return Ok(new { success = true, message = "Xóa tin nhắn nhanh thành công" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }
}
