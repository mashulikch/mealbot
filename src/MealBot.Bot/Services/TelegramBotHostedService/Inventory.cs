using MealBot.Application.Products;
using MealBot.Bot.Keyboards;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace MealBot.Bot.Services;

public sealed partial class TelegramBotHostedService
{
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

        _productDialogs.TryRemove(telegramUserId, out _);

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

}




