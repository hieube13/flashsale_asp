namespace FlashSale.Domain.Entities;

/// <summary>
/// TicketDetail entity — maps to MySQL table ticket_item.
/// Mirrors Java com.xxxx.ddd.domain.model.entity.TicketDetail.
/// </summary>
public class TicketDetail
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public int StockInitial { get; set; }
    public int StockAvailable { get; set; }
    public bool IsStockPrepared { get; set; }

    public decimal PriceOriginal { get; set; }
    public decimal PriceFlash { get; set; }

    public DateTime SaleStartTime { get; set; }
    public DateTime SaleEndTime { get; set; }

    /// <summary>0=INACTIVE, 1=ACTIVE, 2=DELETED</summary>
    public int Status { get; set; }

    public long ActivityId { get; set; }

    public DateTime UpdatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}