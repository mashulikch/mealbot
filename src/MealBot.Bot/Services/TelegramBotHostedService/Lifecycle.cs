using MealBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace MealBot.Bot.Services;

public sealed partial class TelegramBotHostedService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ApplyDatabaseMigrationsAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "MealBot database initialization failed.");
            await WaitForShutdownAsync(stoppingToken);
            return;
        }

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

    private async Task ApplyDatabaseMigrationsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MealBotDbContext>();

        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    private async Task StartPollingAsync(
        string botToken,
        CancellationToken cancellationToken)
    {
        var telegramClient = new TelegramBotClient(botToken);
        var botUser = await telegramClient.GetMe(cancellationToken);

        logger.LogInformation("MealBot @{Username} started", botUser.Username);

        await telegramClient.DeleteWebhook(
            dropPendingUpdates: true,
            cancellationToken: cancellationToken);

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery }
        };

        telegramClient.StartReceiving(
            HandleUpdateAsync,
            HandlePollingErrorAsync,
            receiverOptions,
            cancellationToken);

        await WaitForShutdownAsync(cancellationToken);
    }


    private Task HandlePollingErrorAsync(
        ITelegramBotClient telegramClient,
        Exception exception,
        HandleErrorSource errorSource,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Telegram polling failed from {ErrorSource}.", errorSource);
        return Task.CompletedTask;
    }


    private bool TryGetBotToken(out string botToken)
    {
        botToken = telegramBotOptions.Value.Token?.Trim() ?? string.Empty;

        if (botToken.Length == 0)
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

    private static async Task WaitForShutdownAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Просто будет бот останавливаться и все 
        }
    }
}




