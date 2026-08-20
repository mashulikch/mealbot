using System.Globalization;
using MealBot.Application.Products;
using MealBot.Bot.Keyboards;
using MealBot.Domain;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace MealBot.Bot.Services;

public sealed partial class TelegramBotHostedService
{
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
                _productDialogs.TryRemove(message.From!.Id, out _);
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

        _productDialogs.TryRemove(telegramUser.Id, out _);

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

        _productDialogs.TryRemove(telegramUser.Id, out _);

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

        _productDialogs[callbackQuery.From!.Id] = new ProductDialogState(ProductDialogMode.EditQuantity)
        {
            InventoryItemId = inventoryItemId
        };

        await SendMessageAsync(
            telegramClient,
            chatId,
            "Введите новое количество. После этого можно будет изменить единицу измерения.\n\nДля отмены отправьте /cancel",
            cancellationToken);
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
}




