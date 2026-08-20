using MealBot.Domain;
using Telegram.Bot.Types.ReplyMarkups;

namespace MealBot.Bot.Keyboards;

public static class ProductKeyboard
{
    private const string GramUnitValue = "gram";
    private const string MilliliterUnitValue = "milliliter";
    private const string PieceUnitValue = "piece";

    public const string AddUnitPrefix = "product:add:unit:";
    public const string EditUnitPrefix = "product:eu:";
    public const string EditPrefix = "product:edit:";
    public const string DeletePrefix = "product:delete:";

    public static InlineKeyboardMarkup CreateUnitSelection(string callbackPrefix) =>
        new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                Button("г", callbackPrefix + GramUnitValue),
                Button("мл", callbackPrefix + MilliliterUnitValue),
                Button("шт", callbackPrefix + PieceUnitValue)
            }
        });

    public static InlineKeyboardMarkup CreateInventoryActions(
        IReadOnlyList<(Guid Id, string ProductName)> items)
    {
        var rows = items
            .Select(item => new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    $"{item.ProductName}",
                    EditPrefix + item.Id),
                InlineKeyboardButton.WithCallbackData(
                    "Удалить",
                    DeletePrefix + item.Id)
            })
            .ToArray();

        return new InlineKeyboardMarkup(rows);
    }

    public static string FormatUnit(MeasurementUnit unit) => unit switch
    {
        MeasurementUnit.Gram => "г",
        MeasurementUnit.Milliliter => "мл",
        MeasurementUnit.Piece => "шт",
        _ => unit.ToString()
    };

    public static bool TryParseUnit(string value, out MeasurementUnit unit)
    {
        var normalizedValue = value.Trim().ToLowerInvariant();

        unit = normalizedValue switch
        {
            GramUnitValue => MeasurementUnit.Gram,
            MilliliterUnitValue => MeasurementUnit.Milliliter,
            PieceUnitValue => MeasurementUnit.Piece,
            _ => default
        };

        return unit != default;
    }

    private static InlineKeyboardButton Button(string text, string callbackData) =>
        InlineKeyboardButton.WithCallbackData(text, callbackData);
}
