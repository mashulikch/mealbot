using FluentValidation;
using MealBot.Bot.Keyboards;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace MealBot.Bot.Services;

public sealed partial class TelegramBotHostedService
{
    private static Task SendStartMessageAsync(
        ITelegramBotClient telegramClient,
        Message message,
        CancellationToken cancellationToken)
    {
        var firstName = message.From?.FirstName ?? "привет";
        var welcomeMessage = $"""
                    Привет, {firstName}!

                    Я помогу собрать меню из продуктов, которые у тебя есть дома.
                    Для начала добавь продукты, нажав «Добавить продукт»
                    """;

        return SendMessageAsync(
            telegramClient,
            message.Chat.Id,
            welcomeMessage,
            cancellationToken,
            MainMenuKeyboard.Create());
    }

    private static Task SendMainMenuMessageAsync(
        ITelegramBotClient telegramClient,
        long chatId,
        string responseText,
        CancellationToken cancellationToken) =>
        SendMessageAsync(
            telegramClient,
            chatId,
            responseText,
            cancellationToken,
            MainMenuKeyboard.Create());

    private static Task SendMessageAsync(
        ITelegramBotClient telegramClient,
        long chatId,
        string responseText,
        CancellationToken cancellationToken,
        ReplyMarkup? replyMarkup = null) =>
        telegramClient.SendMessage(
            chatId,
            responseText,
            replyMarkup: replyMarkup,
            cancellationToken: cancellationToken);

    private async Task TrySendProcessingErrorAsync(
        ITelegramBotClient telegramClient,
        Update update,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var chatId = update.Message?.Chat.Id ?? update.CallbackQuery?.Message?.Chat.Id;
        if (chatId is null || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await SendMessageAsync(
                telegramClient,
                chatId.Value,
                GetUserErrorMessage(exception),
                cancellationToken);
        }
        catch (Exception sendException)
        {
            logger.LogWarning(sendException, "Failed to send Telegram processing error message.");
        }
    }

    private static string GetUserErrorMessage(Exception exception) => exception switch
    {
        ValidationException validationException => string.Join(
            "\n",
            validationException.Errors
                .Select(error => $"• {error.ErrorMessage}")
                .Distinct()),
        KeyNotFoundException or InvalidOperationException or ArgumentException => exception.Message,
        _ => "Не удалось обработать запрос. Попробуйте ещё раз"
    };

    private async Task TryAnswerCallbackQueryAsync(
        ITelegramBotClient telegramClient,
        CallbackQuery callbackQuery,
        CancellationToken cancellationToken)
    {
        try
        {
            await telegramClient.AnswerCallbackQuery(
                callbackQuery.Id,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Хост завершает работу; отвечать на обратный вызов не требуется, можно идти спатки, гасите все свои :3
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to answer Telegram callback query.");
        }
    }

}




