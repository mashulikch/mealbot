using MealBot.Domain;

namespace MealBot.Domain.Entities;

public sealed class MealPlan
{
    private MealPlan()
    {
    }

    public MealPlan(
        User user,
        DateOnly startDate,
        int daysCount,
        IEnumerable<MealType> mealTypes,
        int servings)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (daysCount is < 1 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(daysCount), "Период меню должен быть от 1 до 7 дней");
        }

        if (servings is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(servings), "Количество порций должно быть от 1 до 12");
        }

        var selectedMealTypes = mealTypes
            .Distinct()
            .OrderBy(item => item)
            .ToArray();

        if (selectedMealTypes.Length == 0)
        {
            throw new ArgumentException("Нужно выбрать хотя бы один приём пищи", nameof(mealTypes));
        }

        if (selectedMealTypes.Any(item => !Enum.IsDefined(item)))
        {
            throw new ArgumentException("Выбран неизвестный приём пищи", nameof(mealTypes));
        }

        Id = Guid.NewGuid();
        User = user;
        UserId = user.Id;
        StartDate = startDate;
        DaysCount = daysCount;
        Status = MealPlanStatus.Draft;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;

        for (var day = 0; day < daysCount; day++)
        {
            var mealDate = startDate.AddDays(day);
            foreach (var mealType in selectedMealTypes)
            {
                PlannedMeals.Add(new PlannedMeal(this, mealDate, mealType, servings));
            }
        }
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid UserId { get; private set; }

    public DateOnly StartDate { get; private set; }

    public int DaysCount { get; private set; }

    public MealPlanStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public User User { get; private set; } = null!;

    public ICollection<PlannedMeal> PlannedMeals { get; private set; } = new List<PlannedMeal>();
}
