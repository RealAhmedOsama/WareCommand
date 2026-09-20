namespace Wms.Domain.Enums;

/// <summary>
/// The business event represented by an immutable inventory ledger entry.
/// A transaction is a balance delta, not an event-sourced aggregate event.
/// </summary>
public enum InventoryTransactionType
{
    OpeningBalance = 1,
    Receipt = 2,
    Putaway = 3,
    Reservation = 4,
    Release = 5,
    Pick = 6,
    Pack = 7,
    Ship = 8,
    Transfer = 9,
    Adjustment = 10,
    CountVariance = 11,
    Return = 12,
    StatusChange = 13,
    Replenishment = 14,
    Reversal = 15
}
