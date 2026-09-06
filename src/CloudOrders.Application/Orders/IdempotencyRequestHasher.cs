using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CloudOrders.Domain.Orders;

namespace CloudOrders.Application.Orders;

public static class IdempotencyRequestHasher
{
    public static byte[] Compute(Guid actorCustomerProfileId, Guid targetCustomerProfileId, Order order)
    {
        var canonicalPayload = string.Join(
            '|',
            "v1",
            actorCustomerProfileId.ToString("D", CultureInfo.InvariantCulture),
            targetCustomerProfileId.ToString("D", CultureInfo.InvariantCulture),
            order.CustomerReference,
            order.ProductSku,
            order.Quantity.ToString(CultureInfo.InvariantCulture));
        return SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload));
    }

    public static byte[] Compute(string subjectId, Order order)
    {
        var canonicalPayload = string.Join(
            '|',
            "v1",
            subjectId,
            order.CustomerReference,
            order.ProductSku,
            order.Quantity.ToString(CultureInfo.InvariantCulture));
        return SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload));
    }
}
