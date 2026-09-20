using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Wms.Application.Common;
using Wms.Application.Localization;

namespace Wms.ASP.Extensions;

public static class WmsResultExtensions
{
    public static string Localize(
        this Controller controller,
        string key,
        params object[] arguments)
    {
        var localizer = controller.HttpContext.RequestServices
            .GetRequiredService<IStringLocalizer<WmsSharedResource>>();
        return localizer[key, arguments].Value;
    }

    public static void AddToModelState(this Controller controller, Result result)
    {
        foreach (var error in result.Errors)
        {
            if (error.FieldErrors is null || error.FieldErrors.Count == 0)
            {
                controller.ModelState.AddModelError(string.Empty, LocalizeError(controller, error));
                continue;
            }

            foreach (var fieldError in error.FieldErrors)
            {
                foreach (var message in fieldError.Value)
                {
                    controller.ModelState.AddModelError(fieldError.Key, LocalizeError(controller, error, message));
                }
            }
        }
    }

    public static string LocalizeError(this Controller controller, ResultError error) =>
        LocalizeError(controller, error, error.Message);

    private static string LocalizeError(
        Controller controller,
        ResultError error,
        string? originalMessage = null)
    {
        var localizer = controller.HttpContext.RequestServices
            .GetRequiredService<IStringLocalizer<WmsSharedResource>>();
        var direct = localizer[originalMessage ?? error.Message];
        if (!direct.ResourceNotFound)
        {
            return direct.Value;
        }

        var key = error.Type switch
        {
            ErrorType.Validation => "Error.Validation",
            ErrorType.NotFound => "Error.NotFound",
            ErrorType.Conflict => "Error.Conflict",
            ErrorType.Unauthorized => "Error.Unauthorized",
            ErrorType.Forbidden => "Error.Forbidden",
            ErrorType.Concurrency => "Error.Concurrency",
            ErrorType.Dependency => "Error.Dependency",
            _ => "Error.Unexpected"
        };

        return localizer[key].Value;
    }
}
