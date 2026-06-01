using Microsoft.AspNetCore.Mvc;
using WohnungenApi.Data;
using WohnungenApi.Models;
using Microsoft.EntityFrameworkCore;
using WohnungenApi.Dtos;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace WohnungenApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BenutzerController : ControllerBase
    {
        private readonly UserManager<Benutzer> _userManager;
        private readonly RoleManager<IdentityRole<int>> _roleManager;

        public BenutzerController(
            UserManager<Benutzer> userManager,
            RoleManager<IdentityRole<int>> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
        }

        // ----------------------------
        // UPDATE ROLE
        // ----------------------------
        [Authorize(Roles = "Admin")]
        [HttpPut("{id}/role")]
        public async Task<IActionResult> UpdateRole(int id, [FromBody] string newRole)
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null)
                return NotFound("User not found.");

            if (!await _roleManager.RoleExistsAsync(newRole))
                return BadRequest("Role does not exist");

            var currentRoles = await _userManager.GetRolesAsync(user);

            await _userManager.RemoveFromRolesAsync(user, currentRoles);
            var result = await _userManager.AddToRoleAsync(user, newRole);

            if (!result.Succeeded)
                return BadRequest(result.Errors);

            return Ok("Role updated successfully");
        }

        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var roles = await _userManager.GetRolesAsync(user);

            return Ok(new
            {
                user.Email,
                Roles = roles
            });
        }
    }
}
