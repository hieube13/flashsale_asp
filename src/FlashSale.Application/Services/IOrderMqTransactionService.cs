using FlashSale.Domain.Entities;

namespace FlashSale.Application.Services;

/// <summary>
/// Writes an OrderQueue row + its OutboxEvent payload in a single DB transaction.
/// <para>
/// Used by <see cref="IOrderMqAppService.PlaceOrderMqAsync"/> (TASK-015 producer)
/// to enforce the transactional-outbox guarantee that Java expresses via
/// <c>TransactionTemplate</c> — if either INSERT fails, both roll back.
/// </para>
/// <para>
/// The interface lives in the Application layer because the *contract*
/// (atomic persistence) belongs to the application's use-case, while the
/// concrete impl that knows about <c>FlashSaleDbContext</c> + EF transactions
/// sits in Infrastructure.
/// </para>
/// </summary>
public interface IOrderMqTransactionService
{
    /// <summary>
    /// Persist both rows atomically. Throws on any failure — the caller is
    /// responsible for compensating side-effects (Redis stock restore).
    /// </summary>
    Task PersistAsync(OrderQueue queue, OutboxEvent outboxEvent, CancellationToken ct = default);
}