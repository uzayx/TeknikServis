namespace TeknikServis.Application.Common.Exceptions;

public class NotFoundException : Exception
{
    public NotFoundException(string entityName, Guid id)
        : base($"{entityName} bulunamadi. Id: {id}")
    {
    }
}
