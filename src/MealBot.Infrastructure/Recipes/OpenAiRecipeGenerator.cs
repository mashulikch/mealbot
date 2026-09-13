using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using MealBot.Application.Recipes;
using MealBot.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MealBot.Infrastructure.Recipes;

public sealed class OpenAiRecipeGenerator(
    HttpClient httpClient,
    IOptions<RecipeGeneratorOptions> options,
    ILogger<OpenAiRecipeGenerator> logger,
    IValidator<RecipeGenerationRequest> requestValidator) : IRecipeGenerator
{
    private const string ChatCompletionsEndpoint = "chat/completions";
    private const int ErrorDetailsMaxLength = 800;

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly JsonElement RecipeSchema = CreateRecipeSchema();

    public async Task<GeneratedRecipeDto> GenerateAsync(
        RecipeGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await requestValidator.ValidateAndThrowAsync(request, cancellationToken);

        var generatorOptions = GetValidatedOptions();

        return await GenerateWithRetriesAsync(
            request,
            generatorOptions,
            cancellationToken);
    }

    private RecipeGeneratorOptions GetValidatedOptions()
    {
        try
        {
            var generatorOptions = options.Value;
            generatorOptions.ValidateForRequest();
            return generatorOptions;
        }
        catch (InvalidOperationException exception)
        {
            throw new RecipeGenerationException(
                RecipeGenerationErrorKind.Configuration,
                exception.Message,
                isRetryable: false,
                exception);
        }
    }

    private async Task<GeneratedRecipeDto> GenerateWithRetriesAsync(
        RecipeGenerationRequest request,
        RecipeGeneratorOptions generatorOptions,
        CancellationToken cancellationToken)
    {
        var previousErrors = request.PreviousErrors?.ToList() ?? [];
        RecipeGenerationException? lastException = null;
        var attemptsCompleted = 0;

        for (var attempt = 1; attempt <= generatorOptions.MaxAttempts; attempt++)
        {
            attemptsCompleted = attempt;
            try
            {
                return await SendRequestAsync(
                    request with { PreviousErrors = previousErrors },
                    generatorOptions,
                    cancellationToken);
            }
            catch (RecipeGenerationException exception) when
                (exception.IsRetryable && attempt < generatorOptions.MaxAttempts)
            {
                lastException = exception;
                previousErrors.Add(exception.Message);

                logger.LogWarning(
                    exception,
                    "Не удалось получить корректный рецепт с попытки {Attempt} из {MaxAttempts}",
                    attempt,
                    generatorOptions.MaxAttempts);

                if (generatorOptions.RetryDelayMilliseconds > 0)
                {
                    await Task.Delay(
                        generatorOptions.RetryDelayMilliseconds,
                        cancellationToken);
                }
            }
            catch (RecipeGenerationException exception)
            {
                lastException = exception;
                break;
            }
        }

        throw new RecipeGenerationException(
            lastException?.Kind ?? RecipeGenerationErrorKind.InvalidResponse,
            $"Не удалось сгенерировать корректный рецепт за {attemptsCompleted} попытки. " +
            $"Последняя ошибка: {lastException?.Message ?? "неизвестная ошибка"}",
            isRetryable: false,
            lastException);
    }

    private async Task<GeneratedRecipeDto> SendRequestAsync(
        RecipeGenerationRequest request,
        RecipeGeneratorOptions generatorOptions,
        CancellationToken cancellationToken)
    {
        var payload = new ChatCompletionRequest(
            generatorOptions.Model,
            [
                new ChatMessage("system", BuildSystemPrompt()),
                new ChatMessage("user", BuildUserPrompt(request))
            ],
            Temperature: 0.8,
            ResponseFormat: new StructuredResponseFormat(
                "json_schema",
                new JsonSchemaFormat("meal_recipe", Strict: true, RecipeSchema)));

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                ChatCompletionsEndpoint,
                payload,
                JsonOptions,
                cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw CreateApiException(response.StatusCode, responseBody);
            }

            return ParseRecipe(responseBody);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RecipeGenerationException(
                RecipeGenerationErrorKind.Timeout,
                "Сервис генерации рецептов не ответил вовремя",
                isRetryable: true,
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new RecipeGenerationException(
                RecipeGenerationErrorKind.Transport,
                "Не удалось подключиться к сервису генерации рецептов",
                isRetryable: true,
                exception);
        }
    }

    private static RecipeGenerationException CreateApiException(
        HttpStatusCode statusCode,
        string responseBody)
    {
        var statusDescription = $"HTTP {(int)statusCode} ({statusCode})";
        var details = Truncate(responseBody, ErrorDetailsMaxLength);
        var message = string.IsNullOrWhiteSpace(details)
            ? $"Сервис генерации рецептов вернул ошибку {statusDescription}"
            : $"Сервис генерации рецептов вернул ошибку {statusDescription}: {details}";

        var isRetryable = statusCode == HttpStatusCode.TooManyRequests
                          || (int)statusCode >= 500;

        return new RecipeGenerationException(
            RecipeGenerationErrorKind.Api,
            message,
            isRetryable);
    }

    private static GeneratedRecipeDto ParseRecipe(string responseBody)
    {
        try
        {
            var completion = JsonSerializer.Deserialize<ChatCompletionResponse>(
                responseBody,
                JsonOptions);

            var content = completion?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new JsonException("В ответе отсутствует message.content");
            }

            var recipeJson = ExtractJson(content);
            var recipe = JsonSerializer.Deserialize<GeneratedRecipeDto>(recipeJson, JsonOptions);

            if (recipe is null)
            {
                throw new JsonException("Ответ не содержит объект рецепта");
            }

            EnsureRecipeStructure(recipe);
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

    private static void EnsureRecipeStructure(GeneratedRecipeDto recipe)
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

    private static string ExtractJson(string content)
    {
        var normalized = content.Trim();
        if (normalized.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = normalized.IndexOf('\n');
            var lastFence = normalized.LastIndexOf("```", StringComparison.Ordinal);

            if (firstLineEnd > 0 && lastFence > firstLineEnd)
            {
                return normalized[(firstLineEnd + 1)..lastFence].Trim();
            }
        }

        return normalized;
    }

    private static string BuildSystemPrompt() =>
        "Ты — генератор рецептов для Telegram-бота планирования меню. " +
        "Всегда возвращай только JSON по переданной JSON Schema, без Markdown и пояснений. " +
        "Используй названия продуктов и единицы измерения ровно в том виде, в котором они переданы. " +
        "Не выдумывай продукты, если разрешение на недостающие ингредиенты не включено. " +
        "Не повторяй блюда из списка исключений";

    private static string BuildUserPrompt(RecipeGenerationRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Приём пищи: {request.MealType}");
        builder.AppendLine($"Порций: {request.Servings}");
        builder.AppendLine($"Максимальное время приготовления: {request.MaxCookingTimeMinutes} минут");
        builder.AppendLine(
            $"Можно использовать недостающие ингредиенты: {(request.AllowMissingIngredients ? "да" : "нет")}");

        builder.AppendLine("Доступные продукты с точными остатками:");
        foreach (var product in request.AvailableProducts)
        {
            builder.AppendLine(
                $"— {product.Name}: {product.Quantity.ToString(CultureInfo.InvariantCulture)} {product.Unit}");
        }

        builder.AppendLine("Блюда, которые нельзя повторять:");
        if (request.ExcludedRecipeNames.Count == 0)
        {
            builder.AppendLine("— нет");
        }
        else
        {
            foreach (var recipeName in request.ExcludedRecipeNames)
            {
                builder.AppendLine($"— {recipeName}");
            }
        }

        if (request.PreviousErrors is { Count: > 0 })
        {
            builder.AppendLine("Ошибки предыдущих попыток, которые нужно исправить:");
            foreach (var error in request.PreviousErrors)
            {
                builder.AppendLine($"— {error}");
            }
        }

        builder.AppendLine();
        builder.AppendLine(
            "Сгенерируй одно блюдо. Количество ингредиентов должно быть положительным. " +
            "Если недостающие ингредиенты запрещены, используй только переданные продукты " +
            "и не превышай их доступные остатки");

        return builder.ToString();
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static JsonElement CreateRecipeSchema()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "type": "object",
              "additionalProperties": false,
              "required": ["name", "mealType", "servings", "cookingTimeMinutes", "ingredients", "steps"],
              "properties": {
                "name": { "type": "string" },
                "mealType": { "type": "string", "enum": ["Breakfast", "Lunch", "Dinner"] },
                "servings": { "type": "integer", "minimum": 1 },
                "cookingTimeMinutes": { "type": "integer", "minimum": 1 },
                "ingredients": {
                  "type": "array",
                  "minItems": 1,
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["productName", "quantity", "unit", "isOptional"],
                    "properties": {
                      "productName": { "type": "string" },
                      "quantity": { "type": "number", "exclusiveMinimum": 0 },
                      "unit": { "type": "string", "enum": ["Gram", "Milliliter", "Piece"] },
                      "isOptional": { "type": "boolean" }
                    }
                  }
                },
                "steps": {
                  "type": "array",
                  "minItems": 1,
                  "items": { "type": "string" }
                }
              }
            }
            """);

        return document.RootElement.Clone();
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";

    private sealed record ChatCompletionRequest(
        string Model,
        IReadOnlyCollection<ChatMessage> Messages,
        double Temperature,
        [property: JsonPropertyName("response_format")]
        StructuredResponseFormat ResponseFormat);

    private sealed record ChatMessage(string Role, string Content);

    private sealed record StructuredResponseFormat(
        string Type,
        [property: JsonPropertyName("json_schema")]
        JsonSchemaFormat JsonSchema);

    private sealed record JsonSchemaFormat(
        string Name,
        bool Strict,
        [property: JsonPropertyName("schema")]
        JsonElement Schema);

    private sealed record ChatCompletionResponse(
        IReadOnlyList<ChatChoice>? Choices);

    private sealed record ChatChoice(ChatMessageResponse? Message);

    private sealed record ChatMessageResponse(string? Content);
}
