using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using WohnungenApi.Data;
using WohnungenApi.Models;
using WohnungenApi.Dtos;


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
        public async Task<IActionResult> SubmitContact([FromBody] ContactDto dto)
        {
            if (dto == null)
                return BadRequest("Empty message");

            var msg = new ContactMessage
            {
                Name = dto.Name,
                Email = dto.Email,
                Subject = dto.Subject,
                Message = dto.Message,
                SentAt = DateTime.UtcNow
            };

            _context.ContactMessages.Add(msg);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, id = msg.Id });
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var messages = await _context.ContactMessages
                .OrderByDescending(m => m.SentAt)
                .ToListAsync();

            return Ok(messages);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var msg = await _context.ContactMessages.FindAsync(id);
            if (msg == null) return NotFound();
            return Ok(msg);
        }

        [HttpPut("{id}/read")]
        public async Task<IActionResult> MarkAsRead(int id)
        {
            var msg = await _context.ContactMessages.FindAsync(id);
            if (msg == null) return NotFound();

            msg.IsRead = true;
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var msg = await _context.ContactMessages.FindAsync(id);
            if (msg == null) return NotFound();

            _context.ContactMessages.Remove(msg);
            await _context.SaveChangesAsync();

            return Ok();
        }

    }

}
