using FluentValidation;
using MealBot.Application.MealPlans;
using MealBot.Domain.Entities;
using MealBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MealBot.Infrastructure.MealPlans;

public sealed class MealPlanService(
    MealBotDbContext dbContext,
    IValidator<CreateMealPlanRequest> requestValidator) : IMealPlanService
{
    public async Task<MealPlanDto> CreateAsync(
        CreateMealPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        await requestValidator.ValidateAndThrowAsync(request, cancellationToken);

        var user = await FindUserAsync(request.TelegramUserId, cancellationToken)
            ?? throw new InvalidOperationException(
                "Пользователь не авторизован. Сначала отправьте /start");

        var mealPlan = new MealPlan(
            user,
            request.StartDate,
            request.DaysCount,
            request.MealTypes,
            request.Servings);

        dbContext.MealPlans.Add(mealPlan);
        await dbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(mealPlan);
    }

    private Task<User?> FindUserAsync(
        long telegramUserId,
        CancellationToken cancellationToken) =>
        dbContext.Users.SingleOrDefaultAsync(
            user => user.TelegramId == telegramUserId,
            cancellationToken);

    private static MealPlanDto MapToDto(MealPlan mealPlan)
    {
        var plannedMeals = mealPlan.PlannedMeals
            .OrderBy(plannedMeal => plannedMeal.MealDate)
            .ThenBy(plannedMeal => plannedMeal.MealType)
            .Select(plannedMeal => new PlannedMealDto(
                plannedMeal.Id,
                plannedMeal.MealDate,
                plannedMeal.MealType,
                plannedMeal.Servings,
                plannedMeal.Status))
            .ToList();

        if (plannedMeals.Count == 0)
        {
            throw new InvalidOperationException(
                "Не удалось создать меню: в нём нет запланированных блюд");
        }

        return new MealPlanDto(
            mealPlan.Id,
            mealPlan.StartDate,
            mealPlan.DaysCount,
            plannedMeals
                .Select(plannedMeal => plannedMeal.MealType)
                .Distinct()
                .OrderBy(mealType => mealType)
                .ToList(),
            plannedMeals[0].Servings,
            mealPlan.Status,
            plannedMeals);
    }
}
