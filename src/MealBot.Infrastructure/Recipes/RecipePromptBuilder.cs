using System.Globalization;
using System.Text;
using MealBot.Application.Recipes;

namespace MealBot.Infrastructure.Recipes;

/// <summary>
/// Builds the system and user prompts sent to the recipe-generation model.
/// Extracted from <see cref="OpenAiRecipeGenerator"/> so prompt formatting can change
/// without touching request/response/retry orchestration.
/// </summary>
internal static class RecipePromptBuilder
{
    public static string BuildSystemPrompt() =>
        "Ты — генератор рецептов для Telegram-бота планирования меню. " +
        "Всегда возвращай только JSON по переданной JSON Schema, без Markdown и пояснений. " +
        "Используй названия продуктов и единицы измерения ровно в том виде, в котором они переданы. " +
        "Не выдумывай продукты, если разрешение на недостающие ингредиенты не включено. " +
        "Не повторяй блюда из списка исключений";

    public static string BuildUserPrompt(RecipeGenerationRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Приём пищи: {request.MealType}");
        builder.AppendLine($"Порций: {request.Servings}");
        builder.AppendLine($"Максимальное время приготовления: {request.MaxCookingTimeMinutes} минут");
        builder.AppendLine(
            $"Можно использовать недостающие ингредиенты: {(request.AllowMissingIngredients ? "да" : "нет")}");

        AppendAvailableProducts(builder, request.AvailableProducts);
        AppendExcludedRecipes(builder, request.ExcludedRecipeNames);
        AppendPreviousErrors(builder, request.PreviousErrors);

        builder.AppendLine();
        builder.AppendLine(
            "Сгенерируй одно блюдо. Количество ингредиентов должно быть положительным. " +
            "Если недостающие ингредиенты запрещены, используй только переданные продукты " +
            "и не превышай их доступные остатки");

        return builder.ToString();
    }

    private static void AppendAvailableProducts(
        StringBuilder builder,
        IReadOnlyCollection<AvailableProduct> products)
    {
        builder.AppendLine("Доступные продукты с точными остатками:");
        foreach (var product in products)
        {
            builder.AppendLine(
                $"— {product.Name}: {product.Quantity.ToString(CultureInfo.InvariantCulture)} {product.Unit}");
        }
    }

    private static void AppendExcludedRecipes(
        StringBuilder builder,
        IReadOnlyCollection<string> excludedRecipeNames)
    {
        builder.AppendLine("Блюда, которые нельзя повторять:");
        if (excludedRecipeNames.Count == 0)
        {
            builder.AppendLine("— нет");
            return;
        }

        foreach (var recipeName in excludedRecipeNames)
        {
            builder.AppendLine($"— {recipeName}");
        }
    }

    private static void AppendPreviousErrors(
        StringBuilder builder,
        IReadOnlyCollection<string>? previousErrors)
    {
        if (previousErrors is not { Count: > 0 })
        {
            return;
        }

        builder.AppendLine("Ошибки предыдущих попыток, которые нужно исправить:");
        foreach (var error in previousErrors)
        {
            builder.AppendLine($"— {error}");
        }
    }
}
