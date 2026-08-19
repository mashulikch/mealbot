namespace MealBot.Domain.Entities;

public sealed class User
{
    private User()
    {
    }

    public User(long telegramId, string? firstName, string? userName)
    {
        TelegramId = telegramId;
        FirstName = firstName;
        UserName = userName?.Trim();
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public long TelegramId { get; private set; }

    public string? FirstName { get; private set; }

    public string? UserName { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public ICollection<InventoryItem> InventoryItems { get; private set; } = new List<InventoryItem>();

    public void UpdateProfile(string? firstName, string? userName)
    {
        FirstName = firstName;
        UserName = userName?.Trim();
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
