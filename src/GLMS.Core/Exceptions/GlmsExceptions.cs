namespace GLMS.Core.Exceptions;

/// <summary>A business rule was broken. Messages are safe to show to the user.</summary>
public class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : this(new[] { message }) { }

    public BusinessRuleException(IEnumerable<string> errors)
        : base(string.Join(" ", errors))
    {
        Errors = errors.ToList().AsReadOnly();
    }

    public IReadOnlyList<string> Errors { get; }
}

/// <summary>A status change that the workflow does not allow (e.g. Expired -> Active).</summary>
public class InvalidStatusTransitionException : BusinessRuleException
{
    public InvalidStatusTransitionException(string message) : base(message) { }
}

/// <summary>Someone else saved a change to the same record first (optimistic concurrency).</summary>
public class ConcurrencyException : BusinessRuleException
{
    public ConcurrencyException(string message) : base(message) { }
}

/// <summary>An uploaded file failed validation (wrong type, empty, too big, not really a PDF...).</summary>
public class InvalidFileException : BusinessRuleException
{
    public InvalidFileException(string message) : base(message) { }
}

/// <summary>The exchange-rate source could not supply a usable rate.</summary>
public class CurrencyConversionException : Exception
{
    public CurrencyConversionException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>A requested record does not exist.</summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}
