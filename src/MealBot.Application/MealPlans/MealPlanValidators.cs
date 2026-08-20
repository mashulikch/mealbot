using FluentValidation;
using MealBot.Domain;

namespace MealBot.Application.MealPlans;

public sealed class CreateMealPlanRequestValidator : AbstractValidator<CreateMealPlanRequest>
{
    public CreateMealPlanRequestValidator()
    {
        RuleFor(request => request.TelegramUserId).GreaterThan(0);

        RuleFor(request => request.DaysCount)
            .InclusiveBetween(MealPlanLimits.MinDays, MealPlanLimits.MaxDays);

        RuleFor(request => request.MealTypes)
            .NotEmpty()
            .Must(mealTypes => mealTypes.All(Enum.IsDefined))
            .WithMessage("Нужно выбрать хотя бы один поддерживаемый приём пищи");

        RuleFor(request => request.Servings)
            .InclusiveBetween(MealPlanLimits.MinServings, MealPlanLimits.MaxServings);
    }
}
