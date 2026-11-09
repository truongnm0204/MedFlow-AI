namespace MedFlow.Domain.Exceptions;

public class NotFoundException : DomainException
{
    public NotFoundException(string message)
        : base(message)
    {
    }

    public NotFoundException(string entity, object key)
        : base($"{entity} with key '{key}' not found.")
    {
    }
}
