using MealBot.Application.Recipes;
using MealBot.Domain;
using Xunit;

namespace MealBot.UnitTests.Recipes;

public sealed class RecipeValidatorTests
{
    private static readonly Guid ChickenBreastId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly RecipeValidator validator = new();

    [Fact]
    public void Validate_resolves_alias_and_returns_canonical_product()
    {
        var request = CreateRequest(
            new AvailableProduct("курица", 700, MeasurementUnit.Gram));
        var recipe = CreateRecipe(
            name: "Курица с рисом",
            ingredients:
            [
                new RecipeIngredientDto(
                    "куриное филе",
                    300,
                    MeasurementUnit.Gram,
                    IsOptional: false)
            ]);

        var result = validator.Validate(
            recipe,
            request,
            [
                new ProductCatalogItem(
                    ChickenBreastId,
                    "Куриная грудка",
                    ["курица", "куриное филе"])
            ]);

        Assert.True(result.IsValid);
        var ingredient = Assert.Single(result.Recipe!.Ingredients);
        Assert.Equal(ChickenBreastId, ingredient.ProductId);
        Assert.Equal("Куриная грудка", ingredient.ProductName);
        Assert.Equal(0m, ingredient.MissingQuantity);
    }

    [Fact]
    public void Validate_rejects_mismatched_meal_servings_duplicate_and_shortage()
    {
        var request = CreateRequest(
            [
                new AvailableProduct("Куриная грудка", 100, MeasurementUnit.Gram)
            ],
            mealType: MealType.Breakfast,
            servings: 2,
            excludedRecipeName: "Курица с рисом");
        var recipe = CreateRecipe(
            name: "  курица   с рисом ",
            mealType: MealType.Lunch,
            servings: 1,
            ingredients:
            [
                new RecipeIngredientDto(
                    "Куриная грудка",
                    120,
                    MeasurementUnit.Gram,
                    IsOptional: false)
            ]);

        var result = validator.Validate(
            recipe,
            request,
            [new ProductCatalogItem(ChickenBreastId, "Куриная грудка", [])]);

        Assert.False(result.IsValid);
        Assert.Contains("meal_type_mismatch", result.Errors.Select(error => error.Code));
        Assert.Contains("servings_mismatch", result.Errors.Select(error => error.Code));
        Assert.Contains("duplicate_recipe", result.Errors.Select(error => error.Code));
        Assert.Contains("insufficient_inventory", result.Errors.Select(error => error.Code));
    }

    [Fact]
    public void Validate_allows_missing_required_quantity_when_requested()
    {
        var request = CreateRequest(
            [
                new AvailableProduct("Куриная грудка", 100, MeasurementUnit.Gram)
            ],
            allowMissingIngredients: true);
        var recipe = CreateRecipe(
            ingredients:
            [
                new RecipeIngredientDto(
                    "Куриная грудка",
                    120,
                    MeasurementUnit.Gram,
                    IsOptional: false)
            ]);

        var result = validator.Validate(
            recipe,
            request,
            [new ProductCatalogItem(ChickenBreastId, "Куриная грудка", [])]);

        Assert.True(result.IsValid);
        Assert.Equal(20m, Assert.Single(result.Recipe!.Ingredients).MissingQuantity);
    }

    [Fact]
    public void Validate_rejects_unknown_product_and_unsupported_unit()
    {
        var request = CreateRequest(
            new AvailableProduct("Куриная грудка", 500, MeasurementUnit.Gram));
        var recipe = CreateRecipe(
            ingredients:
            [
                new RecipeIngredientDto(
                    "Неизвестный продукт",
                    50,
                    MeasurementUnit.Gram,
                    IsOptional: false),
                new RecipeIngredientDto(
                    "Куриная грудка",
                    50,
                    (MeasurementUnit)999,
                    IsOptional: false)
            ]);

        var result = validator.Validate(
            recipe,
            request,
            [new ProductCatalogItem(ChickenBreastId, "Куриная грудка", [])]);

        Assert.False(result.IsValid);
        Assert.Contains("unsupported_ingredient_unit", result.Errors.Select(error => error.Code));
        Assert.Contains("unknown_ingredient", result.Errors.Select(error => error.Code));
    }

    private static RecipeGenerationRequest CreateRequest(
        params AvailableProduct[] availableProducts) =>
        CreateRequest(
            availableProducts,
            mealType: MealType.Lunch,
            servings: 2,
            excludedRecipeName: null,
            allowMissingIngredients: false);

    private static RecipeGenerationRequest CreateRequest(
        AvailableProduct[] availableProducts,
        MealType mealType = MealType.Lunch,
        int servings = 2,
        string? excludedRecipeName = null,
        bool allowMissingIngredients = false) =>
        new(
            availableProducts,
            mealType,
            servings,
            MaxCookingTimeMinutes: 40,
            excludedRecipeName is null ? [] : [excludedRecipeName],
            allowMissingIngredients);

    private static GeneratedRecipeDto CreateRecipe(
        string name = "Курица с рисом",
        MealType mealType = MealType.Lunch,
        int servings = 2,
        IReadOnlyList<RecipeIngredientDto>? ingredients = null) =>
        new(
            name,
            mealType,
            servings,
            CookingTimeMinutes: 30,
            ingredients ?? [new RecipeIngredientDto(
                "Куриная грудка",
                100,
                MeasurementUnit.Gram,
                IsOptional: false)],
            Steps: ["Приготовить ингредиенты.", "Подать блюдо."]);
}
