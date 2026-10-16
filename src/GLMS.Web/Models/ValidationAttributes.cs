using System.ComponentModel.DataAnnotations;
using System.Globalization;
using GLMS.Core.Files;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace GLMS.Web.Models;

internal static class ClientValidationHelper
{
    public static void Merge(ClientModelValidationContext context, string key, string value)
    {
        if (!context.Attributes.ContainsKey(key)) context.Attributes.Add(key, value);
    }
}
/// <summary>The date must be after another date on the same form (e.g. EndDate after StartDate).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class DateAfterAttribute : ValidationAttribute, IClientModelValidator
{
    public DateAfterAttribute(string otherProperty, string otherLabel)
    {
        OtherProperty = otherProperty;
        OtherLabel = otherLabel;
        ErrorMessage = "{0} must be after the " + otherLabel.ToLowerInvariant() + ".";
    }

    public string OtherProperty { get; }
    public string OtherLabel { get; }

    protected override ValidationResult? IsValid(object? value, ValidationContext context)
    {
        var other = context.ObjectType.GetProperty(OtherProperty)?.GetValue(context.ObjectInstance);
        if (value is DateTime date && other is DateTime otherDate && date.Date <= otherDate.Date)
            return new ValidationResult(FormatErrorMessage(context.DisplayName), new[] { context.MemberName! });
        return ValidationResult.Success;
    }

    public void AddValidation(ClientModelValidationContext context)
    {
        ClientValidationHelper.Merge(context, "data-val", "true");
        ClientValidationHelper.Merge(context, "data-val-dateafter", FormatErrorMessage(context.ModelMetadata.GetDisplayName()));
        ClientValidationHelper.Merge(context, "data-val-dateafter-other", OtherProperty);
    }
}

/// <summary>A money amount may have at most this many decimal places (cents).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MaxDecimalPlacesAttribute : ValidationAttribute, IClientModelValidator
{
    public MaxDecimalPlacesAttribute(int places)
    {
        Places = places;
        ErrorMessage = "{0} can't have more than " + places + " decimal places.";
    }

    public int Places { get; }

    protected override ValidationResult? IsValid(object? value, ValidationContext context)
    {
        if (value is decimal d && decimal.Round(d, Places) != d)
            return new ValidationResult(FormatErrorMessage(context.DisplayName), new[] { context.MemberName! });
        return ValidationResult.Success;
    }

    public void AddValidation(ClientModelValidationContext context)
    {
        ClientValidationHelper.Merge(context, "data-val", "true");
        ClientValidationHelper.Merge(context, "data-val-maxdecimals", FormatErrorMessage(context.ModelMetadata.GetDisplayName()));
        ClientValidationHelper.Merge(context, "data-val-maxdecimals-places", Places.ToString(CultureInfo.InvariantCulture));
    }
}

/// <summary>PDF only, not empty, at most 10 MB. The server also checks the file's real content.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PdfFileAttribute : ValidationAttribute, IClientModelValidator
{
    public PdfFileAttribute() => ErrorMessage = "Only PDF files up to 10 MB can be uploaded.";

    protected override ValidationResult? IsValid(object? value, ValidationContext context)
    {
        if (value is not IFormFile file) return ValidationResult.Success; // [Required] handles "missing"
        return FileValidator.IsAllowed(file.FileName, file.Length)
            ? ValidationResult.Success
            : new ValidationResult(ErrorMessage, new[] { context.MemberName! });
    }

    public void AddValidation(ClientModelValidationContext context)
    {
        ClientValidationHelper.Merge(context, "data-val", "true");
        ClientValidationHelper.Merge(context, "data-val-pdffile", ErrorMessage!);
        ClientValidationHelper.Merge(context, "data-val-pdffile-maxbytes", FileValidator.MaxFileSizeBytes.ToString(CultureInfo.InvariantCulture));
    }
}

/// <summary>Required only when a checkbox on the same form is ticked (e.g. the PDF when "Activate now" is ticked).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class RequiredIfTrueAttribute : ValidationAttribute, IClientModelValidator
{
    public RequiredIfTrueAttribute(string checkboxProperty, string message)
    {
        CheckboxProperty = checkboxProperty;
        ErrorMessage = message;
    }

    public string CheckboxProperty { get; }

    protected override ValidationResult? IsValid(object? value, ValidationContext context)
    {
        var ticked = context.ObjectType.GetProperty(CheckboxProperty)?.GetValue(context.ObjectInstance) as bool? ?? false;
        var missing = value is null || (value is string s && string.IsNullOrWhiteSpace(s));
        return ticked && missing
            ? new ValidationResult(ErrorMessage, new[] { context.MemberName! })
            : ValidationResult.Success;
    }

    public void AddValidation(ClientModelValidationContext context)
    {
        ClientValidationHelper.Merge(context, "data-val", "true");
        ClientValidationHelper.Merge(context, "data-val-requirediftrue", ErrorMessage!);
        ClientValidationHelper.Merge(context, "data-val-requirediftrue-other", CheckboxProperty);
    }
}
