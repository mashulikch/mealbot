using MealBot.Domain;

namespace MealBot.Application.Recipes;

public sealed record AvailableProduct(
    string Name,
    decimal Quantity,
    MeasurementUnit Unit);

public sealed record RecipeGenerationRequest(
    IReadOnlyCollection<AvailableProduct> AvailableProducts,
    MealType MealType,
    int Servings,
    int MaxCookingTimeMinutes,
    IReadOnlyCollection<string> ExcludedRecipeNames,
    bool AllowMissingIngredients = false,
    IReadOnlyCollection<string>? PreviousErrors = null);

public sealed record GeneratedRecipeDto(
    string Name,
    MealType MealType,
    int Servings,
    int CookingTimeMinutes,
    IReadOnlyList<RecipeIngredientDto> Ingredients,
    IReadOnlyList<string> Steps);

public sealed record RecipeIngredientDto(
    string ProductName,
    decimal Quantity,
    MeasurementUnit Unit,
    bool IsOptional);

public enum RecipeGenerationErrorKind
{
    Configuration = 1,
    Timeout = 2,
    Transport = 3,
    Api = 4,
    InvalidResponse = 5
}

public sealed class RecipeGenerationException : Exception
{
    public RecipeGenerationException(
        RecipeGenerationErrorKind kind,
        string message,
        bool isRetryable,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        IsRetryable = isRetryable;
    }

    public RecipeGenerationErrorKind Kind { get; }

    public bool IsRetryable { get; }
}
