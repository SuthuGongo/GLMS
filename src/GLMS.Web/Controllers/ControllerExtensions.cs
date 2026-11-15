using System.Reflection;
using GLMS.Web.ApiClients;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace GLMS.Web.Controllers;

internal static class ControllerExtensions
{
    public const string FlashMessageKey = "FlashMessage";
    public const string FlashTypeKey = "FlashType";

    /// <summary>
    /// Copies the API's validation messages into ModelState. A message about a field the form has (e.g. "Title")
    /// is shown under that field; business-rule messages ("general") go to the form's summary.
    /// </summary>
    public static void AddApiErrors(this ModelStateDictionary modelState, ApiValidationException ex, Type? formModel = null)
    {
        var fields = formModel?.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>();

        foreach (var (key, messages) in ex.Errors)
        {
            var field = ApiErrorExtensions.IsGeneralKey(key) ? null : fields.FirstOrDefault(f => string.Equals(f, key, StringComparison.OrdinalIgnoreCase));
            foreach (var message in messages)
                modelState.AddModelError(field ?? string.Empty, message);
        }
    }

    public static void Flash(this Controller controller, string message, string type = "success")
    {
        controller.TempData[FlashMessageKey] = message;
        controller.TempData[FlashTypeKey] = type;
    }
}
