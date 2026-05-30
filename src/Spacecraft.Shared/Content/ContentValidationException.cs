namespace Spacecraft.Shared.Content;

/// <summary>Thrown when loaded content definitions fail cross-reference validation.</summary>
public sealed class ContentValidationException : Exception
{
    public IReadOnlyList<string> Problems { get; }

    public ContentValidationException(IReadOnlyList<string> problems)
        : base("Content validation failed:" + Environment.NewLine + string.Join(Environment.NewLine, problems))
    {
        Problems = problems;
    }
}
