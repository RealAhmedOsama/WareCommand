using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Context;

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

    [HttpGet]
    public IActionResult Correlation(
        [FromServices] IRequestContext requestContext,
        [FromServices] IWmsOperationContextAccessor operationContextAccessor)
    {
        return Ok(new
        {
            requestContext.CorrelationId,
            operation = operationContextAccessor.Current?.OperationId,
            operationName = operationContextAccessor.Current?.OperationName,
            reference = operationContextAccessor.Current?.ReferenceId,
            sourceClient = operationContextAccessor.Current?.SourceClient
        });
    }
}
