namespace Wms.ASP.Models;

public class ErrorViewModel
{
    public string? RequestId { get; set; }

    public string? ErrorReference { get; set; }

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    public bool ShowErrorReference => !string.IsNullOrEmpty(ErrorReference);
}
