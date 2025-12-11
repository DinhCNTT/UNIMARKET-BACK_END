using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading.Tasks;
using UniMarket.DTO;
using UniMarket.Services;

namespace UniMarket.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AiController : ControllerBase
    {
        private readonly AiService _aiService;
        private readonly ILogger<AiController> _logger;
        private readonly IHostEnvironment _env;

        public AiController(AiService aiService, ILogger<AiController> logger, IHostEnvironment env)
        {
            _aiService = aiService;
            _logger = logger;
            _env = env;
        }

        [HttpPost("chat")]
        public async Task<IActionResult> Chat([FromBody] AiChatRequestDto request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Message))
                return BadRequest("Vui lòng nhập tin nhắn");

            try
            {
                var result = await _aiService.ProcessChatAsync(request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AiController.Chat error");
                return StatusCode(500, "Lỗi AI: " + ex.Message);
            }
        }


    }
}
