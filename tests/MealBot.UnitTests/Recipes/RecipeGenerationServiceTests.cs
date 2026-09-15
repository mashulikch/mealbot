using MealBot.Application.Recipes;
using MealBot.Domain;
using Xunit;

namespace MealBot.UnitTests.Recipes;

public sealed class RecipeGenerationServiceTests
{
    private static readonly Guid ProductId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task GenerateAndValidateAsync_retries_with_validation_errors()
    {
        var generator = new FakeRecipeGenerator(
            CreateRecipe("Уже есть"),
            CreateRecipe("Новое блюдо"));
        var service = new RecipeGenerationService(
            generator,
            new RecipeValidator(),
            new FakeProductCatalog());

        var request = new RecipeGenerationRequest(
            [new AvailableProduct("Курица", 500, MeasurementUnit.Gram)],
            MealType.Lunch,
            Servings: 2,
            MaxCookingTimeMinutes: 40,
            ExcludedRecipeNames: ["Уже есть"]);

        var result = await service.GenerateAndValidateAsync(request);

        Assert.Equal("Новое блюдо", result.Name);
        Assert.Equal(2, generator.Requests.Count);
        Assert.Contains(
            "Блюдо с названием",
            generator.Requests[1].PreviousErrors!.Single());
    }

    private static GeneratedRecipeDto CreateRecipe(string name) =>
        new(
            name,
            MealType.Lunch,
            Servings: 2,
            CookingTimeMinutes: 30,
            Ingredients:
            [
                new RecipeIngredientDto(
                    "Курица",
                    200,
                    MeasurementUnit.Gram,
                    IsOptional: false)
            ],
            Steps: ["Приготовить курицу."]);

    private sealed class FakeRecipeGenerator(params GeneratedRecipeDto[] recipes) : IRecipeGenerator
    {
        public List<RecipeGenerationRequest> Requests { get; } = [];

        public Task<GeneratedRecipeDto> GenerateAsync(
            RecipeGenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(recipes[Math.Min(Requests.Count - 1, recipes.Length - 1)]);
        }
    }

    private sealed class FakeProductCatalog : IProductCatalog
    {
        public Task<IReadOnlyCollection<ProductCatalogItem>> FindByNamesAsync(
            IReadOnlyCollection<string> productNames,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<ProductCatalogItem>>(
            [
                new ProductCatalogItem(ProductId, "Курица", [])
            ]);
    }
}
