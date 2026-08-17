using MealBot.Bot.Keyboards;
using MealBot.Bot.Options;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace MealBot.Bot.Services;

public sealed class TelegramBotHostedService(
    IOptions<TelegramBotOptions> telegramBotOptions,
    ILogger<TelegramBotHostedService> logger) : BackgroundService
{
    private static readonly HashSet<string> MainMenuCommands = new(StringComparer.Ordinal)
    {
        MainMenuKeyboard.MyProductsText,
        MainMenuKeyboard.AddProductText,
        MainMenuKeyboard.CreateMenuText,
        MainMenuKeyboard.MyMenuText
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!TryGetBotToken(out var botToken))
        {
            await WaitForShutdownAsync(stoppingToken);
            return;
        }

        try
        {
            await StartPollingAsync(botToken, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Telegram bot polling stopped.");
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "Telegram bot failed to start polling.");
            await WaitForShutdownAsync(stoppingToken);
        }
    }

    private async Task StartPollingAsync(string botToken, CancellationToken stoppingToken)
    {
        var telegramClient = new TelegramBotClient(botToken);
        var botUser = await telegramClient.GetMe(stoppingToken);

        logger.LogInformation("MealBot @{Username} started", botUser.Username);

        await telegramClient.DeleteWebhook(
            dropPendingUpdates: true,
            cancellationToken: stoppingToken);

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = new[] { UpdateType.Message }
        };

        telegramClient.StartReceiving(
            HandleUpdateAsync,
            HandlePollingErrorAsync,
            receiverOptions,
            stoppingToken);

        await WaitForShutdownAsync(stoppingToken);
    }

    private async Task HandleUpdateAsync(
        ITelegramBotClient telegramClient,
        Update update,
        CancellationToken cancellationToken
    )
    {
        var message = update.Message;
        if (message is null || string.IsNullOrWhiteSpace(message.Text))
        {
            return;
        }

        var messageText = message.Text.Trim();
        var telegramUserId = message.From?.Id;
        logger.LogInformation(
            "Received Telegram message from user {TelegramUserId}: {MessageText}",
            telegramUserId,
            messageText);

        try
        {
            if (messageText.Equals("/start", StringComparison.OrdinalIgnoreCase))
            {
                await SendStartMessageAsync(telegramClient, message, cancellationToken);
                return;
            }

            if (MainMenuCommands.Contains(messageText))
            {
                await SendMainMenuMessageAsync(
                    telegramClient,
                    message,
                    "Этот раздел начнет работать на след. этапах",
                    cancellationToken);
                return;
            }

            await SendMainMenuMessageAsync(
                telegramClient,
                message,
                "Сорямба, я знаю /start и кнопки главного меню, остальное на твоей совести",
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(
                "Processing Telegram message was canceled for chat {ChatId}.",
                message.Chat.Id);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to process Telegram message for chat {ChatId}.",
                message.Chat.Id);
        }
    }

    private static Task SendStartMessageAsync(
        ITelegramBotClient telegramClient,
        Message message,
        CancellationToken cancellationToken
    )
    {
        var userName = message.From?.FirstName ?? "привет";
        var welcomeMessage = $"""
                    Привет, {userName}!

                    Я помогу собрать тебе меню из продуктов, которые у тебя есть дома.
                    Для начала добавь продукты, нажав "Добавить продукт", 
                    а также можешь посмотреть в "Мои продукты", какие продукты у тебя есть
                    """;

        return telegramClient.SendMessage(
            message.Chat.Id,
            welcomeMessage,
            replyMarkup: MainMenuKeyboard.Create(),
            cancellationToken: cancellationToken
        );
    }

    private static Task SendMainMenuMessageAsync(
        ITelegramBotClient telegramClient,
        Message message,
        string responseText,
        CancellationToken cancellationToken)
    {
        return telegramClient.SendMessage(
            message.Chat.Id,
            responseText,
            replyMarkup: MainMenuKeyboard.Create(),
            cancellationToken: cancellationToken);
    }

    private Task HandlePollingErrorAsync(
        ITelegramBotClient telegramClient,
        Exception exception,
        HandleErrorSource errorSource,
        CancellationToken cancellationToken
    )
    {
        logger.LogError(
            exception,
            "Telegram polling failed from {ErrorSource}.",
            errorSource);
        return Task.CompletedTask;
    }

    private bool TryGetBotToken(out string botToken)
    {
        botToken = telegramBotOptions.Value.Token;

        if (string.IsNullOrWhiteSpace(botToken))
        {
            logger.LogWarning("Telegram bot token is not configured. Set Token to enable polling.");
            return false;
        }

        if (IsTelegramBotTokenFormatValid(botToken))
        {
            return true;
        }

        logger.LogError(
            "Telegram bot token has invalid format. Use the token from BotFather, for example 123456789:ABC...");
        botToken = string.Empty;
        return false;
    }

    private static bool IsTelegramBotTokenFormatValid(string botToken)
    {
        var separatorIndex = botToken.IndexOf(':');

        return separatorIndex > 0
               && botToken[..separatorIndex].All(char.IsDigit)
               && botToken.Length - separatorIndex - 1 >= 30;
    }

    private static async Task WaitForShutdownAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Просто будет бот останавливаться и все 
        }
    }
}
