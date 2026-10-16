using GLMS.Core.Domain;
using GLMS.Core.Services;

namespace GLMS.Core.Validation;
public static class ContractValidator
{
    public const int TitleMaxLength = 200;
    public const int MaxTermYears = 10;
    public static IReadOnlyList<string> Validate(ContractInput? input)
    {
        if (input is null) return new[] { "Contract details are required." };

        var errors = new List<string>();

        if (input.ClientId <= 0)
            errors.Add("Choose a client.");

        if (string.IsNullOrWhiteSpace(input.Title))
            errors.Add("A contract title is required.");
        else if (input.Title.Trim().Length > TitleMaxLength)
            errors.Add($"The contract title can't be longer than {TitleMaxLength} characters.");

        if (input.StartDate == default || input.EndDate == default)
            errors.Add("Both a start date and an end date are required.");
        else if (input.EndDate.Date <= input.StartDate.Date)
            errors.Add("The end date must be after the start date.");
        else if (input.EndDate.Date > input.StartDate.Date.AddYears(MaxTermYears))
            errors.Add($"A contract term can't be longer than {MaxTermYears} years.");

        if (!Enum.IsDefined(input.ServiceLevel))
            errors.Add("Choose a valid service level.");

        return errors;
    }
    public static IReadOnlyList<string> ValidateActivation(ContractInput input, bool hasSignedAgreement, DateTime today)
    {
        var errors = new List<string>();
        if (!hasSignedAgreement)
            errors.Add("Upload the signed agreement (PDF) to create the contract as Active, or save it as a Draft.");
        if (input.EndDate.Date < today.Date)
            errors.Add("A contract whose end date has already passed can't be activated.");
        return errors;
    }

    public static IReadOnlyList<string> ValidateStatus(ContractStatus status) =>
        Enum.IsDefined(status) ? Array.Empty<string>() : new[] { "That is not a valid contract status." };
}
