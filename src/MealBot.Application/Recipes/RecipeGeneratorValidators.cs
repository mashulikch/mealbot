using FluentValidation;
using MealBot.Domain;

namespace MealBot.Application.Recipes;

public sealed class RecipeGenerationRequestValidator : AbstractValidator<RecipeGenerationRequest>
{
    public RecipeGenerationRequestValidator()
    {
        RuleFor(request => request.AvailableProducts)
            .NotNull()
            .NotEmpty()
            .WithMessage("Нужно передать хотя бы один доступный продукт");

        RuleForEach(request => request.AvailableProducts)
            .SetValidator(new AvailableProductValidator());

        RuleFor(request => request.MealType)
            .Must(Enum.IsDefined)
            .WithMessage("Указан неизвестный приём пищи");

        RuleFor(request => request.Servings)
            .InclusiveBetween(
                RecipeGenerationLimits.MinServings,
                RecipeGenerationLimits.MaxServings)
            .WithMessage(
                $"Количество порций должно быть от {RecipeGenerationLimits.MinServings} " +
                $"до {RecipeGenerationLimits.MaxServings}");

        RuleFor(request => request.MaxCookingTimeMinutes)
            .InclusiveBetween(
                RecipeGenerationLimits.MinCookingTimeMinutes,
                RecipeGenerationLimits.MaxCookingTimeMinutes)
            .WithMessage(
                $"Максимальное время приготовления должно быть от " +
                $"{RecipeGenerationLimits.MinCookingTimeMinutes} до " +
                $"{RecipeGenerationLimits.MaxCookingTimeMinutes} минут");

        RuleFor(request => request.ExcludedRecipeNames)
            .NotNull()
            .WithMessage("Список исключаемых блюд не должен быть пустым");

        RuleForEach(request => request.ExcludedRecipeNames)
            .NotEmpty()
            .WithMessage("Название исключаемого блюда не должно быть пустым");
    }

    private sealed class AvailableProductValidator : AbstractValidator<AvailableProduct>
    {
        public AvailableProductValidator()
        {
            RuleFor(product => product.Name)
                .Must(name => !string.IsNullOrWhiteSpace(name))
                .WithMessage("Название доступного продукта не должно быть пустым");
            RuleFor(product => product.Quantity).GreaterThan(0);
            RuleFor(product => product.Unit)
                .IsInEnum()
                .WithMessage("Указана неизвестная единица измерения");
        }
    }
}
