using Microsoft.AspNetCore.Mvc;
using Wms.Application.Common;

namespace Wms.ASP.Extensions;

public static class WmsResultExtensions
{
    public static void AddToModelState(this Controller controller, Result result)
    {
        foreach (var error in result.Errors)
        {
            if (error.FieldErrors is null || error.FieldErrors.Count == 0)
            {
                controller.ModelState.AddModelError(string.Empty, error.Message);
                continue;
            }

            foreach (var fieldError in error.FieldErrors)
            {
                foreach (var message in fieldError.Value)
                {
                    controller.ModelState.AddModelError(fieldError.Key, message);
                }
            }
        }
    }
}
