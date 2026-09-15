using FluentValidation;
using MealBot.Application.Products;
using MealBot.Application.Recipes;
using Microsoft.Extensions.DependencyInjection;

namespace MealBot.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddMealBotApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<AddProductRequestValidator>();
        services.AddSingleton<IRecipeValidator, RecipeValidator>();
        services.AddScoped<IRecipeGenerationService, RecipeGenerationService>();
        return services;
    }
}
