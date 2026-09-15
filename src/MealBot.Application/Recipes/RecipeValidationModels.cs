using MealBot.Domain;

namespace MealBot.Application.Recipes;

public sealed record ProductCatalogItem(
    Guid ProductId,
    string Name,
    IReadOnlyCollection<string> Aliases);

public sealed record RecipeValidationError(
    string Code,
    string Message);


public sealed record ValidatedRecipeIngredientDto(
    Guid ProductId,
    string ProductName,
    decimal Quantity,
    MeasurementUnit Unit,
    bool IsOptional,
    decimal AvailableQuantity,
    decimal MissingQuantity);

public sealed record ValidatedRecipeDto(
    string Name,
    MealType MealType,
    int Servings,
    int CookingTimeMinutes,
    IReadOnlyList<ValidatedRecipeIngredientDto> Ingredients,
    IReadOnlyList<string> Steps)
{
    public GeneratedRecipeDto ToGeneratedRecipe() =>
        new(
            Name,
            MealType,
            Servings,
            CookingTimeMinutes,
            Ingredients
                .Select(ingredient => new RecipeIngredientDto(
                    ingredient.ProductName,
                    ingredient.Quantity,
                    ingredient.Unit,
                    ingredient.IsOptional))
                .ToArray(),
            Steps);
}

public sealed record RecipeValidationResult(
    bool IsValid,
    ValidatedRecipeDto? Recipe,
    IReadOnlyList<RecipeValidationError> Errors)
{
    public static RecipeValidationResult Valid(ValidatedRecipeDto recipe) =>
        new(true, recipe, Array.Empty<RecipeValidationError>());

    public static RecipeValidationResult Invalid(
        IEnumerable<RecipeValidationError> errors) =>
        new(false, null, errors.ToArray());
}

public interface IRecipeValidator
{
    RecipeValidationResult Validate(
        GeneratedRecipeDto recipe,
        RecipeGenerationRequest request,
        IReadOnlyCollection<ProductCatalogItem> productCatalog);
}

public interface IProductCatalog
{
    Task<IReadOnlyCollection<ProductCatalogItem>> FindByNamesAsync(
        IReadOnlyCollection<string> productNames,
        CancellationToken cancellationToken = default);
}

public interface IRecipeGenerationService
{
    Task<ValidatedRecipeDto> GenerateAndValidateAsync(
        RecipeGenerationRequest request,
        CancellationToken cancellationToken = default);
}
