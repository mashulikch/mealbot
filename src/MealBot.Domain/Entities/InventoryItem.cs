namespace MealBot.Domain.Entities;

public sealed class InventoryItem
{
    private InventoryItem()
    {
    }

    public InventoryItem(User user, Product product, decimal quantity, MeasurementUnit unit)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(product);
        EnsurePositiveQuantity(quantity);

        EnsureValidUnit(unit);

        User = user;
        UserId = user.Id;
        Product = product;
        ProductId = product.Id;
        Quantity = quantity;
        Unit = unit;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid UserId { get; private set; }

    public Guid ProductId { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal ReservedQuantity { get; private set; }

    public MeasurementUnit Unit { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public User User { get; private set; } = null!;

    public Product Product { get; private set; } = null!;

    public decimal AvailableQuantity => Quantity - ReservedQuantity;

    public void SetQuantity(decimal quantity)
    {
        EnsurePositiveQuantity(quantity);

        if (quantity < ReservedQuantity)
        {
            throw new InvalidOperationException("Количество не может быть меньше уже зарезервированного");
        }

        Quantity = quantity;
        Touch();
    }

    public void SetUnit(MeasurementUnit unit)
    {
        EnsureValidUnit(unit);

        Unit = unit;
        Touch();
    }

    public void AddQuantity(decimal quantity)
    {
        EnsurePositiveQuantity(quantity);

        Quantity += quantity;
        Touch();
    }

    public void Reserve(decimal quantity)
    {
        if (quantity <= 0 || quantity > AvailableQuantity)
        {
            throw new InvalidOperationException("Недостаточно свободного продукта для резервирования");
        }

        ReservedQuantity += quantity;
        Touch();
    }

    public void ReleaseReservation(decimal quantity)
    {
        if (quantity <= 0 || quantity > ReservedQuantity)
        {
            throw new InvalidOperationException(
                "Нельзя освободить больше зарезервированного количества");
        }

        ReservedQuantity -= quantity;
        Touch();
    }

    public void ConsumeReserved(decimal quantity)
    {
        if (quantity <= 0 || quantity > ReservedQuantity || quantity > Quantity)
        {
            throw new InvalidOperationException("Нельзя списать указанное количество продукта");
        }

        Quantity -= quantity;
        ReservedQuantity -= quantity;
        Touch();
    }

    private static void EnsurePositiveQuantity(decimal quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Количество должно быть больше нуля");
        }
    }

    private static void EnsureValidUnit(MeasurementUnit unit)
    {
        if (!Enum.IsDefined(unit))
        {
            throw new ArgumentException("Неизвестная единица измерения", nameof(unit));
        }
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
