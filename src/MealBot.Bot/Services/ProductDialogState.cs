namespace MealBot.Bot.Services;

public enum ProductDialogMode
{
    AddName,
    AddQuantity,
    EditQuantity
}

public sealed class ProductDialogState(ProductDialogMode mode)
{
    public ProductDialogMode Mode { get; set; } = mode;

    public Guid? InventoryItemId { get; set; }

    public string? ProductName { get; set; }

    public decimal? Quantity { get; set; }
}
