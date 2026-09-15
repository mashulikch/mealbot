using MealBot.Application.Recipes;
using MealBot.Domain;
using MealBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MealBot.Infrastructure.Products;

public sealed class ProductCatalogService(MealBotDbContext dbContext) : IProductCatalog
{
    public async Task<IReadOnlyCollection<ProductCatalogItem>> FindByNamesAsync(
        IReadOnlyCollection<string> productNames,
        CancellationToken cancellationToken = default)
    {
        var normalizedNames = productNames
            .Select(name => ProductName.TryNormalize(name, out var normalizedName)
                ? normalizedName
                : null)
            .Where(name => name is not null)
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalizedNames.Length == 0)
        {
            return Array.Empty<ProductCatalogItem>();
        }

        var products = await dbContext.Products
            .AsNoTracking()
            .Include(product => product.Aliases)
            .Where(product =>
                normalizedNames.Contains(product.NormalizedName)
                || product.Aliases.Any(alias => normalizedNames.Contains(alias.NormalizedAlias)))
            .ToListAsync(cancellationToken);

        return products
            .Select(product => new ProductCatalogItem(
                product.Id,
                product.Name,
                product.Aliases
                    .Select(alias => alias.Alias)
                    .ToArray()))
            .ToArray();
    }

}
