using MealBot.Application.MealPlans;

namespace MealBot.Application.Recipes;

public static class RecipeGenerationLimits
{
    public const int MaxValidationAttempts = 3;
    public const int MinServings = MealPlanLimits.MinServings;
    public const int MaxServings = MealPlanLimits.MaxServings;
    public const int MinCookingTimeMinutes = 1;
    public const int MaxCookingTimeMinutes = 240;
}
