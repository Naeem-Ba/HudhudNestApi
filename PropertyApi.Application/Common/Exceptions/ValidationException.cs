using FluentValidation.Results;

namespace PropertyApi.Application.Common.Exceptions;

/// <summary>
/// Thrown by ValidationBehavior when a Command/Query fails FluentValidation.
/// Contains a dictionary of field ? error messages for structured API responses.
///
/// BUG FIX: Was an empty "internal class ValidationException {}" — completely non-functional.
/// </summary>
public sealed class ValidationException : Exception
{
    /// <summary>
    /// Field name ? array of error messages for that field.
    /// e.g. { "Title": ["Title is required.", "Title must be under 200 chars."] }
    /// </summary>
    public IDictionary<string, string[]> Errors { get; }

    public ValidationException()
        : base("One or more validation failures occurred.")
    {
        Errors = new Dictionary<string, string[]>();
    }

    public ValidationException(IEnumerable<ValidationFailure> failures)
        : this()
    {
        Errors = failures
            .GroupBy(f => f.PropertyName, f => f.ErrorMessage)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray());
    }
}
