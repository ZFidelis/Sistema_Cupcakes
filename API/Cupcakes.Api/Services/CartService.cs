using Cupcakes.Api.Contracts;
using Cupcakes.Api.Data;
using Cupcakes.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cupcakes.Api.Services;

public sealed class CartService(CupcakesDbContext db)
{
    public async Task<CartResponse> GetAsync(string userId, CancellationToken cancellationToken)
    {
        var items = await db.CartItems.AsNoTracking()
            .Where(item => db.Carts.Any(cart => cart.Id == item.CartId && cart.UserId == userId))
            .Include(item => item.Product).OrderBy(item => item.Product.Name).ThenBy(item => item.ProductId)
            .ToArrayAsync(cancellationToken);

        var responseItems = items.Select(item =>
        {
            var product = item.Product;
            var subtotal = product.Price * item.Quantity;
            var total = product.EffectivePrice * item.Quantity;
            var available = product.IsActive && product.Stock >= item.Quantity;
            var message = !product.IsActive ? "Produto indisponivel. Remova o item do carrinho."
                : !available ? "Estoque insuficiente. Ajuste a quantidade ou remova o item." : null;
            return new CartItemResponse(product.Id, product.Name, product.ImagePath, item.Quantity,
                product.Price, product.PromotionalPrice, product.EffectivePrice, subtotal,
                subtotal - total, total, product.Stock, available, message);
        }).ToArray();

        return new CartResponse(responseItems, responseItems.Sum(item => (long)item.Quantity),
            responseItems.Sum(item => item.Subtotal), responseItems.Sum(item => item.Discount),
            responseItems.Sum(item => item.Total), responseItems.Any(item => !item.IsAvailable),
            "Valores em BRL. Frete e impostos adicionais nao sao calculados. O carrinho nao reserva estoque; precos e disponibilidade serao revalidados na compra.");
    }

    public Task<CartChangeResult> AddAsync(string userId, Guid productId, int quantity, CancellationToken ct) =>
        ChangeAsync(userId, productId, quantity, CartChange.Add, ct);

    public Task<CartChangeResult> UpdateAsync(string userId, Guid productId, int quantity, CancellationToken ct) =>
        ChangeAsync(userId, productId, quantity, CartChange.Update, ct);

    public Task<CartChangeResult> RemoveAsync(string userId, Guid productId, CancellationToken ct) =>
        ChangeAsync(userId, productId, null, CartChange.Remove, ct);

    private async Task<CartChangeResult> ChangeAsync(string userId, Guid productId, int? quantity,
        CartChange change, CancellationToken ct)
    {
        if (change != CartChange.Remove && quantity is not > 0)
            return new(null, 400, "A quantidade deve ser um inteiro positivo.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        
        var user = await db.Users.FromSqlInterpolated(
            $"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {userId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (user is null) return new(null, 401, "Sessao indisponivel. Entre novamente.");

        var cart = await db.Carts.Include(cart => cart.Items).SingleOrDefaultAsync(cart => cart.UserId == userId, ct);
        var item = cart?.Items.SingleOrDefault(item => item.ProductId == productId);

        if (change is CartChange.Remove or CartChange.Update && item is null)
            return new(null, 404, "Item nao encontrado no seu carrinho.");

        if (change == CartChange.Remove)
        {
            db.CartItems.Remove(item!);
        }
        else
        {
            var product = await db.Products.SingleOrDefaultAsync(product => product.Id == productId, ct);
            if (product is null) return new(null, 404, "Produto nao encontrado.");
            if (!product.IsActive) return new(null, 409, "Este produto esta indisponivel.");

            var desired = change == CartChange.Add ? (long)(item?.Quantity ?? 0) + quantity!.Value : quantity!.Value;
            if (desired > product.Stock)
                return new(null, 409, "A quantidade solicitada excede o estoque disponivel.");

            if (cart is null)
            {
                cart = new Cart(userId);
                db.Carts.Add(cart);
            }
            if (item is null) cart.Items.Add(new CartItem(cart.Id, productId, (int)desired));
            else item.SetQuantity((int)desired);
        }

        await db.SaveChangesAsync(ct);
        var response = await GetAsync(userId, ct);
        await transaction.CommitAsync(ct);
        return new(response, 200);
    }

    private enum CartChange { Add, Update, Remove }
}

public sealed record CartChangeResult(CartResponse? Cart, int StatusCode, string? Error = null);
