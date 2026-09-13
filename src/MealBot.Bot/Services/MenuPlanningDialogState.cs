using MealBot.Domain;

namespace MealBot.Bot.Services;

public enum MenuPlanningDialogMode
{
    SelectDays,
    SelectMeals,
    SelectServings
}

public sealed class MenuPlanningDialogState
{
    public MenuPlanningDialogMode Mode { get; set; } = MenuPlanningDialogMode.SelectDays;

    public int? DaysCount { get; set; }

    public HashSet<MealType> SelectedMealTypes { get; } = new();

    public int? Servings { get; set; }
}
