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

        if (!_menuDialogs.TryGetValue(telegramUserId, out var state))
        {
            await SendMessageAsync(
                telegramClient,
                chatId,
                MenuDialogExpiredMessage,
                cancellationToken);
            return;
        }

        if (callbackData.StartsWith(MenuKeyboard.DaysPrefix, StringComparison.Ordinal))
        {
            if (state.Mode != MenuPlanningDialogMode.SelectDays
                || !int.TryParse(
                    callbackData[MenuKeyboard.DaysPrefix.Length..],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var daysCount)
                || daysCount is < MealPlanLimits.MinDays or > MealPlanLimits.MaxDays)
            {
                await SendMessageAsync(telegramClient, chatId, MenuDialogExpiredMessage, cancellationToken);
                return;
            }

            state.DaysCount = daysCount;
            state.Mode = MenuPlanningDialogMode.SelectMeals;
            await SendMenuDialogPromptAsync(
                telegramClient,
                chatId,
                state,
                null,
                cancellationToken);
            return;
        }

        if (callbackData.StartsWith(MenuKeyboard.MealPrefix, StringComparison.Ordinal))
        {
            if (state.Mode != MenuPlanningDialogMode.SelectMeals
                || !MenuKeyboard.TryParseMealType(
                    callbackData[MenuKeyboard.MealPrefix.Length..],
                    out var mealType))
            {
                await SendMessageAsync(telegramClient, chatId, MenuDialogExpiredMessage, cancellationToken);
                return;
            }

            if (!state.MealTypes.Add(mealType))
            {
                state.MealTypes.Remove(mealType);
            }

            await SendMenuDialogPromptAsync(
                telegramClient,
                chatId,
                state,
                null,
                cancellationToken);
            return;
        }

        if (callbackData.Equals(MenuKeyboard.MealsConfirmCallback, StringComparison.Ordinal))
        {
            if (state.Mode != MenuPlanningDialogMode.SelectMeals)
            {
                await SendMessageAsync(telegramClient, chatId, MenuDialogExpiredMessage, cancellationToken);
                return;
            }

            if (state.MealTypes.Count == 0)
            {
                await SendMenuDialogPromptAsync(
                    telegramClient,
                    chatId,
                    state,
                    "Выберите хотя бы один приём пищи",
                    cancellationToken);
                return;
            }

            state.Mode = MenuPlanningDialogMode.SelectServings;
            await SendMenuDialogPromptAsync(
                telegramClient,
                chatId,
                state,
                null,
                cancellationToken);
            return;
        }

        if (callbackData.StartsWith(MenuKeyboard.ServingsPrefix, StringComparison.Ordinal))
        {
            if (state.Mode != MenuPlanningDialogMode.SelectServings
                || !int.TryParse(
                    callbackData[MenuKeyboard.ServingsPrefix.Length..],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var servings)
                || servings is < MealPlanLimits.MinServings or > MealPlanLimits.MaxServings)
            {
                await SendMessageAsync(telegramClient, chatId, MenuDialogExpiredMessage, cancellationToken);
                return;
            }

            state.Servings = servings;
            await CompleteMealPlanCreationAsync(
                telegramClient,
                callbackQuery,
                chatId,
                state,
                cancellationToken);
            return;
        }
    }

    private async Task CompleteMealPlanCreationAsync(
        ITelegramBotClient telegramClient,
        CallbackQuery callbackQuery,
        long chatId,
        MenuPlanningDialogState state,
        CancellationToken cancellationToken)
    {
        var telegramUser = callbackQuery.From!;
        var mealPlan = await ExecuteWithMealPlanServiceAsync(
            service => service.CreateAsync(
                new CreateMealPlanRequest(
                    telegramUser.Id,
                    DateOnly.FromDateTime(DateTime.UtcNow),
                    state.DaysCount!.Value,
                    state.MealTypes.ToArray(),
                    state.Servings!.Value),
                cancellationToken));

        _menuDialogs.TryRemove(telegramUser.Id, out _);

        await SendMainMenuMessageAsync(
            telegramClient,
            chatId,
            BuildMealPlanCreatedMessage(mealPlan),
            cancellationToken);
    }

    private static Task SendMenuDialogPromptAsync(
        ITelegramBotClient telegramClient,
        long chatId,
        MenuPlanningDialogState state,
        string? prefix,
        CancellationToken cancellationToken)
    {
        var (prompt, keyboard) = state.Mode switch
        {
            MenuPlanningDialogMode.SelectDays =>
                (SelectDaysPrompt, (ReplyMarkup?)MenuKeyboard.CreateDaysSelection()),
            MenuPlanningDialogMode.SelectMeals =>
                (SelectMealsPrompt, (ReplyMarkup?)MenuKeyboard.CreateMealSelection(state.MealTypes)),
            MenuPlanningDialogMode.SelectServings =>
                (SelectServingsPrompt, (ReplyMarkup?)MenuKeyboard.CreateServingsSelection()),
            _ => throw new ArgumentOutOfRangeException()
        };

        var response = string.IsNullOrWhiteSpace(prefix)
            ? prompt
            : $"{prefix}\n\n{prompt}";

        return SendMessageAsync(telegramClient, chatId, response, cancellationToken, keyboard);
    }

    private static string BuildMealPlanCreatedMessage(MealPlanDto mealPlan)
    {
        var endDate = mealPlan.StartDate.AddDays(mealPlan.DaysCount - 1);
        var meals = string.Join(", ", mealPlan.MealTypes.Select(FormatMealType));

        return $"Настройки меню сохранены!\n\n" +
               $"Период: {mealPlan.StartDate:dd.MM.yyyy} — {endDate:dd.MM.yyyy} ({mealPlan.DaysCount} дн.)\n" +
               $"Приёмы пищи: {meals}\n" +
               $"Порций на блюдо: {mealPlan.Servings}\n" +
               "Блюда будут составлены из добавленных продуктов; соль, перец и базовые приправы не учитываются.\n\n" +
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




