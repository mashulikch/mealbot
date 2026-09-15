using System.Globalization;
using MealBot.Application.MealPlans;
using MealBot.Bot.Keyboards;
using MealBot.Domain;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace MealBot.Bot.Services;

public sealed partial class TelegramBotHostedService
{
    private async Task HandleMenuPlanningCallbackAsync(
        ITelegramBotClient telegramClient,
        CallbackQuery callbackQuery,
        string callbackData,
        long chatId,
        CancellationToken cancellationToken)
    {
        var telegramUserId = callbackQuery.From!.Id;

        if (!_menuDialogs.TryGetValue(telegramUserId, out var dialogState))
        {
            await SendExpiredMenuDialogAsync(telegramClient, chatId, cancellationToken);
            return;
        }

        if (callbackData.StartsWith(MenuKeyboard.DaysPrefix, StringComparison.Ordinal))
        {
            await HandleDaysSelectionAsync(
                telegramClient,
                chatId,
                dialogState,
                callbackData,
                cancellationToken);
            return;
        }

        if (callbackData.StartsWith(MenuKeyboard.MealPrefix, StringComparison.Ordinal))
        {
            await HandleMealSelectionAsync(
                telegramClient,
                chatId,
                dialogState,
                callbackData,
                cancellationToken);
            return;
        }

        if (callbackData.Equals(MenuKeyboard.MealsConfirmCallback, StringComparison.Ordinal))
        {
            await ConfirmMealSelectionAsync(
                telegramClient,
                chatId,
                dialogState,
                cancellationToken);
            return;
        }

        if (callbackData.StartsWith(MenuKeyboard.ServingsPrefix, StringComparison.Ordinal))
        {
            await HandleServingsSelectionAsync(
                telegramClient,
                callbackQuery,
                chatId,
                dialogState,
                callbackData,
                cancellationToken);
            return;
        }

        logger.LogWarning("Unknown menu callback data: {CallbackData}", callbackData);
        await SendExpiredMenuDialogAsync(telegramClient, chatId, cancellationToken);
    }

    private async Task HandleDaysSelectionAsync(
        ITelegramBotClient telegramClient,
        long chatId,
        MenuPlanningDialogState dialogState,
        string callbackData,
        CancellationToken cancellationToken)
    {
        if (dialogState.Mode != MenuPlanningDialogMode.SelectDays
            || !TryParseSelection(
                callbackData,
                MenuKeyboard.DaysPrefix,
                MealPlanLimits.MinDays,
                MealPlanLimits.MaxDays,
                out var daysCount))
        {
            await SendExpiredMenuDialogAsync(telegramClient, chatId, cancellationToken);
            return;
        }

        dialogState.DaysCount = daysCount;
        dialogState.Mode = MenuPlanningDialogMode.SelectMeals;

        await SendMenuDialogPromptAsync(
            telegramClient,
            chatId,
            dialogState,
            prefix: null,
            cancellationToken);
    }

    private async Task HandleMealSelectionAsync(
        ITelegramBotClient telegramClient,
        long chatId,
        MenuPlanningDialogState dialogState,
        string callbackData,
        CancellationToken cancellationToken)
    {
        var mealTypeValue = callbackData[MenuKeyboard.MealPrefix.Length..];
        if (dialogState.Mode != MenuPlanningDialogMode.SelectMeals
            || !MenuKeyboard.TryParseMealType(mealTypeValue, out var mealType))
        {
            await SendExpiredMenuDialogAsync(telegramClient, chatId, cancellationToken);
            return;
        }

        if (!dialogState.SelectedMealTypes.Add(mealType))
        {
            dialogState.SelectedMealTypes.Remove(mealType);
        }

        await SendMenuDialogPromptAsync(
            telegramClient,
            chatId,
            dialogState,
            prefix: null,
            cancellationToken);
    }

    private async Task ConfirmMealSelectionAsync(
        ITelegramBotClient telegramClient,
        long chatId,
        MenuPlanningDialogState dialogState,
        CancellationToken cancellationToken)
    {
        if (dialogState.Mode != MenuPlanningDialogMode.SelectMeals)
        {
            await SendExpiredMenuDialogAsync(telegramClient, chatId, cancellationToken);
            return;
        }

        if (dialogState.SelectedMealTypes.Count == 0)
        {
            await SendMenuDialogPromptAsync(
                telegramClient,
                chatId,
                dialogState,
                "Выберите хотя бы один приём пищи",
                cancellationToken);
            return;
        }

        dialogState.Mode = MenuPlanningDialogMode.SelectServings;
        await SendMenuDialogPromptAsync(
            telegramClient,
            chatId,
            dialogState,
            prefix: null,
            cancellationToken);
    }

