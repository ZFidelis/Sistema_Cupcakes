using Cupcakes.Api.Contracts;
using Cupcakes.Api.Domain;

namespace Cupcakes.Api.Services;

public sealed class CheckoutPreviewService(CartService carts)
{
    public async Task<CheckoutPreviewResponse> PreviewAsync(string userId, OrderAddress address, CancellationToken ct)
    {
        var cart = await carts.GetAsync(userId, ct);
        var issues = new List<CheckoutIssue>();
        if (cart.Items.Length == 0)
            issues.Add(new("empty_cart", "Adicione produtos ao carrinho antes de continuar."));

        foreach (var item in cart.Items.Where(item => !item.IsAvailable))
            issues.Add(new("unavailable_product", item.AvailabilityMessage ?? "Produto indisponivel.", item.ProductId));

        if (cart.Subtotal > Product.MaximumPrice || cart.Total > Product.MaximumPrice)
            issues.Add(new("amount_limit", "O valor do carrinho excede o limite suportado para um pedido. Reduza as quantidades."));

        return new CheckoutPreviewResponse(new CheckoutAddressResponse(address.Recipient, address.Street,
            address.Number, address.Neighborhood, address.City, address.State, address.PostalCode, address.Complement),
            cart, issues.Count == 0, issues.ToArray(),
            "Resumo para conferencia, sem criar pedido ou processar pagamento. Nao ha reserva de estoque. Precos e disponibilidade devem ser revalidados ao confirmar a compra.");
    }
}
