using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using MealBot.Application.Recipes;
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
    private const double CreativityTemperature = 0.8;

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public async Task<GeneratedRecipeDto> GenerateAsync(
        RecipeGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await requestValidator.ValidateAndThrowAsync(request, cancellationToken);

        var generatorOptions = GetValidatedOptions();

        return await GenerateWithRetriesAsync(request, generatorOptions, cancellationToken);
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
                    await Task.Delay(generatorOptions.RetryDelayMilliseconds, cancellationToken);
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
                new ChatMessage("system", RecipePromptBuilder.BuildSystemPrompt()),
                new ChatMessage("user", RecipePromptBuilder.BuildUserPrompt(request))
            ],
            Temperature: CreativityTemperature,
            ResponseFormat: new StructuredResponseFormat(
                "json_schema",
                new JsonSchemaFormat("meal_recipe", Strict: true, RecipeSchemaProvider.Schema)));

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

            return RecipeResponseParser.Parse(responseBody, JsonOptions, logger);
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

        var isRetryable = statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

        return new RecipeGenerationException(RecipeGenerationErrorKind.Api, message, isRetryable);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        return jsonOptions;
    }

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
}
