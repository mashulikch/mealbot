namespace MealBot.Application.Recipes;

public interface IRecipeGenerator
{
    Task<GeneratedRecipeDto> GenerateAsync(
        RecipeGenerationRequest request,
        CancellationToken cancellationToken = default);
}
