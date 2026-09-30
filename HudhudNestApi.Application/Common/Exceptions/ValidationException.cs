using FluentValidation.Results;

namespace HudhudNestApi.Application.Common.Exceptions;

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

    /// <summary>
    /// Field name -> array of error codes, aligned index-for-index with the messages in
    /// <see cref="Errors"/>.
    ///
    /// A localised client cannot translate an English sentence, so without this the API
    /// forced every caller to display the server's prose -- which is exactly what the
    /// Angular app did to its Arabic and German users on the registration form. Rules
    /// that set no deliberate code contribute FluentValidation's own rule name
    /// ("NotEmptyValidator" and friends); clients are expected to translate the codes
    /// they recognise and fall back to the message for the rest.
    /// </summary>
    public IDictionary<string, string[]> ErrorCodes { get; }

    public ValidationException()
        : base("One or more validation failures occurred.")
    {
        Errors = new Dictionary<string, string[]>();
        ErrorCodes = new Dictionary<string, string[]>();
    }

    public ValidationException(IEnumerable<ValidationFailure> failures)
        : this()
    {
        // Materialised once: the two dictionaries must be built from the same sequence
        // in the same order, or the codes stop lining up with the messages they explain.
        var grouped = failures
            .GroupBy(f => f.PropertyName)
            .ToList();

        Errors = grouped.ToDictionary(
            group => group.Key,
            group => group.Select(f => f.ErrorMessage).ToArray());

        ErrorCodes = grouped.ToDictionary(
            group => group.Key,
            group => group.Select(f => f.ErrorCode ?? string.Empty).ToArray());
    }

    /// <summary>
    /// For a single field rejected inside a handler, where running the whole
    /// FluentValidation pipeline again would be the long way round. Keeps such
    /// rejections in the 422 channel instead of the 500 one.
    /// </summary>
    public ValidationException(
        string propertyName,
        string errorMessage,
        string? errorCode = null)
        : this()
    {
        Errors = new Dictionary<string, string[]>
        {
            [propertyName] = [errorMessage]
        };

        ErrorCodes = new Dictionary<string, string[]>
        {
            [propertyName] = [errorCode ?? string.Empty]
        };
    }
}

