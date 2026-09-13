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
        await ExecuteProductServiceAsync(
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

    private async Task<TResult> ExecuteProductServiceAsync<TResult>(
        Func<IProductService, Task<TResult>> operation)
    {
        return await ExecuteScopedOperationAsync(operation);
    }

    private async Task ExecuteProductServiceAsync(Func<IProductService, Task> operation)
    {
        await ExecuteScopedOperationAsync<IProductService, bool>(async productService =>
        {
            await operation(productService);
            return true;
        });
    }

    private Task<TResult> ExecuteMealPlanServiceAsync<TResult>(
        Func<IMealPlanService, Task<TResult>> operation)
        => ExecuteScopedOperationAsync(operation);

    private async Task<TResult> ExecuteScopedOperationAsync<TService, TResult>(
        Func<TService, Task<TResult>> operation)
        where TService : notnull
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<TService>();

        return await operation(service);
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



