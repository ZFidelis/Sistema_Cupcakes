using Cupcakes.Api.Contracts;
using Cupcakes.Api.Domain;

namespace Cupcakes.Api.Services;

internal static class OrderResponseMapper
{
    internal static OrderConfirmationResponse Map(Order order) => new(order.Id, order.ConfirmationId,
        order.CreatedAt, order.Status.ToString(), order.PaymentMethod.ToString(), order.PaymentResult,
        new(order.Address.Recipient, order.Address.Street, order.Address.Number, order.Address.Neighborhood,
            order.Address.City, order.Address.State, order.Address.PostalCode, order.Address.Complement),
        order.Items.OrderBy(item => item.ProductName).ThenBy(item => item.ProductId).Select(item =>
            new OrderConfirmationItem(item.ProductId, item.ProductName, item.Quantity, item.OriginalUnitPrice,
                item.AppliedUnitPrice, item.Subtotal, item.Discount, item.Total)).ToArray(),
        order.Subtotal, order.Discount, order.Total);
}
