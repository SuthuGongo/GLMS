using System.Net.Mail;
using System.Text.RegularExpressions;
using GLMS.Core.Services;

namespace GLMS.Core.Validation;

/// <summary>
/// Every rule for client details, in one place outside the controllers. Lengths match the database columns
/// and the email rule matches the CHECK constraint CK_Clients_Email_Format, so valid input always saves.
/// </summary>
public static partial class ClientValidator
{
    public const int NameMaxLength = 150;
    public const int ContactMaxLength = 120;
    public const int EmailMaxLength = 256;
    public const int PhoneMaxLength = 30;
    public const int RegionMaxLength = 80;

    /// <summary>Shared with the browser check on the client form.</summary>
    public const string EmailPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";

    public static IReadOnlyList<string> Validate(ClientInput? input)
    {
        if (input is null) return new[] { "Client details are required." };

        var errors = new List<string>();

        Required(input.Name, "Client name", NameMaxLength, errors);
        Required(input.Region, "Region", RegionMaxLength, errors);
        Optional(input.ContactPerson, "Contact person", ContactMaxLength, errors);
        Optional(input.PhoneNumber, "Phone number", PhoneMaxLength, errors);

        if (!IsValidEmail(input.Email))
            errors.Add("A valid email address is required, for example name@company.co.za.");
        else if (input.Email.Trim().Length > EmailMaxLength)
            errors.Add($"The email address can't be longer than {EmailMaxLength} characters.");

        if (!string.IsNullOrWhiteSpace(input.PhoneNumber) && !PhoneShape().IsMatch(input.PhoneNumber.Trim()))
            errors.Add("The phone number can only contain digits, spaces, brackets, dashes and a leading +.");

        return errors;
    }

    public static bool IsValidEmail(string? email)
    {
        var e = (email ?? string.Empty).Trim();
        return e.Length > 0 && MailAddress.TryCreate(e, out _) && EmailShape().IsMatch(e);
    }

    private static void Required(string? value, string label, int max, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) errors.Add($"{label} is required.");
        else if (value.Trim().Length > max) errors.Add($"{label} can't be longer than {max} characters.");
    }

    private static void Optional(string? value, string label, int max, List<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length > max)
            errors.Add($"{label} can't be longer than {max} characters.");
    }

    [GeneratedRegex(EmailPattern)]
    private static partial Regex EmailShape();

    [GeneratedRegex(@"^\+?[0-9 ()\-]{6,30}$")]
    private static partial Regex PhoneShape();
}
