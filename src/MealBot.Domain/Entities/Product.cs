namespace MealBot.Domain.Entities;

public sealed class Product
{
    private Product()
    {
    }

    public Product(string name)
    {
        Rename(name);
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public string Name { get; private set; } = string.Empty;

    public string NormalizedName { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private set; }

    public ICollection<InventoryItem> InventoryItems { get; private set; } = new List<InventoryItem>();

    public ICollection<ProductAlias> Aliases { get; private set; } = new List<ProductAlias>();

    public void Rename(string name)
    {
        var normalizedName = ProductName.Normalize(name);

        Name = normalizedName.DisplayName;
        NormalizedName = normalizedName.NormalizedName;
        CreatedAtUtc = CreatedAtUtc == default ? DateTime.UtcNow : CreatedAtUtc;
    }
}
