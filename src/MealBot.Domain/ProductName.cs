namespace MealBot.Domain;

public static class ProductName
{
    public const int MinLength = 2;
    public const int MaxLength = 100;

    public static bool IsValid(string? name)
    {
        return TryNormalize(name, out _);
    }

    public static (string DisplayName, string NormalizedName) Normalize(string name)
    {
        var displayName = CollapseWhitespace(name);

        if (displayName.Length is < MinLength or > MaxLength)
        {
            throw new ArgumentException(
                $"Название продукта должно содержать от {MinLength} до {MaxLength} символов",
                nameof(name));
        }

        return (displayName, displayName.ToUpperInvariant());
    }

    public static bool TryNormalize(string? name, out string normalizedName)
    {
        var displayName = CollapseWhitespace(name);
        if (displayName.Length is < MinLength or > MaxLength)
        {
            normalizedName = string.Empty;
            return false;
        }

        normalizedName = displayName.ToUpperInvariant();
        return true;
    }

    private static string CollapseWhitespace(string? name) =>
        string.Join(
            ' ',
            (name ?? string.Empty)
                .Trim()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
