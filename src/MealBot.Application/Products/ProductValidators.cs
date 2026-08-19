using FluentValidation;
using MealBot.Domain;

namespace MealBot.Application.Products;

public sealed class AddProductRequestValidator : AbstractValidator<AddProductRequest>
{
    public AddProductRequestValidator()
    {
        RuleFor(request => request.TelegramUserId)
            .GreaterThan(0);
        RuleFor(request => request.Name)
            .NotEmpty()
            .Must(name => name.Trim().Length is >= 2 and <= ProductName.MaxLength)
            .WithMessage($"Название продукта должно содержать от 2 до {ProductName.MaxLength} символов");
        RuleFor(request => request.Quantity)
            .GreaterThan(0)
            .LessThanOrEqualTo(ProductLimits.MaxQuantity)
            .WithMessage(ProductLimits.MaxQuantityErrorMessage);
        RuleFor(request => request.Unit)
            .IsInEnum();
    }
}

public sealed class UpdateInventoryItemRequestValidator : AbstractValidator<UpdateInventoryItemRequest>
{
    public UpdateInventoryItemRequestValidator()
    {
        RuleFor(request => request.TelegramUserId)
            .GreaterThan(0);
        RuleFor(request => request.InventoryItemId)
            .NotEmpty();
        RuleFor(request => request.Quantity)
            .GreaterThan(0)
            .LessThanOrEqualTo(ProductLimits.MaxQuantity)
            .WithMessage(ProductLimits.MaxQuantityErrorMessage);
        RuleFor(request => request.Unit)
            .IsInEnum();
    }
}
