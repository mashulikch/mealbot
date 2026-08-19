using MealBot.Domain;

namespace MealBot.Application.Products;

public sealed record AddProductRequest(
    long TelegramUserId,
    string Name,
    decimal Quantity,
    MeasurementUnit Unit,
    string? FirstName = null,
    string? UserName = null
);

public sealed record UpdateInventoryItemRequest(
    long TelegramUserId,
    Guid InventoryItemId,
    decimal Quantity,
    MeasurementUnit Unit
);

public sealed record InventoryItemDto(
    Guid Id,
    string ProductName,
    decimal Quantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    MeasurementUnit Unit
);