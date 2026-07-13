namespace FlashSale.Domain.Entities;

/// <summary>
/// Booking entity — mirrors Java Booking.
/// Status: 0=PENDING, 1=CONFIRMED, 2=CANCELLED.
/// </summary>
public class Booking
{
    public long Id { get; set; }
    public long TicketId { get; set; }
    public int Quantity { get; set; }
    public string BookingCode { get; set; } = string.Empty;

    /// <summary>0=PENDING, 1=CONFIRMED, 2=CANCELLED</summary>
    public int Status { get; set; }

    public DateTime CreatedAt { get; set; }
}