using MealBot.Infrastructure.Products;
using MealBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace MealBot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMealBotInfrastructure(this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MealBot") ??
                                throw new InvalidOperationException("Connection string 'MealBot' isn't configured");

        services.AddDbContext<MealBotDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<MealBot.Application.Products.IProductService, ProductService>();

        return services;
    }
}