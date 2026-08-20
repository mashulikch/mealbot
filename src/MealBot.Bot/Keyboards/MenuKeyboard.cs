using MealBot.Domain;
using Telegram.Bot.Types.ReplyMarkups;

namespace MealBot.Bot.Keyboards;

public static class MenuKeyboard
{
    public const string DaysPrefix = "menu:days:";
    public const string MealPrefix = "menu:meal:";
    public const string MealsConfirmCallback = "menu:meals:confirm";
    public const string ServingsPrefix = "menu:servings:";

    public static InlineKeyboardMarkup CreateDaysSelection() =>
        new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                Button("1 день", DaysPrefix + "1"),
                Button("3 дня", DaysPrefix + "3")
            },
            new[]
            {
                Button("5 дней", DaysPrefix + "5"),
                Button("7 дней", DaysPrefix + "7")
            }
        });

    public static InlineKeyboardMarkup CreateMealSelection(IReadOnlySet<MealType> selectedMealTypes) =>
        new InlineKeyboardMarkup(new[]
        {
            new[] { Button(FormatMealType(MealType.Breakfast, selectedMealTypes), MealPrefix + MealType.Breakfast) },
            new[] { Button(FormatMealType(MealType.Lunch, selectedMealTypes), MealPrefix + MealType.Lunch) },
            new[] { Button(FormatMealType(MealType.Dinner, selectedMealTypes), MealPrefix + MealType.Dinner) },
            new[] { Button("Продолжить", MealsConfirmCallback) }
        });

    public static InlineKeyboardMarkup CreateServingsSelection() =>
        new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                Button("1", ServingsPrefix + "1"),
                Button("2", ServingsPrefix + "2"),
                Button("3", ServingsPrefix + "3"),
                Button("4", ServingsPrefix + "4")
            },
            new[]
            {
                Button("5", ServingsPrefix + "5"),
                Button("6", ServingsPrefix + "6"),
                Button("8", ServingsPrefix + "8"),
                Button("12", ServingsPrefix + "12")
            }
        });

    public static bool TryParseMealType(string value, out MealType mealType) =>
        Enum.TryParse(value, ignoreCase: true, out mealType)
        && Enum.IsDefined(mealType);

    private static InlineKeyboardButton Button(string text, string callbackData) =>
        InlineKeyboardButton.WithCallbackData(text, callbackData);

    private static string FormatMealType(MealType mealType, IReadOnlySet<MealType> selectedMealTypes)
    {
        var prefix = selectedMealTypes.Contains(mealType) ? "✅ " : string.Empty;
        var name = mealType switch
        {
            MealType.Breakfast => "Завтрак",
            MealType.Lunch => "Обед",
            MealType.Dinner => "Ужин",
            _ => mealType.ToString()
        };

        return prefix + name;
    }
}
