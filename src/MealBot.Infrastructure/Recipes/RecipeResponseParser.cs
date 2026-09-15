using System.Text.Json;
using MealBot.Application.Recipes;
using Microsoft.Extensions.Logging;

namespace MealBot.Infrastructure.Recipes;

/// <summary>
/// Turns the raw OpenAI chat-completion response body into a <see cref="GeneratedRecipeDto"/>
/// and performs the minimal structural checks needed before handing the recipe back to the caller
/// (full business validation against the product catalog still happens in
/// MealBot.Application.Recipes.RecipeValidator).
/// </summary>
internal static class RecipeResponseParser
{
    private const string MarkdownFenceMarker = "```";

    public static GeneratedRecipeDto Parse(
        string responseBody,
        JsonSerializerOptions jsonOptions,
        ILogger logger)
    {
        try
        {
            var completion = JsonSerializer.Deserialize<ChatCompletionResponse>(responseBody, jsonOptions);

            var content = completion?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new JsonException("В ответе отсутствует message.content");
            }

            var recipeJson = ExtractJsonPayload(content, logger);
            var recipe = JsonSerializer.Deserialize<GeneratedRecipeDto>(recipeJson, jsonOptions);

            if (recipe is null)
            {
                throw new JsonException("Ответ не содержит объект рецепта");
            }

            EnsureStructureIsValid(recipe);
            return recipe;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new RecipeGenerationException(
                RecipeGenerationErrorKind.InvalidResponse,
                $"Сервис вернул некорректный JSON рецепта: {exception.Message}",
                isRetryable: true,
                exception);
        }
    }

    private static string ExtractJsonPayload(string content, ILogger logger)
    {
        var normalized = content.Trim();
        if (!normalized.StartsWith(MarkdownFenceMarker, StringComparison.Ordinal))
        {
            return normalized;
        }

        var firstLineEnd = normalized.IndexOf('\n');
        var lastFence = normalized.LastIndexOf(MarkdownFenceMarker, StringComparison.Ordinal);

        if (firstLineEnd <= 0 || lastFence <= firstLineEnd)
        {
            logger.LogDebug(
                "Ответ начинается с ограждения Markdown, но закрывающее ограждение не найдено — " +
                "используем содержимое без изменений");
            return normalized;
        }

        return normalized[(firstLineEnd + 1)..lastFence].Trim();
    }

    private static void EnsureStructureIsValid(GeneratedRecipeDto recipe)
    {
        if (string.IsNullOrWhiteSpace(recipe.Name))
        {
            throw new JsonException("У рецепта отсутствует название");
        }

        if (!Enum.IsDefined(recipe.MealType))
        {
            throw new JsonException("У рецепта указан неизвестный приём пищи");
        }

        if (recipe.Servings <= 0)
        {
            throw new JsonException("Количество порций должно быть больше нуля");
        }

        if (recipe.CookingTimeMinutes <= 0)
        {
            throw new JsonException("Время приготовления должно быть больше нуля");
        }

        if (recipe.Ingredients is null || recipe.Ingredients.Count == 0)
        {
            throw new JsonException("В рецепте должен быть хотя бы один ингредиент");
        }

        foreach (var ingredient in recipe.Ingredients)
        {
            if (ingredient is null
                || string.IsNullOrWhiteSpace(ingredient.ProductName)
                || ingredient.Quantity <= 0
                || !Enum.IsDefined(ingredient.Unit))
            {
                throw new JsonException("В рецепте указан некорректный ингредиент");
            }
        }

        if (recipe.Steps is null
            || recipe.Steps.Count == 0
            || recipe.Steps.Any(string.IsNullOrWhiteSpace))
        {
            throw new JsonException("В рецепте должна быть хотя бы одна инструкция");
        }
    }

    private sealed record ChatCompletionResponse(IReadOnlyList<ChatChoice>? Choices);

    private sealed record ChatChoice(ChatMessageResponse? Message);

    private sealed record ChatMessageResponse(string? Content);
}
