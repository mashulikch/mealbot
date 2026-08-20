using MealBot.Domain;

namespace MealBot.Domain.Entities;

public sealed class PlannedMeal
{
    private PlannedMeal()
    {
    }

    internal PlannedMeal(MealPlan mealPlan, DateOnly mealDate, MealType mealType, int servings)
    {
        ArgumentNullException.ThrowIfNull(mealPlan);

        if (!Enum.IsDefined(mealType))
        {
            throw new ArgumentException("Неизвестный приём пищи", nameof(mealType));
        }

        if (servings is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(servings), "Количество порций должно быть от 1 до 12");
        }

        Id = Guid.NewGuid();
        MealPlan = mealPlan;
        MealPlanId = mealPlan.Id;
        MealDate = mealDate;
        MealType = mealType;
        Servings = servings;
        Status = PlannedMealStatus.Planned;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid MealPlanId { get; private set; }

    public DateOnly MealDate { get; private set; }

    public MealType MealType { get; private set; }

    public int Servings { get; private set; }

    public PlannedMealStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public MealPlan MealPlan { get; private set; } = null!;
}