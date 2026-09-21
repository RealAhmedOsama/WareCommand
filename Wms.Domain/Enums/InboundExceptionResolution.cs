namespace Wms.Domain.Enums;

public enum InboundExceptionResolution
{
    Hold = 1,
    SupervisorReview = 2,
    CorrectSourceData = 3,
    BlindReceiptApproval = 4,
    ReturnToVendor = 5,
    Quarantine = 6,
    RepackRelabel = 7,
    Cancel = 8
}
