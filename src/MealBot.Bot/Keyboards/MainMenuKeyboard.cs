using Telegram.Bot.Types.ReplyMarkups;

namespace MealBot.Bot.Keyboards;

public static class MainMenuKeyboard
{
    public const string MyProductsText = "Мои продукты";
    public const string AddProductText = "Добавить продукт";
    public const string CreateMenuText = "Составить меню";
    public const string MyMenuText = "Моё меню";

    public static ReplyKeyboardMarkup Create() =>
        new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton[]
            {
                MyProductsText,
                AddProductText
            },
            new KeyboardButton[]
            {
                CreateMenuText,
                MyMenuText
            }
        })
        {
            IsPersistent = true,
            ResizeKeyboard = true,
            InputFieldPlaceholder = "Выберите действие"
        };
}
