using Wms.Application.Attachments;
using Wms.Domain.Enums;

namespace Wms.Infrastructure.Attachments;

/// <summary>
/// Safe local default. Production can replace this port with an AV/quarantine
/// adapter; when scanning is required, AttachmentService refuses uploads while
/// this unconfigured implementation is active.
/// </summary>
public sealed class NoOpAttachmentScanner : IAttachmentScanner
{
    public bool IsConfigured => false;

    public Task<AttachmentScanResult> ScanAsync(
        AttachmentScanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new AttachmentScanResult(
            AttachmentScanStatus.NotConfigured,
            "No antivirus scanner is configured for this environment."));
    }
}
