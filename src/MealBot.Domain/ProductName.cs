namespace MealBot.Domain;

public static class ProductName
{
    public const int MinLength = 2;
    public const int MaxLength = 100;

    public static bool IsValid(string? name)
    {
        var normalizedName = CollapseWhitespace(name);
        return normalizedName.Length is >= MinLength and <= MaxLength;
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

    private static string CollapseWhitespace(string? name) =>
        string.Join(
            ' ',
            (name ?? string.Empty)
                .Trim()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
