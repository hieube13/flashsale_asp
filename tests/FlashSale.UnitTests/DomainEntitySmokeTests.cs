using FlashSale.Domain.Entities;
using FlashSale.Domain.Enums;

namespace FlashSale.UnitTests;

/// <summary>
/// Smoke tests for domain entities — ensure shape mirrors Java and constructors/setters work.
/// </summary>
public class DomainEntitySmokeTests
{
    [Fact]
    public void Ticket_CanBeInstantiatedWithValidFields()
    {
        var t = new Ticket
        {
            Id = 1,
            Name = "Concert",
            Description = "x",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddHours(2),
            Status = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        Assert.Equal(1, t.Id);
        Assert.Equal("Concert", t.Name);
        Assert.Equal(1, t.Status);
        Assert.Equal(0, (int)OrderStatus.PENDING);
        Assert.Equal(1, (int)OrderStatus.SUCCESS);
        Assert.Equal(2, (int)OrderStatus.CANCELLED);
    }

    [Fact]
    public void TickerOrder_StatusEnum_MatchesJava()
    {
        Assert.Equal(0, (int)OrderStatus.PENDING);
        Assert.Equal(1, (int)OrderStatus.SUCCESS);
        Assert.Equal(2, (int)OrderStatus.CANCELLED);
        Assert.Equal(3, (int)OrderStatus.EXPIRED);
        Assert.Equal(4, (int)OrderStatus.REFUNDED);
    }

    [Fact]
    public void OrderQueue_StatusEnum_MatchesJava()
    {
        Assert.Equal(0, (int)OrderQueueStatus.PENDING);
        Assert.Equal(1, (int)OrderQueueStatus.SUCCESS);
        Assert.Equal(2, (int)OrderQueueStatus.FAILED);
    }

    [Fact]
    public void OutboxEvent_StatusEnum_MatchesJava()
    {
        Assert.Equal(0, (int)OutboxStatus.PENDING);
        Assert.Equal(1, (int)OutboxStatus.PUBLISHED);
    }

    [Fact]
    public void IdempotencyKey_TokenIsPrimaryKey()
    {
        var k = new IdempotencyKey
        {
            Token = "MQ-abc123",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };

        Assert.Equal("MQ-abc123", k.Token);
        Assert.True(k.ExpiresAt > k.CreatedAt);
    }

    [Fact]
    public void PaymentTransaction_DefaultFieldsSet()
    {
        var p = new PaymentTransaction
        {
            PaymentId = Guid.NewGuid().ToString(),
            OrderNumber = "ORD-001",
            UserId = 1001,
            Amount = 100m,
            PaymentMethod = "VNPAY",
            PaymentStatus = 0
        };

        Assert.Equal(0, p.PaymentStatus); // INIT
        Assert.True(p.PaymentId.Length > 0);
    }
}