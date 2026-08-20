using System.Collections.Concurrent;
using MealBot.Bot.Options;
using MealBot.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MealBot.Bot.Services;

public sealed partial class TelegramBotHostedService(
    IOptions<TelegramBotOptions> telegramBotOptions,
    IServiceScopeFactory scopeFactory,
    ILogger<TelegramBotHostedService> logger) : BackgroundService
{
    private const string StartCommand = "/start";
    private const string CancelCommand = "/cancel";

    private const string UnavailableMenuMessage =
        "Этот раздел будет добавлен на следующих этапах MVP";

    private const string SelectDaysPrompt =
        "На сколько дней составить меню?";

    private const string SelectMealsPrompt =
        "Какие приёмы пищи включить в меню? Можно выбрать несколько вариантов";

    private const string SelectServingsPrompt =
        "Сколько порций готовить для каждого блюда?";

    private const string MenuDialogExpiredMessage =
        "Настройка меню устарела. Нажмите «Составить меню» ещё раз";

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

    private readonly ConcurrentDictionary<long, ProductDialogState> _productDialogs = new();
    private readonly ConcurrentDictionary<long, MenuPlanningDialogState> _menuDialogs = new();
}


