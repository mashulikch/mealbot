using Telegram.Bot;
using MealBot.Bot.Keyboards;
using Telegram.Bot.Types;

namespace MealBot.Bot.Services;

public sealed partial class TelegramBotHostedService
{
    private async Task HandleUpdateAsync(
        ITelegramBotClient telegramClient,
        Update update,
        CancellationToken cancellationToken)
    {
        try
        {
            if (update.CallbackQuery is not null)
            {
                await HandleCallbackQueryAsync(
                    telegramClient,
                    update.CallbackQuery,
                    cancellationToken);
                return;
            }

            if (!TryGetTextMessage(update, out var message))
            {
                return;
            }

            await EnsureUserAsync(message.From!, cancellationToken);
            await HandleTextMessageAsync(telegramClient, message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Processing Telegram update was canceled.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to process Telegram update.");
            await TrySendProcessingErrorAsync(
                telegramClient,
                update,
                exception,
                cancellationToken);
        }
    }

    private async Task HandleTextMessageAsync(
        ITelegramBotClient telegramClient,
        Message message,
        CancellationToken cancellationToken)
    {
        var telegramUserId = message.From!.Id;
        var messageText = message.Text!.Trim();

        logger.LogInformation(
            "Received Telegram message from user {TelegramUserId}: {MessageText}",
            telegramUserId,
            messageText);

        switch (messageText)
        {
            case var text when text.Equals(StartCommand, StringComparison.OrdinalIgnoreCase):
                ResetDialogStates(telegramUserId);
                await SendStartMessageAsync(telegramClient, message, cancellationToken);
                return;

            case var text when text.Equals(CancelCommand, StringComparison.OrdinalIgnoreCase):
                ResetDialogStates(telegramUserId);
                await SendMainMenuMessageAsync(
                    telegramClient,
                    message.Chat.Id,
                    "Действие отменено",
                    cancellationToken);
                return;
        }

        if (_productDialogs.TryGetValue(telegramUserId, out var dialogState))
        {
            await HandleProductDialogMessageAsync(
                telegramClient,
                message,
                dialogState,
                cancellationToken);
            return;
        }

        if (_menuDialogs.TryGetValue(telegramUserId, out var menuDialogState))
        {
            await SendMenuDialogPromptAsync(
                telegramClient,
                message.Chat.Id,
                menuDialogState,
                "Используйте кнопки для настройки меню или отправьте /cancel",
                cancellationToken);
            return;
        }

        await HandleMainMenuCommandAsync(
            telegramClient,
            message,
            messageText,
            cancellationToken);
    }

    private async Task HandleMainMenuCommandAsync(
        ITelegramBotClient telegramClient,
        Message message,
        string messageText,
        CancellationToken cancellationToken)
    {
        var telegramUserId = message.From!.Id;

        switch (messageText)
        {
            case MainMenuKeyboard.MyProductsText:
                await SendInventoryAsync(
                    telegramClient,
                    message.Chat.Id,
                    telegramUserId,
                    cancellationToken);
                return;

            case MainMenuKeyboard.AddProductText:
                _menuDialogs.TryRemove(telegramUserId, out _);
                StartAddProductDialog(telegramUserId);
                await SendMessageAsync(
                    telegramClient,
                    message.Chat.Id,
                    ProductNamePrompt,
                    cancellationToken);
                return;

            case MainMenuKeyboard.CreateMenuText:
                _productDialogs.TryRemove(telegramUserId, out _);
                StartMenuPlanningDialog(telegramUserId);
                await SendMessageAsync(
                    telegramClient,
                    message.Chat.Id,
                    SelectDaysPrompt,
                    cancellationToken,
                    MenuKeyboard.CreateDaysSelection());
                return;

            case MainMenuKeyboard.MyMenuText:
                await SendMainMenuMessageAsync(
                    telegramClient,
                    message.Chat.Id,
                    UnavailableMenuMessage,
                    cancellationToken);
                return;

            default:
                await SendMainMenuMessageAsync(
                    telegramClient,
                    message.Chat.Id,
                    InvalidCommandMessage,
                    cancellationToken);
                return;
        }
    }


    private async Task HandleCallbackQueryAsync(
        ITelegramBotClient telegramClient,
        CallbackQuery callbackQuery,
        CancellationToken cancellationToken)
    {
        try
        {
            var telegramUser = callbackQuery.From;
            var callbackData = callbackQuery.Data;
            var chat = callbackQuery.Message?.Chat;

            if (telegramUser is null
                || string.IsNullOrWhiteSpace(callbackData)
                || chat is null)
            {
                return;
            }

            await EnsureUserAsync(telegramUser, cancellationToken);
            await DispatchCallbackAsync(
                telegramClient,
                callbackQuery,
                callbackData,
                chat.Id,
                cancellationToken);
        }
        finally
        {
            await TryAnswerCallbackQueryAsync(telegramClient, callbackQuery, cancellationToken);
        }
    }

    private async Task DispatchCallbackAsync(
        ITelegramBotClient telegramClient,
        CallbackQuery callbackQuery,
        string callbackData,
        long chatId,
        CancellationToken cancellationToken)
    {
        if (IsMenuPlanningCallback(callbackData))
        {
            await HandleMenuPlanningCallbackAsync(
                telegramClient,
                callbackQuery,
                callbackData,
                chatId,
                cancellationToken);
            return;
        }

        if (callbackData.StartsWith(ProductKeyboard.AddUnitPrefix, StringComparison.Ordinal))
        {
            await CompleteAddProductAsync(
                telegramClient,
                callbackQuery,
                callbackData[ProductKeyboard.AddUnitPrefix.Length..],
                chatId,
                cancellationToken);
            return;
        }

        if (callbackData.StartsWith(ProductKeyboard.EditUnitPrefix, StringComparison.Ordinal))
        {
            await CompleteEditProductAsync(
                telegramClient,
                callbackQuery,
                callbackData[ProductKeyboard.EditUnitPrefix.Length..],
                chatId,
                cancellationToken);
            return;
        }

        if (callbackData.StartsWith(ProductKeyboard.EditPrefix, StringComparison.Ordinal))
        {
            await StartEditProductAsync(
                telegramClient,
                callbackQuery,
                callbackData[ProductKeyboard.EditPrefix.Length..],
                chatId,
                cancellationToken);
            return;
        }

        if (callbackData.StartsWith(ProductKeyboard.DeletePrefix, StringComparison.Ordinal))
        {
            await DeleteProductAsync(
                telegramClient,
                callbackQuery,
                callbackData[ProductKeyboard.DeletePrefix.Length..],
                chatId,
                cancellationToken);
            return;
        }

        logger.LogWarning("Unknown Telegram callback data: {CallbackData}", callbackData);
    }

    private static bool IsMenuPlanningCallback(string callbackData) =>
        callbackData.StartsWith(MenuKeyboard.DaysPrefix, StringComparison.Ordinal)
        || callbackData.StartsWith(MenuKeyboard.MealPrefix, StringComparison.Ordinal)
        || callbackData.Equals(MenuKeyboard.MealsConfirmCallback, StringComparison.Ordinal)
        || callbackData.StartsWith(MenuKeyboard.ServingsPrefix, StringComparison.Ordinal);


    private static bool TryGetTextMessage(Update update, out Message message)
    {
        if (update.Message is not { From: not null } candidate
            || string.IsNullOrWhiteSpace(candidate.Text))
        {
            message = null!;
            return false;
        }

        message = candidate;
        return true;
    }

}



