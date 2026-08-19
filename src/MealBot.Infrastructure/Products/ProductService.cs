using FluentValidation;
using MealBot.Application.Products;
using MealBot.Domain;
using MealBot.Domain.Entities;
using MealBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MealBot.Infrastructure.Products;

public sealed class ProductService(
    MealBotDbContext dbContext,
    IValidator<AddProductRequest> addProductValidator,
    IValidator<UpdateInventoryItemRequest> updateInventoryItemValidator) : IProductService
{
    public async Task EnsureUserAsync(
        long telegramUserId,
        string? firstName,
        string? userName,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .SingleOrDefaultAsync(item => item.TelegramId == telegramUserId, cancellationToken);

        if (user is null)
        {
            dbContext.Users.Add(new User(telegramUserId, firstName, userName));
        }
        else
        {
            user.UpdateProfile(firstName, userName);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<InventoryItemDto> AddAsync(
        AddProductRequest request,
        CancellationToken cancellationToken = default)
    {
        await addProductValidator.ValidateAndThrowAsync(request, cancellationToken);

        var user = await GetAuthorizedUserAsync(request.TelegramUserId, cancellationToken);
        var normalizedProductName = ProductName.Normalize(request.Name);
        var product = await GetOrCreateProductAsync(normalizedProductName, cancellationToken);

        var inventoryItem = await dbContext.InventoryItems
            .Include(item => item.Product)
            .SingleOrDefaultAsync(
                item => item.UserId == user.Id
                        && item.ProductId == product.Id
                        && item.Unit == request.Unit,
                cancellationToken);

        if (inventoryItem is null)
        {
            inventoryItem = new InventoryItem(user, product, request.Quantity, request.Unit);
            dbContext.InventoryItems.Add(inventoryItem);
        }
        else
        {
            inventoryItem.AddQuantity(request.Quantity);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(inventoryItem);
    }

    public async Task<IReadOnlyList<InventoryItemDto>> GetInventoryAsync(
        long telegramUserId,
        CancellationToken cancellationToken = default)
    {
        var user = await GetAuthorizedUserAsync(telegramUserId, cancellationToken);

        return await dbContext.InventoryItems
            .AsNoTracking()
            .Where(item => item.UserId == user.Id)
            .OrderBy(item => item.Product.Name)
            .ThenBy(item => item.Unit)
            .Select(item => new InventoryItemDto(
                item.Id,
                item.Product.Name,
                item.Quantity,
                item.ReservedQuantity,
                item.Quantity - item.ReservedQuantity,
                item.Unit))
            .ToListAsync(cancellationToken);
    }

    public async Task<InventoryItemDto> UpdateAsync(
        UpdateInventoryItemRequest request,
        CancellationToken cancellationToken = default)
    {
        await updateInventoryItemValidator.ValidateAndThrowAsync(request, cancellationToken);

        var user = await GetAuthorizedUserAsync(request.TelegramUserId, cancellationToken);
        var inventoryItem = await dbContext.InventoryItems
            .Include(item => item.Product)
            .SingleOrDefaultAsync(
                item => item.Id == request.InventoryItemId && item.UserId == user.Id,
                cancellationToken)
            ?? throw new KeyNotFoundException("Продукт не найден в запасах пользователя");

        EnsureUnitCanBeChanged(inventoryItem, request.Unit);

        inventoryItem.SetQuantity(request.Quantity);
        inventoryItem.SetUnit(request.Unit);

        var duplicateItem = await dbContext.InventoryItems
            .Include(item => item.Product)
            .SingleOrDefaultAsync(
                item => item.Id != inventoryItem.Id
                        && item.UserId == user.Id
                        && item.ProductId == inventoryItem.ProductId
                        && item.Unit == request.Unit,
                cancellationToken);

        if (duplicateItem is not null)
        {
            EnsureItemsCanBeMerged(inventoryItem, duplicateItem);

            duplicateItem.AddQuantity(inventoryItem.Quantity);
            dbContext.InventoryItems.Remove(inventoryItem);
            await dbContext.SaveChangesAsync(cancellationToken);
            return ToDto(duplicateItem);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(inventoryItem);
    }

    public async Task DeleteAsync(
        long telegramUserId,
        Guid inventoryItemId,
        CancellationToken cancellationToken = default)
    {
        var user = await GetAuthorizedUserAsync(telegramUserId, cancellationToken);
        var inventoryItem = await dbContext.InventoryItems
            .SingleOrDefaultAsync(
                item => item.Id == inventoryItemId && item.UserId == user.Id,
                cancellationToken)
            ?? throw new KeyNotFoundException("Продукт не найден в запасах пользователя");

        if (inventoryItem.ReservedQuantity > 0)
        {
            throw new InvalidOperationException("Нельзя удалить продукт, пока он зарезервирован меню");
        }

        dbContext.InventoryItems.Remove(inventoryItem);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Product> GetOrCreateProductAsync(
        (string DisplayName, string NormalizedName) normalizedProductName,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products
            .SingleOrDefaultAsync(
                item => item.NormalizedName == normalizedProductName.NormalizedName,
                cancellationToken);

        if (product is not null)
        {
            return product;
        }

        product = new Product(normalizedProductName.DisplayName);
        dbContext.Products.Add(product);
        return product;
    }

    private static void EnsureUnitCanBeChanged(
        InventoryItem inventoryItem,
        MeasurementUnit requestedUnit)
    {
        if (inventoryItem.ReservedQuantity > 0 && inventoryItem.Unit != requestedUnit)
        {
            throw new InvalidOperationException(
                "Нельзя менять единицу измерения продукта, пока он зарезервирован меню");
        }
    }

    private static void EnsureItemsCanBeMerged(
        InventoryItem inventoryItem,
        InventoryItem duplicateItem)
    {
        if (duplicateItem.ReservedQuantity > 0 || inventoryItem.ReservedQuantity > 0)
        {
            throw new InvalidOperationException(
                "Нельзя объединить запасы, пока продукт зарезервирован меню");
        }
    }

    private async Task<User> GetAuthorizedUserAsync(
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Users
                   .SingleOrDefaultAsync(item => item.TelegramId == telegramUserId, cancellationToken)
               ?? throw new InvalidOperationException(
                   "Пользователь не авторизован. Сначала отправьте /start");
    }

    private static InventoryItemDto ToDto(InventoryItem inventoryItem) =>
        new(
            inventoryItem.Id,
            inventoryItem.Product.Name,
            inventoryItem.Quantity,
            inventoryItem.ReservedQuantity,
            inventoryItem.AvailableQuantity,
            inventoryItem.Unit);
}
