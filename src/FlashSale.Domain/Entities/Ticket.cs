namespace FlashSale.Domain.Entities;

/// <summary>
/// Ticket entity — mirrors Java com.xxxx.ddd.domain.model.entity.Ticket.
/// Status: 0=INACTIVE, 1=ACTIVE, 2=DELETED.
/// </summary>
public class Ticket
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }

    /// <summary>0=INACTIVE, 1=ACTIVE, 2=DELETED</summary>
    public int Status { get; set; }

    public DateTime UpdatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}