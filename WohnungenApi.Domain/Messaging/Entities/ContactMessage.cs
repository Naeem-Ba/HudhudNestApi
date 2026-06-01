using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WohnungenApi.Domain.Common.Entities;

namespace WohnungenApi.Domain.Messaging.Entities;

/// <summary>
/// General platform contact form submission (not tied to a property).
/// </summary>
public class ContactMessage : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string Body { get; set; } = string.Empty;

    public bool IsRead { get; set; } = false;
    public DateTime? ReadAt { get; set; }
    public string? IpAddress { get; set; }  // For spam detection
}


