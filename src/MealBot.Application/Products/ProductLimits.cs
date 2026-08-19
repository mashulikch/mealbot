namespace MealBot.Application.Products;

public static class ProductLimits
{
    public const decimal MaxQuantity = 1_000_000m;
    public const string MaxQuantityErrorMessage =
        "Количество должно быть больше нуля и не больше 1 000 000";
}