    private async Task HandleServingsSelectionAsync(
        ITelegramBotClient telegramClient,
        CallbackQuery callbackQuery,
        long chatId,
        MenuPlanningDialogState dialogState,
        string callbackData,
        CancellationToken cancellationToken)
    {
        if (dialogState.Mode != MenuPlanningDialogMode.SelectServings
            || !TryParseSelection(
                callbackData,
                MenuKeyboard.ServingsPrefix,
                MealPlanLimits.MinServings,
                MealPlanLimits.MaxServings,
                out var servings))
        {
            await SendExpiredMenuDialogAsync(telegramClient, chatId, cancellationToken);
            return;
        }

        dialogState.Servings = servings;
        await CompleteMealPlanCreationAsync(
            telegramClient,
            callbackQuery,
            chatId,
            dialogState,
            cancellationToken);
    }

    private async Task CompleteMealPlanCreationAsync(
        ITelegramBotClient telegramClient,
        CallbackQuery callbackQuery,
        long chatId,
        MenuPlanningDialogState dialogState,
        CancellationToken cancellationToken)
    {
        if (dialogState.DaysCount is not { } daysCount
            || dialogState.Servings is not { } servings
            || dialogState.SelectedMealTypes.Count == 0)
        {
            await SendExpiredMenuDialogAsync(telegramClient, chatId, cancellationToken);
            return;
        }

        var telegramUser = callbackQuery.From!;
        var mealPlan = await ExecuteMealPlanServiceAsync(
            service => service.CreateAsync(
                new CreateMealPlanRequest(
                    TelegramUserId: telegramUser.Id,
                    StartDate: DateOnly.FromDateTime(DateTime.UtcNow),
                    DaysCount: daysCount,
                    MealTypes: dialogState.SelectedMealTypes.ToArray(),
                    Servings: servings),
                cancellationToken));

        _menuDialogs.TryRemove(telegramUser.Id, out _);

        await SendMainMenuMessageAsync(
            telegramClient,
            chatId,
            BuildMealPlanCreatedMessage(mealPlan),
            cancellationToken);
    }

    private static Task SendExpiredMenuDialogAsync(
        ITelegramBotClient telegramClient,
        long chatId,
        CancellationToken cancellationToken) =>
        SendMessageAsync(
            telegramClient,
            chatId,
            MenuDialogExpiredMessage,
            cancellationToken);

    private static bool TryParseSelection(
        string callbackData,
        string callbackPrefix,
        int minimumValue,
        int maximumValue,
        out int value)
    {
        if (!callbackData.StartsWith(callbackPrefix, StringComparison.Ordinal))
        {
            value = default;
            return false;
        }

        return int.TryParse(
            callbackData[callbackPrefix.Length..],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out value)
               && value >= minimumValue
               && value <= maximumValue;
    }

    private static Task SendMenuDialogPromptAsync(
        ITelegramBotClient telegramClient,
        long chatId,
        MenuPlanningDialogState dialogState,
        string? prefix,
        CancellationToken cancellationToken)
    {
        var (prompt, keyboard) = dialogState.Mode switch
        {
            MenuPlanningDialogMode.SelectDays =>
                (SelectDaysPrompt, (ReplyMarkup?)MenuKeyboard.CreateDaysSelection()),
            MenuPlanningDialogMode.SelectMeals =>
                (SelectMealsPrompt, (ReplyMarkup?)MenuKeyboard.CreateMealSelection(dialogState.SelectedMealTypes)),
            MenuPlanningDialogMode.SelectServings =>
                (SelectServingsPrompt, (ReplyMarkup?)MenuKeyboard.CreateServingsSelection()),
            _ => throw new ArgumentOutOfRangeException(nameof(dialogState.Mode), dialogState.Mode, null)
        };

        var response = string.IsNullOrWhiteSpace(prefix)
            ? prompt
            : $"{prefix}\n\n{prompt}";

        return SendMessageAsync(telegramClient, chatId, response, cancellationToken, keyboard);
    }

    private static string BuildMealPlanCreatedMessage(MealPlanDto mealPlan)
    {
        var endDate = mealPlan.StartDate.AddDays(mealPlan.DaysCount - 1);
        var mealNames = string.Join(", ", mealPlan.MealTypes.Select(FormatMealType));

        return $"Настройки меню сохранены!\n\n" +
               $"Период: {mealPlan.StartDate:dd.MM.yyyy} — {endDate:dd.MM.yyyy} " +
               $"({mealPlan.DaysCount} дн.)\n" +
               $"Приёмы пищи: {mealNames}\n" +
               $"Порций на блюдо: {mealPlan.Servings}\n" +
               "Блюда будут составлены из добавленных продуктов; соль, перец и базовые " +
               "приправы не учитываются.\n\n" +
               $"Запланировано блюд: {mealPlan.PlannedMeals.Count}";
    }

    private static string FormatMealType(MealType mealType) => mealType switch
    {
        MealType.Breakfast => "завтрак",
        MealType.Lunch => "обед",
        MealType.Dinner => "ужин",
        _ => mealType.ToString()
    };
}
