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
        var existingUser = await dbContext.Users
            .SingleOrDefaultAsync(
                user => user.TelegramId == telegramUserId,
                cancellationToken);

        if (existingUser is null)
        {
            dbContext.Users.Add(new User(telegramUserId, firstName, userName));
        }
        else
        {
            existingUser.UpdateProfile(firstName, userName);
        }

        await SaveChangesAsync(
            cancellationToken,
            "Не удалось сохранить данные пользователя. Попробуйте ещё раз");
    }

    public async Task<InventoryItemDto> AddAsync(
        AddProductRequest request,
        CancellationToken cancellationToken = default)
    {
        await addProductValidator.ValidateAndThrowAsync(request, cancellationToken);

        var user = await GetAuthorizedUserAsync(request.TelegramUserId, cancellationToken);
        var product = await FindOrCreateProductAsync(request.Name, cancellationToken);

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

        await SaveChangesAsync(
            cancellationToken,
            "Не удалось сохранить изменения в запасах. Попробуйте ещё раз");
        return MapToDto(inventoryItem);
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
        var inventoryItem = await FindInventoryItemForUserAsync(
            user.Id,
            request.InventoryItemId,
            cancellationToken)
            ?? throw new KeyNotFoundException("Продукт не найден в запасах пользователя");

        EnsureUnitCanBeChanged(inventoryItem, request.Unit);

        inventoryItem.SetQuantity(request.Quantity);
        inventoryItem.SetUnit(request.Unit);

        var duplicateItem = await FindDuplicateInventoryItemAsync(
            user.Id,
            inventoryItem,
            request.Unit,
            cancellationToken);

        if (duplicateItem is not null)
        {
            EnsureItemsCanBeMerged(inventoryItem, duplicateItem);

            duplicateItem.AddQuantity(inventoryItem.Quantity);
            dbContext.InventoryItems.Remove(inventoryItem);
            await SaveChangesAsync(
                cancellationToken,
                "Не удалось сохранить изменения в запасах. Попробуйте ещё раз");
            return MapToDto(duplicateItem);
        }

        await SaveChangesAsync(
            cancellationToken,
            "Не удалось сохранить изменения в запасах. Попробуйте ещё раз");
        return MapToDto(inventoryItem);
    }

    public async Task DeleteAsync(
        long telegramUserId,
        Guid inventoryItemId,
        CancellationToken cancellationToken = default)
    {
        var user = await GetAuthorizedUserAsync(telegramUserId, cancellationToken);
        var inventoryItem = await FindInventoryItemForUserAsync(
            user.Id,
            inventoryItemId,
            cancellationToken)
            ?? throw new KeyNotFoundException("Продукт не найден в запасах пользователя");

        if (inventoryItem.ReservedQuantity > 0)
        {
            throw new InvalidOperationException(
                "Нельзя удалить продукт, пока он зарезервирован меню");
        }

        dbContext.InventoryItems.Remove(inventoryItem);
        await SaveChangesAsync(
            cancellationToken,
            "Не удалось сохранить изменения в запасах. Попробуйте ещё раз");
    }

    private async Task<Product> FindOrCreateProductAsync(
        string productName,
        CancellationToken cancellationToken)
    {
        var normalizedProductName = ProductName.Normalize(productName);
        var existingProduct = await dbContext.Products
            .SingleOrDefaultAsync(
                product => product.NormalizedName == normalizedProductName.NormalizedName,
                cancellationToken);

        if (existingProduct is not null)
        {
            return existingProduct;
        }

        var newProduct = new Product(normalizedProductName.DisplayName);
        dbContext.Products.Add(newProduct);
        return newProduct;
    }

    private Task<InventoryItem?> FindInventoryItemForUserAsync(
        Guid userId,
        Guid inventoryItemId,
        CancellationToken cancellationToken) =>
        dbContext.InventoryItems
            .Include(item => item.Product)
            .SingleOrDefaultAsync(
                item => item.Id == inventoryItemId && item.UserId == userId,
                cancellationToken);

    private Task<InventoryItem?> FindDuplicateInventoryItemAsync(
        Guid userId,
        InventoryItem inventoryItem,
        MeasurementUnit requestedUnit,
        CancellationToken cancellationToken) =>
        dbContext.InventoryItems
            .Include(item => item.Product)
            .SingleOrDefaultAsync(
                item => item.Id != inventoryItem.Id
                        && item.UserId == userId
                        && item.ProductId == inventoryItem.ProductId
                        && item.Unit == requestedUnit,
                cancellationToken);

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
        var user = await dbContext.Users
            .SingleOrDefaultAsync(
                candidate => candidate.TelegramId == telegramUserId,
                cancellationToken);

        return user ?? throw new InvalidOperationException(
            "Пользователь не авторизован. Сначала отправьте /start");
    }

    private async Task SaveChangesAsync(
        CancellationToken cancellationToken,
        string failureMessage)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new InvalidOperationException(failureMessage, exception);
        }
    }

    private static InventoryItemDto MapToDto(InventoryItem inventoryItem) =>
        new(
            inventoryItem.Id,
            inventoryItem.Product.Name,
            inventoryItem.Quantity,
            inventoryItem.ReservedQuantity,
            inventoryItem.AvailableQuantity,
            inventoryItem.Unit);
}
