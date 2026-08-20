using MealBot.Domain;

namespace MealBot.Application.MealPlans;

public sealed record CreateMealPlanRequest(
    long TelegramUserId,
    DateOnly StartDate,
    int DaysCount,
    IReadOnlyCollection<MealType> MealTypes,
    int Servings
);

public sealed record PlannedMealDto(
    Guid Id,
    DateOnly MealDate,
    MealType MealType,
    int Servings,
    PlannedMealStatus Status
);


public sealed record MealPlanDto(
    Guid Id,
    DateOnly StartDate,
    int DaysCount,
    IReadOnlyCollection<MealType> MealTypes,
    int Servings,
    MealPlanStatus Status,
    IReadOnlyList<PlannedMealDto> PlannedMeals
);
