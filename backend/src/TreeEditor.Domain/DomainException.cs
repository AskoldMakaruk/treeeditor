namespace TreeEditor.Domain;

/// <summary>Raised when a request violates a domain rule; mapped to HTTP 400 by the API.</summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }
}
