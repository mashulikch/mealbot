using System.Collections.Concurrent;
using System.Globalization;
using MealBot.Application.Products;
using MealBot.Bot.Keyboards;
using MealBot.Bot.Options;
using MealBot.Domain;
using MealBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace MealBot.Bot.Services;

public sealed class TelegramBotHostedService(
    IOptions<TelegramBotOptions> telegramBotOptions,
    IServiceScopeFactory scopeFactory,
    ILogger<TelegramBotHostedService> logger) : BackgroundService
{
    private const string StartCommand = "/start";
    private const string CancelCommand = "/cancel";

    private const string UnavailableMenuMessage =
        "Раздел меню будет добавлен на следующих этапах MVP";

    private const string InvalidCommandMessage =
        "Выберите действие на клавиатуре или отправьте /start";

    private static readonly string InvalidProductNameMessage =
        $"Название должно содержать от 2 до {ProductName.MaxLength} символов. Попробуйте ещё раз";

    private const string ProductNamePrompt =
        "Введите название продукта, например: Курица\n\nДля отмены отправьте /cancel";

    private const string QuantityPrompt =
        "Введите количество. Можно использовать точку или запятую, например: 1000 или 1,5";

    private const string InvalidQuantityMessage =
        "Количество должно быть положительным числом, например: 1000 или 1,5";

    private const string UnitSelectionPrompt = "Выберите единицу измерения:";

    private const string AddDialogExpiredMessage =
        "Диалог добавления устарел. Нажмите «Добавить продукт» ещё раз";

    private const string EditDialogExpiredMessage =
        "Диалог изменения устарел. Откройте «Мои продукты» ещё раз";

    private readonly ConcurrentDictionary<long, ProductDialogState> activeDialogs = new();

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
            await TrySendProcessingErrorAsync(telegramClient, update, cancellationToken);
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
                activeDialogs.TryRemove(telegramUserId, out _);
                await SendStartMessageAsync(telegramClient, message, cancellationToken);
                return;

            case var text when text.Equals(CancelCommand, StringComparison.OrdinalIgnoreCase):
                activeDialogs.TryRemove(telegramUserId, out _);
                await SendMainMenuMessageAsync(
                    telegramClient,
                    message.Chat.Id,
                    "Действие отменено",
                    cancellationToken);
                return;
        }

        if (activeDialogs.TryGetValue(telegramUserId, out var dialogState))
        {
            await HandleProductDialogMessageAsync(
                telegramClient,
                message,
                dialogState,
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
                StartAddProductDialog(telegramUserId);
                await SendMessageAsync(
                    telegramClient,
                    message.Chat.Id,
                    ProductNamePrompt,
                    cancellationToken);
                return;

            case MainMenuKeyboard.CreateMenuText:
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

    private async Task HandleProductDialogMessageAsync(
        ITelegramBotClient telegramClient,
        Message message,
        ProductDialogState dialogState,
        CancellationToken cancellationToken)
    {
        var text = message.Text!.Trim();

        switch (dialogState.Mode)
        {
            case ProductDialogMode.AddName:
                await HandleProductNameInputAsync(
                    telegramClient,
                    message.Chat.Id,
                    dialogState,
                    text,
                    cancellationToken);
                return;

            case ProductDialogMode.AddQuantity:
            case ProductDialogMode.EditQuantity:
                await HandleQuantityInputAsync(
                    telegramClient,
                    message.Chat.Id,
                    dialogState,
                    text,
                    cancellationToken);
                return;

            default:
                logger.LogWarning(
                    "Unknown product dialog mode {DialogMode}",
                    dialogState.Mode);
                activeDialogs.TryRemove(message.From!.Id, out _);
                await SendMainMenuMessageAsync(
                    telegramClient,
                    message.Chat.Id,
                    "Диалог завершён. Начните действие ещё раз",
                    cancellationToken);
                return;
        }
    }

    private static async Task HandleProductNameInputAsync(
        ITelegramBotClient telegramClient,
        long chatId,
        ProductDialogState dialogState,
        string productName,
        CancellationToken cancellationToken)
    {
        if (productName.Length is < 2 or > ProductName.MaxLength)
        {
            await SendMessageAsync(
                telegramClient,
                chatId,
                InvalidProductNameMessage,
                cancellationToken);
            return;
        }

        dialogState.ProductName = productName;
        dialogState.Mode = ProductDialogMode.AddQuantity;

        await SendMessageAsync(
            telegramClient,
            chatId,
            QuantityPrompt,
            cancellationToken);
    }

    private static async Task HandleQuantityInputAsync(
        ITelegramBotClient telegramClient,
        long chatId,
        ProductDialogState dialogState,
        string quantityText,
        CancellationToken cancellationToken)
    {
        if (!TryParseQuantity(quantityText, out var quantity))
        {
            await SendMessageAsync(
                telegramClient,
                chatId,
                InvalidQuantityMessage,
                cancellationToken);
            return;
        }

        if (!TryGetUnitSelectionPrefix(dialogState, out var callbackPrefix))
        {
            await SendMessageAsync(
                telegramClient,
                chatId,
                "Диалог устарел. Начните действие ещё раз",
                cancellationToken);
            return;
        }

        dialogState.Quantity = quantity;

        await SendMessageAsync(
            telegramClient,
            chatId,
            UnitSelectionPrompt,
            cancellationToken,
            ProductKeyboard.CreateUnitSelection(callbackPrefix));
    }

    private static bool TryGetUnitSelectionPrefix(
        ProductDialogState dialogState,
        out string callbackPrefix)
    {
        callbackPrefix = dialogState.Mode switch
        {
            ProductDialogMode.AddQuantity => ProductKeyboard.AddUnitPrefix,
            ProductDialogMode.EditQuantity when dialogState.InventoryItemId is { } itemId =>
                $"{ProductKeyboard.EditUnitPrefix}{itemId}:",
            _ => string.Empty
        };

        return callbackPrefix.Length > 0;
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

    private async Task CompleteAddProductAsync(
        ITelegramBotClient telegramClient,
        CallbackQuery callbackQuery,
        string unitValue,
        long chatId,
        CancellationToken cancellationToken)
    {
        var telegramUser = callbackQuery.From!;

        if (!ProductKeyboard.TryParseUnit(unitValue, out var unit)
            || !TryGetDialogState(
                telegramUser.Id,
                ProductDialogMode.AddQuantity,
                out var dialogState)
            || string.IsNullOrWhiteSpace(dialogState.ProductName)
            || dialogState.Quantity is null)
        {
            await SendMessageAsync(
                telegramClient,
                chatId,
                AddDialogExpiredMessage,
                cancellationToken);
            return;
        }

        var result = await ExecuteWithProductServiceAsync(
            service => service.AddAsync(
                new AddProductRequest(
                    telegramUser.Id,
                    dialogState.ProductName!,
                    dialogState.Quantity.Value,
                    unit,
                    telegramUser.FirstName,
                    telegramUser.Username),
                cancellationToken));

        activeDialogs.TryRemove(telegramUser.Id, out _);

        await SendInventoryAsync(
            telegramClient,
            chatId,
            telegramUser.Id,
            cancellationToken,
            $"Добавлено: {result.ProductName} — {FormatQuantity(result.Quantity)} {ProductKeyboard.FormatUnit(result.Unit)}");
    }

    private async Task CompleteEditProductAsync(
        ITelegramBotClient telegramClient,
        CallbackQuery callbackQuery,
        string callbackValue,
        long chatId,
        CancellationToken cancellationToken)
    {
        var telegramUser = callbackQuery.From!;

        if (!TryParseEditUnitCallback(callbackValue, out var itemId, out var unit)
            || !TryGetDialogState(
                telegramUser.Id,
                ProductDialogMode.EditQuantity,
                out var dialogState)
            || dialogState.InventoryItemId != itemId
            || dialogState.Quantity is null)
        {
            await SendMessageAsync(
                telegramClient,
                chatId,
                EditDialogExpiredMessage,
                cancellationToken);
            return;
        }

        var result = await ExecuteWithProductServiceAsync(
            service => service.UpdateAsync(
                new UpdateInventoryItemRequest(
                    telegramUser.Id,
                    itemId,
                    dialogState.Quantity.Value,
                    unit),
                cancellationToken));

        activeDialogs.TryRemove(telegramUser.Id, out _);

        await SendInventoryAsync(
            telegramClient,
            chatId,
            telegramUser.Id,
            cancellationToken,
            $"Изменено: {result.ProductName} — {FormatQuantity(result.Quantity)} {ProductKeyboard.FormatUnit(result.Unit)}");
    }

    private static bool TryParseEditUnitCallback(
        string callbackValue,
        out Guid inventoryItemId,
        out MeasurementUnit unit)
    {
        inventoryItemId = Guid.Empty;
        unit = default;

        var separatorIndex = callbackValue.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == callbackValue.Length - 1)
        {
            return false;
        }

        return Guid.TryParse(callbackValue[..separatorIndex], out inventoryItemId)
               && ProductKeyboard.TryParseUnit(
                   callbackValue[(separatorIndex + 1)..],
                   out unit);
    }

    private async Task StartEditProductAsync(
        ITelegramBotClient telegramClient,
        CallbackQuery callbackQuery,
        string inventoryItemIdValue,
        long chatId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(inventoryItemIdValue, out var inventoryItemId))
        {
            await SendMessageAsync(
                telegramClient,
                chatId,
                "Не удалось определить продукт",
                cancellationToken);
            return;
        }

        activeDialogs[callbackQuery.From!.Id] = new ProductDialogState(ProductDialogMode.EditQuantity)
        {
            InventoryItemId = inventoryItemId
        };

        await SendMessageAsync(
            telegramClient,
            chatId,
            "Введите новое количество. После этого можно будет изменить единицу измерения.\n\nДля отмены отправьте /cancel",
            cancellationToken);
    }

    private async Task DeleteProductAsync(
        ITelegramBotClient telegramClient,
        CallbackQuery callbackQuery,
        string inventoryItemIdValue,
        long chatId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(inventoryItemIdValue, out var inventoryItemId))
        {
            await SendMessageAsync(
                telegramClient,
                chatId,
                "Не удалось определить продукт",
                cancellationToken);
            return;
        }

        var telegramUserId = callbackQuery.From!.Id;

        await ExecuteWithProductServiceAsync(
            service => service.DeleteAsync(
                telegramUserId,
                inventoryItemId,
                cancellationToken));

        activeDialogs.TryRemove(telegramUserId, out _);

        await SendInventoryAsync(
            telegramClient,
            chatId,
            telegramUserId,
            cancellationToken,
            "Продукт удалён из запасов");
    }

    private async Task SendInventoryAsync(
        ITelegramBotClient telegramClient,
        long chatId,
        long telegramUserId,
        CancellationToken cancellationToken,
        string? prefix = null)
    {
        var inventory = await ExecuteWithProductServiceAsync(
            service => service.GetInventoryAsync(telegramUserId, cancellationToken));

        var inventoryMessage = BuildInventoryMessage(inventory, prefix);
        var keyboard = inventory.Count == 0
            ? null
            : ProductKeyboard.CreateInventoryActions(
                inventory.Select(item => (item.Id, item.ProductName)).ToList());

        await SendMessageAsync(
            telegramClient,
            chatId,
            inventoryMessage,
            cancellationToken,
            keyboard);
    }

    private static string BuildInventoryMessage(
        IReadOnlyList<InventoryItemDto> inventory,
        string? prefix)
    {
        var inventoryText = inventory.Count == 0
            ? "Запасы пока пусты. Нажмите «Добавить продукт», чтобы добавить первый продукт"
            : "Ваши продукты:\n\n" + string.Join(
                "\n",
                inventory.Select(FormatInventoryItem));

        return string.IsNullOrWhiteSpace(prefix)
            ? inventoryText
            : $"{prefix}\n\n{inventoryText}";
    }

    private static string FormatInventoryItem(InventoryItemDto inventoryItem)
    {
        var unitName = ProductKeyboard.FormatUnit(inventoryItem.Unit);
        var reservedQuantity = inventoryItem.ReservedQuantity > 0
            ? $" (свободно {FormatQuantity(inventoryItem.AvailableQuantity)} {unitName})"
            : string.Empty;

        return $"• {inventoryItem.ProductName} — {FormatQuantity(inventoryItem.Quantity)} {unitName}{reservedQuantity}";
    }

    private async Task EnsureUserAsync(
        User telegramUser,
        CancellationToken cancellationToken)
    {
        await ExecuteWithProductServiceAsync(
            service => service.EnsureUserAsync(
                telegramUser.Id,
                telegramUser.FirstName,
                telegramUser.Username,
                cancellationToken));
    }

    private bool TryGetDialogState(
        long telegramUserId,
        ProductDialogMode expectedMode,
        out ProductDialogState dialogState)
    {
        if (activeDialogs.TryGetValue(telegramUserId, out var currentState)
            && currentState.Mode == expectedMode)
        {
            dialogState = currentState;
            return true;
        }

        dialogState = null!;
        return false;
    }

    private async Task<TResult> ExecuteWithProductServiceAsync<TResult>(
        Func<IProductService, Task<TResult>> operation)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var productService = scope.ServiceProvider.GetRequiredService<IProductService>();

        return await operation(productService);
    }

    private Task ExecuteWithProductServiceAsync(Func<IProductService, Task> operation) =>
        ExecuteWithProductServiceAsync(async productService =>
        {
            await operation(productService);
            return true;
        });

    private static Task SendStartMessageAsync(
        ITelegramBotClient telegramClient,
        Message message,
        CancellationToken cancellationToken)
    {
        var firstName = message.From?.FirstName ?? "привет";
        var welcomeMessage = $"""
                    Привет, {firstName}!

                    Я помогу собрать меню из продуктов, которые у тебя есть дома.
                    Для начала добавь продукты, нажав «Добавить продукт».
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
                "Не удалось обработать запрос. Попробуйте ещё раз",
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to send Telegram processing error message.");
        }
    }

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

    private Task HandlePollingErrorAsync(
        ITelegramBotClient telegramClient,
        Exception exception,
        HandleErrorSource errorSource,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Telegram polling failed from {ErrorSource}.", errorSource);
        return Task.CompletedTask;
    }

    private void StartAddProductDialog(long telegramUserId)
    {
        activeDialogs[telegramUserId] = new ProductDialogState(ProductDialogMode.AddName);
    }

    private static bool TryGetTextMessage(Update update, out Message message)
    {
        message = update.Message!;
        return message is not null
               && !string.IsNullOrWhiteSpace(message.Text)
               && message.From is not null;
    }

    private static bool TryParseQuantity(string value, out decimal quantity)
    {
        var normalizedValue = value.Trim().Replace(',', '.');

        return decimal.TryParse(
                   normalizedValue,
                   NumberStyles.AllowDecimalPoint,
                   CultureInfo.InvariantCulture,
                   out quantity)
               && quantity > 0
               && quantity <= ProductLimits.MaxQuantity;
    }

    private static string FormatQuantity(decimal quantity) =>
        quantity.ToString("0.###", CultureInfo.InvariantCulture);

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
