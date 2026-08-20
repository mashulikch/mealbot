namespace MealBot.Application.MealPlans;

public interface IMealPlanService
{
    Task<MealPlanDto> CreateAsync(
        CreateMealPlanRequest request,
        CancellationToken cancellationToken = default
    );
}