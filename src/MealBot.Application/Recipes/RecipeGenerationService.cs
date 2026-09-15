namespace MealBot.Application.Recipes;

public sealed class RecipeGenerationService(
    IRecipeGenerator recipeGenerator,
    IRecipeValidator recipeValidator,
    IProductCatalog productCatalog) : IRecipeGenerationService
{
    private const string EmptyResponseErrorCode = "empty_response";

    public async Task<ValidatedRecipeDto> GenerateAndValidateAsync(
        RecipeGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.AvailableProducts);

        var previousErrors = request.PreviousErrors?
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? [];
        IReadOnlyList<RecipeValidationError> lastErrors = Array.Empty<RecipeValidationError>();

        for (var attempt = 1; attempt <= RecipeGenerationLimits.MaxValidationAttempts; attempt++)
        {
            var candidate = await recipeGenerator.GenerateAsync(
                request with { PreviousErrors = previousErrors },
                cancellationToken);

            if (candidate is null)
            {
                lastErrors =
                [
                    new RecipeValidationError(
                        EmptyResponseErrorCode,
                        "Генератор не вернул рецепт")
                ];
            }
            else
            {
                var productNames = CollectProductNames(request, candidate);
                var catalog = await productCatalog.FindByNamesAsync(
                    productNames,
                    cancellationToken);

                var validation = recipeValidator.Validate(candidate, request, catalog);
                if (validation.IsValid && validation.Recipe is not null)
                {
                    return validation.Recipe;
                }

                lastErrors = validation.Errors;
            }

            AddUniqueErrorMessages(previousErrors, lastErrors);
        }

        var details = lastErrors.Count == 0
            ? string.Empty
            : $" Последние ошибки: {string.Join("; ", lastErrors.Select(error => error.Message))}";

        throw new RecipeGenerationException(
            RecipeGenerationErrorKind.InvalidResponse,
            "Не получилось подобрать блюдо из оставшихся продуктов. " +
            "Добавьте продукты или разрешите использовать недостающие ингредиенты" + details,
            isRetryable: false);
    }

    private static IReadOnlyCollection<string> CollectProductNames(
        RecipeGenerationRequest request,
        GeneratedRecipeDto candidate)
    {
        return request.AvailableProducts
            .Select(product => product.Name)
            .Append(candidate.Name)
            .Concat(candidate.Ingredients?.Where(ingredient => ingredient is not null)
                        .Select(ingredient => ingredient.ProductName)
                    ?? Enumerable.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddUniqueErrorMessages(
        ICollection<string> target,
        IEnumerable<RecipeValidationError> errors)
    {
        foreach (var message in errors
                     .Select(error => error.Message)
                     .Where(message => !string.IsNullOrWhiteSpace(message))
                     .Distinct(StringComparer.Ordinal)
                     .Where(message => !target.Contains(message, StringComparer.Ordinal)))
        {
            target.Add(message);
        }
    }
}
