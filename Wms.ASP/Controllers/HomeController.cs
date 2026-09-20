using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Wms.ASP.Errors;
using Wms.ASP.Models;

namespace Wms.ASP.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        var errorReference = HttpContext.Items[WmsErrorHandling.ErrorReferenceItem] as string;
        if (errorReference is null &&
            HttpContext.Features.Get<IExceptionHandlerPathFeature>()?.Error is not null)
        {
            errorReference = HttpContext.TraceIdentifier;
        }

        return View(new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
            ErrorReference = errorReference
        });
    }
}
