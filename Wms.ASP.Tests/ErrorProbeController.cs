using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Wms.ASP.Tests;

public sealed class ErrorProbeController : Controller
{
    [HttpGet]
    public IActionResult Unexpected()
    {
        _ = HttpContext.TraceIdentifier;
        throw new InvalidOperationException("sensitive provider detail must not reach the client");
    }

    [HttpGet]
    public IActionResult Concurrency()
    {
        _ = HttpContext.TraceIdentifier;
        throw new DbUpdateConcurrencyException("internal concurrency detail");
    }
}
