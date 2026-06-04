using PropertyApi.Domain.Common.Entities;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Domain.Messaging.Entities;

/// <summary>
/// Direct message between two users about a specific property.
/// </summary>
public class Message : BaseEntity
{
    public string Content { get; set; } = string.Empty;

    // -- Participants --------------------------------------------
    public Guid SenderId { get; set; }
    public Guid ReceiverId { get; set; }       

    // -- Related Property ---------------------------------------
    public Guid PropertyId { get; set; }

    // -- Read Status --------------------------------------------
    public bool IsRead { get; set; } = false;     
    public DateTime? ReadAt { get; set; }               

    // -- Navigation ---------------------------------------------
    public User? Sender { get; set; }
    public Property? Property { get; set; }
}
