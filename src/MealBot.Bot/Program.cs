using MealBot.Bot.Options;
using MealBot.Bot.Services;

LoadDotEnv();

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<TelegramBotOptions>(
    builder.Configuration.GetSection(TelegramBotOptions.SectionName));

builder.Services.AddHostedService<TelegramBotHostedService>();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var host = builder.Build();
host.Run();

static void LoadDotEnv()
{
    try
    {
        var envFilePath = FindDotEnv(Directory.GetCurrentDirectory())
                          ?? FindDotEnv(AppContext.BaseDirectory);

        if (envFilePath is null)
        {
            return;
        }

        foreach (var line in File.ReadLines(envFilePath))
        {
            if (!TryParseEnvironmentVariable(line, out var key, out var value))
            {
                continue;
            }

            SetEnvironmentVariableIfMissing(key, value);
        }
    }
    catch (IOException exception)
    {
        Console.Error.WriteLine($"Could not read .env file: {exception.Message}");
    }
    catch (UnauthorizedAccessException exception)
    {
        Console.Error.WriteLine($"Could not access .env file: {exception.Message}");
    }
}

static bool TryParseEnvironmentVariable(
    string rawLine,
    out string key,
    out string value)
{
    key = string.Empty;
    value = string.Empty;

    var line = rawLine.Trim();
    if (line.Length == 0 || line.StartsWith('#'))
    {
        return false;
    }

    var separatorIndex = line.IndexOf('=');
    if (separatorIndex <= 0)
    {
        return false;
    }

    key = line[..separatorIndex].Trim();
    value = line[(separatorIndex + 1)..].Trim();

    if (value.Length >= 2
        && ((value[0] == '"' && value[^1] == '"')
            || (value[0] == '\'' && value[^1] == '\'')))
    {
        value = value[1..^1];
    }

    return !string.IsNullOrWhiteSpace(key);
}

static void SetEnvironmentVariableIfMissing(string key, string value)
{
    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
    {
        Environment.SetEnvironmentVariable(key, value);
    }
}

static string? FindDotEnv(string startDirectory)
{
    var directory = new DirectoryInfo(startDirectory);

    while (directory != null)
    {
        var candidate = Path.Combine(directory.FullName, ".env");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        directory = directory.Parent;
    }

    return null;
}
