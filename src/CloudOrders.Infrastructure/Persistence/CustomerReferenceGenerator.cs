using System.Security.Cryptography;
using CloudOrders.Application.Identity;

namespace CloudOrders.Infrastructure.Persistence;

public sealed class CustomerReferenceGenerator : ICustomerReferenceGenerator
{
    public string Create() => $"CUS-{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}";
}
