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

        var user = await GetAuthorizedUserAsync(request.TelegramUserId, cancellationToken);

        var mealPlan = new MealPlan(
            user: user,
            startDate: request.StartDate,
            daysCount: request.DaysCount,
            mealTypes: request.MealTypes,
            servings: request.Servings);

        dbContext.MealPlans.Add(mealPlan);
        await SaveChangesAsync(cancellationToken);

        return MapToDto(mealPlan);
    }

    private async Task<User> GetAuthorizedUserAsync(
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.TelegramId == telegramUserId,
            cancellationToken);

        return user ?? throw new InvalidOperationException(
            "Пользователь не авторизован. Сначала отправьте /start");
    }

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

        var mealTypes = plannedMeals
            .Select(plannedMeal => plannedMeal.MealType)
            .Distinct()
            .OrderBy(mealType => mealType)
            .ToList();

        return new MealPlanDto(
            mealPlan.Id,
            mealPlan.StartDate,
            mealPlan.DaysCount,
            mealTypes,
            plannedMeals[0].Servings,
            mealPlan.Status,
            plannedMeals);
    }

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new InvalidOperationException(
                "Не удалось сохранить настройки меню. Попробуйте ещё раз",
                exception);
        }
    }
}
