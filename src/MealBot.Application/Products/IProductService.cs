namespace MealBot.Application.Products;

public interface IProductService
{
    Task EnsureUserAsync(
        long telegramUserId,
        string? firstName,
        string? userName,
        CancellationToken cancellationToken = default
    );

    Task<InventoryItemDto> AddAsync(
        AddProductRequest request,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<InventoryItemDto>> GetInventoryAsync(
        long telegramUserId,
        CancellationToken cancellationToken = default
    );

    Task<InventoryItemDto> UpdateAsync(
        UpdateInventoryItemRequest request,
        CancellationToken cancellationToken = default
    );

    Task DeleteAsync(
        long telegramUserId,
        Guid inventoryItemId,
        CancellationToken cancellationToken = default
    );
}