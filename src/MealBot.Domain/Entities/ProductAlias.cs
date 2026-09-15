namespace MealBot.Domain.Entities;

public sealed class ProductAlias
{
    private ProductAlias()
    {
    }

    public ProductAlias(Product product, string alias)
    {
        ArgumentNullException.ThrowIfNull(product);

        Product = product;
        ProductId = product.Id;
        SetAlias(alias);
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid ProductId { get; private set; }

    public string Alias { get; private set; } = string.Empty;

    public string NormalizedAlias { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private set; }

    public Product Product { get; private set; } = null!;

    public void SetAlias(string alias)
    {
        var normalizedAlias = ProductName.Normalize(alias);
        Alias = normalizedAlias.DisplayName;
        NormalizedAlias = normalizedAlias.NormalizedName;
    }
}
