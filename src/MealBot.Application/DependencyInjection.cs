using FluentValidation;
using MealBot.Application.Products;
using Microsoft.Extensions.DependencyInjection;

namespace MealBot.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddMealBotApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<AddProductRequestValidator>();
        return services;
    }
}