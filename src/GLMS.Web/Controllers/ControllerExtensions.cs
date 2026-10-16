using GLMS.Core.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace GLMS.Web.Controllers;

internal static class ControllerExtensions
{
    /// <summary>Copies business-rule messages from the Core layer into ModelState so the view shows them.</summary>
    public static void AddBusinessErrors(this ModelStateDictionary modelState, BusinessRuleException ex, string key = "")
    {
        foreach (var error in ex.Errors)
            modelState.AddModelError(key, error);
    }

    public static void Flash(this Controller controller, string message, string type = "success")
    {
        controller.TempData["FlashMessage"] = message;
        controller.TempData["FlashType"] = type;
    }
}
