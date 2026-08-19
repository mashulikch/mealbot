namespace MealBot.Domain;

public static class ProductName
{
    public const int MaxLength = 100;

    public static (string DisplayName, string NormalizedName) Normalize(string name)
    {
        var displayName = string.Join(' ', (name ?? string.Empty).Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (displayName.Length is < 2 or > MaxLength)
        {
            throw new ArgumentException($"Название продукта должно содержать от 2 до {MaxLength} символов", nameof(name));
        }

        return (displayName, displayName.ToUpperInvariant());
    }
}