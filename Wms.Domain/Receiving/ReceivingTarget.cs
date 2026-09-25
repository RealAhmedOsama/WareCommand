using Wms.Domain.Entities;

namespace Wms.Domain.Receiving;

public sealed record ReceivingTarget(Item Item, Location? Location);
