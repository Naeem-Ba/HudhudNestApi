using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using WohnungenApi.Data;
using WohnungenApi.Models;

namespace WohnungenApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ContactController : ControllerBase
    {
        private readonly WohnungenContext _context;

        public ContactController(WohnungenContext context)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> SubmitContact(ContactMessage message)
        {
            if (message == null)
                return BadRequest("Empty message");

            _context.ContactMessages.Add(message);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, id = message.Id });
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var messages = await _context.ContactMessages
                .OrderByDescending(m => m.SentAt)
                .ToListAsync();

            return Ok(messages);
        }

    }
    public class ContactDto
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public string Subject { get; set; }
        public string Message { get; set; }
    }

}
