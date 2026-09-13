namespace MealBot.Infrastructure.Recipes;

public sealed class RecipeGeneratorOptions
{
    public const string SectionName = "RecipeGenerator";

    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "gpt-4o-mini";

    public int TimeoutSeconds { get; set; } = 60;

    public int MaxAttempts { get; set; } = 3;

    public int RetryDelayMilliseconds { get; set; } = 250;

    public Uri GetBaseAddress()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseAddress)
            || baseAddress.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                $"{SectionName}:BaseUrl должен быть абсолютным HTTP(S)-адресом");
        }

        return new Uri(
            baseAddress.ToString().TrimEnd('/') + "/",
            UriKind.Absolute);
    }

    public TimeSpan GetTimeout()
    {
        if (TimeoutSeconds is < 1 or > 300)
        {
            throw new InvalidOperationException(
                $"{SectionName}:TimeoutSeconds должен быть от 1 до 300 секунд");
        }

        return TimeSpan.FromSeconds(TimeoutSeconds);
    }

    public void ValidateForRequest()
    {
        _ = GetBaseAddress();
        _ = GetTimeout();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                "Не настроен ключ сервиса генерации рецептов. " +
                "Укажите RecipeGenerator__ApiKey");
        }

        if (string.IsNullOrWhiteSpace(Model))
        {
            throw new InvalidOperationException(
                $"{SectionName}:Model не должен быть пустым");
        }

        if (MaxAttempts is < 1 or > 3)
        {
            throw new InvalidOperationException(
                $"{SectionName}:MaxAttempts должен быть от 1 до 3");
        }

        if (RetryDelayMilliseconds is < 0 or > 10_000)
        {
            throw new InvalidOperationException(
                $"{SectionName}:RetryDelayMilliseconds должен быть от 0 до 10000");
        }
    }
}
