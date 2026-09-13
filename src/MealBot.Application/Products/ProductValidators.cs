using FluentValidation;
using MealBot.Domain;

namespace MealBot.Application.Products;

public sealed class AddProductRequestValidator : AbstractValidator<AddProductRequest>
{
    public AddProductRequestValidator()
    {
        RuleFor(request => request.TelegramUserId).GreaterThan(0);

        RuleFor(request => request.Name)
            .NotEmpty()
            .Must(ProductName.IsValid)
            .WithMessage(
                $"Название продукта должно содержать от {ProductName.MinLength} " +
                $"до {ProductName.MaxLength} символов");

        RuleFor(request => request.Quantity).MustBeValidProductQuantity();
        RuleFor(request => request.Unit).IsInEnum();
    }
}

public sealed class UpdateInventoryItemRequestValidator : AbstractValidator<UpdateInventoryItemRequest>
{
    public UpdateInventoryItemRequestValidator()
    {
        RuleFor(request => request.TelegramUserId).GreaterThan(0);
        RuleFor(request => request.InventoryItemId).NotEmpty();
        RuleFor(request => request.Quantity).MustBeValidProductQuantity();
        RuleFor(request => request.Unit).IsInEnum();
    }
}

internal static class ProductValidationExtensions
{
    public static IRuleBuilderOptions<T, decimal> MustBeValidProductQuantity<T>(
        this IRuleBuilder<T, decimal> ruleBuilder) =>
        ruleBuilder
            .GreaterThan(0)
            .WithMessage(ProductLimits.MaxQuantityErrorMessage)
            .LessThanOrEqualTo(ProductLimits.MaxQuantity)
            .WithMessage(ProductLimits.MaxQuantityErrorMessage);
}
