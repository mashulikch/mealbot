using MealBot.Application.MealPlans;
using MealBot.Application.Products;
using MealBot.Application.Recipes;
using MealBot.Infrastructure.MealPlans;
using MealBot.Infrastructure.Persistence;
using MealBot.Infrastructure.Products;
using MealBot.Infrastructure.Recipes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;

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
        services.AddScoped<IProductCatalog, ProductCatalogService>();
        services.AddScoped<IMealPlanService, MealPlanService>();
        services.AddOptions<RecipeGeneratorOptions>()
            .Bind(configuration.GetSection(RecipeGeneratorOptions.SectionName));

        services.AddHttpClient<IRecipeGenerator, OpenAiRecipeGenerator>(
            ConfigureRecipeGeneratorHttpClient);

        return services;
    }

    private static void ConfigureRecipeGeneratorHttpClient(
        IServiceProvider serviceProvider,
        HttpClient httpClient)
    {
        var options = serviceProvider
            .GetRequiredService<IOptions<RecipeGeneratorOptions>>()
            .Value;

        httpClient.BaseAddress = options.GetBaseAddress();
        httpClient.Timeout = options.GetTimeout();

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }
    }
}
