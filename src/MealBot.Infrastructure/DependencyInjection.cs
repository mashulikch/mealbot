using MealBot.Application.MealPlans;
using MealBot.Application.Products;
using MealBot.Infrastructure.MealPlans;
using MealBot.Infrastructure.Persistence;
using MealBot.Infrastructure.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MealBot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMealBotInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MealBot")
            ?? throw new InvalidOperationException(
                "Connection string 'MealBot' isn't configured");

        services.AddDbContext<MealBotDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IMealPlanService, MealPlanService>();

        return services;
    }
}
