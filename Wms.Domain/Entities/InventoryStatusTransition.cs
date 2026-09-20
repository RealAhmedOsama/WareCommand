using Wms.Domain.Common;

namespace Wms.Domain.Entities;

public sealed class InventoryStatusTransition : Entity
{
    private InventoryStatusTransition()
    {
    }

    public InventoryStatusTransition(
        int fromInventoryStatusId,
        int toInventoryStatusId,
        bool requiresReason = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fromInventoryStatusId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(toInventoryStatusId);

        if (toInventoryStatusId == fromInventoryStatusId)
        {
            throw new ArgumentException(
                "A status transition must target a different status.",
                nameof(toInventoryStatusId));
        }

        FromInventoryStatusId = fromInventoryStatusId;
        ToInventoryStatusId = toInventoryStatusId;
        RequiresReason = requiresReason;
    }

    public int FromInventoryStatusId { get; private set; }
    public int ToInventoryStatusId { get; private set; }
    public bool RequiresReason { get; private set; } = true;
    public bool IsActive { get; private set; } = true;

    public InventoryStatus FromInventoryStatus { get; private set; } = null!;
    public InventoryStatus ToInventoryStatus { get; private set; } = null!;

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        SetUpdatedAt();
    }

    public void Update(bool requiresReason, bool isActive)
    {
        RequiresReason = requiresReason;
        IsActive = isActive;
        SetUpdatedAt();
    }
}
