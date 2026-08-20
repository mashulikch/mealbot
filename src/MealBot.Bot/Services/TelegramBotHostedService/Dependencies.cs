using MealBot.Application.MealPlans;
using MealBot.Application.Products;
using Telegram.Bot.Types;

namespace MealBot.Bot.Services;

public sealed partial class TelegramBotHostedService
{
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
        if (_productDialogs.TryGetValue(telegramUserId, out var currentState)
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

    private async Task<TResult> ExecuteWithMealPlanServiceAsync<TResult>(
        Func<IMealPlanService, Task<TResult>> operation)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var mealPlanService = scope.ServiceProvider.GetRequiredService<IMealPlanService>();

        return await operation(mealPlanService);
    }

    private void StartAddProductDialog(long telegramUserId)
    {
        _productDialogs[telegramUserId] = new ProductDialogState(ProductDialogMode.AddName);
    }

    private void StartMenuPlanningDialog(long telegramUserId)
    {
        _menuDialogs[telegramUserId] = new MenuPlanningDialogState();
    }


    private void ResetDialogStates(long telegramUserId)
    {
        _productDialogs.TryRemove(telegramUserId, out _);
        _menuDialogs.TryRemove(telegramUserId, out _);
    }
}




