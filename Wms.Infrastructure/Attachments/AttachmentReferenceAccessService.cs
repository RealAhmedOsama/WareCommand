using Microsoft.EntityFrameworkCore;
using Wms.Application.Attachments;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Attachments;

public sealed class AttachmentReferenceAccessService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService) : IAttachmentReferenceAccessService
{
    public async Task<Result> AuthorizeAsync(
        AttachmentReferenceAccessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AttachmentReferenceTypes.TryNormalize(request.ReferenceType, out var referenceType) ||
            string.IsNullOrWhiteSpace(request.ReferenceId) ||
            request.ReferenceId.Length > 200 ||
            request.WarehouseId <= 0)
        {
            return Result.Failure(WmsErrors.Validation(
                "attachments.reference_invalid",
                "The attachment reference is invalid."));
        }

        var referenceId = request.ReferenceId.Trim();
        if (referenceId.Any(character => char.IsControl(character) || character is '/' or '\\'))
        {
            return Result.Failure(WmsErrors.Validation(
                "attachments.reference_invalid",
                "The attachment reference is invalid."));
        }

        var manage = request.Mode == AttachmentAccessMode.Manage;
        var permission = RequiredPermission(referenceType, manage);
        var authorization = await warehouseAccessService.AuthorizeAsync(
            permission,
            request.WarehouseId,
            cancellationToken);

        if (referenceType == AttachmentReferenceTypes.Damage)
        {
            authorization = await AuthorizeDamageAsync(
                manage,
                request.WarehouseId,
                cancellationToken);
        }

        if (authorization.IsFailure)
        {
            return authorization;
        }

        if (!int.TryParse(referenceId, out var id) || id <= 0)
        {
            return Result.Failure(WmsErrors.Validation(
                "attachments.reference_id_invalid",
                "The attachment reference id must identify an existing record."));
        }

        var exists = referenceType switch
        {
            AttachmentReferenceTypes.Receipt => await context.Receipts.AnyAsync(
                row => row.Id == id && row.WarehouseId == request.WarehouseId,
                cancellationToken),
            AttachmentReferenceTypes.QualityInspection => await context.QualityInspections.AnyAsync(
                row => row.Id == id && row.WarehouseId == request.WarehouseId,
                cancellationToken),
            AttachmentReferenceTypes.InboundException => await context.InboundExceptions.AnyAsync(
                row => row.Id == id && row.WarehouseId == request.WarehouseId,
                cancellationToken),
            AttachmentReferenceTypes.OutboundException => await context.OutboundExceptions.AnyAsync(
                row => row.Id == id && row.WarehouseId == request.WarehouseId,
                cancellationToken),
            AttachmentReferenceTypes.Return => await context.ReturnAuthorizations.AnyAsync(
                row => row.Id == id && row.WarehouseId == request.WarehouseId,
                cancellationToken),
            AttachmentReferenceTypes.Approval => await context.ApprovalRequests.AnyAsync(
                row => row.Id == id && row.WarehouseId == request.WarehouseId,
                cancellationToken),
            AttachmentReferenceTypes.Shipment => await context.Shipments.AnyAsync(
                row => row.Id == id && row.WarehouseId == request.WarehouseId,
                cancellationToken),
            AttachmentReferenceTypes.AuditEvidence => await context.AuditEntries.AnyAsync(
                row => row.Id == id && row.WarehouseId == request.WarehouseId,
                cancellationToken),
            AttachmentReferenceTypes.Damage => await context.InboundExceptions.AnyAsync(
                    row => row.Id == id && row.WarehouseId == request.WarehouseId,
                    cancellationToken) ||
                await context.OutboundExceptions.AnyAsync(
                    row => row.Id == id && row.WarehouseId == request.WarehouseId,
                    cancellationToken),
            _ => false
        };

        return exists
            ? Result.Success()
            : Result.Failure(WmsErrors.NotFound(
                "attachments.reference_not_found",
                "The referenced warehouse record was not found."));
    }

    private async Task<Result> AuthorizeDamageAsync(
        bool manage,
        int warehouseId,
        CancellationToken cancellationToken)
    {
        var inbound = await warehouseAccessService.AuthorizeAsync(
            manage ? WmsPermissions.AdvanceShippingNoticesManage : WmsPermissions.AdvanceShippingNoticesRead,
            warehouseId,
            cancellationToken);
        if (inbound.IsSuccess)
        {
            return inbound;
        }

        return await warehouseAccessService.AuthorizeAsync(
            manage ? WmsPermissions.SalesOrdersManage : WmsPermissions.SalesOrdersRead,
            warehouseId,
            cancellationToken);
    }

    private static string RequiredPermission(string referenceType, bool manage) =>
        referenceType switch
        {
            AttachmentReferenceTypes.Receipt => manage
                ? WmsPermissions.ReceiptsManage
                : WmsPermissions.ReceiptsRead,
            AttachmentReferenceTypes.QualityInspection => manage
                ? WmsPermissions.QualityInspect
                : WmsPermissions.QualityRead,
            AttachmentReferenceTypes.InboundException => manage
                ? WmsPermissions.AdvanceShippingNoticesManage
                : WmsPermissions.AdvanceShippingNoticesRead,
            AttachmentReferenceTypes.OutboundException => manage
                ? WmsPermissions.SalesOrdersManage
                : WmsPermissions.SalesOrdersRead,
            AttachmentReferenceTypes.Return => WmsPermissions.ReceivingExecute,
            AttachmentReferenceTypes.Approval => manage
                ? WmsPermissions.ApprovalManage
                : WmsPermissions.ApprovalRead,
            AttachmentReferenceTypes.Shipment => WmsPermissions.ShippingExecute,
            AttachmentReferenceTypes.AuditEvidence => WmsPermissions.AuditRead,
            AttachmentReferenceTypes.Damage => WmsPermissions.AdvanceShippingNoticesRead,
            _ => WmsPermissions.All
        };
}
