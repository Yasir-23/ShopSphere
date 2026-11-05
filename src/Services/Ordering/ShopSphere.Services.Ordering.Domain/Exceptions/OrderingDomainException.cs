namespace ShopSphere.Services.Ordering.Domain.Exceptions;

public class OrderingDomainException : Exception
{
    public OrderingDomainException(string message) : base(message)
    {
    }

    public OrderingDomainException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

