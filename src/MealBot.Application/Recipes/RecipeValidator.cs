using System.Globalization;
using MealBot.Domain;

namespace MealBot.Application.Recipes;

public sealed class RecipeValidator : IRecipeValidator
{
    private static class ErrorCodes
    {
        public const string EmptyName = "empty_name";
        public const string InvalidMealType = "invalid_meal_type";
        public const string MealTypeMismatch = "meal_type_mismatch";
        public const string InvalidServings = "invalid_servings";
        public const string ServingsMismatch = "servings_mismatch";
        public const string InvalidCookingTime = "invalid_cooking_time";
        public const string CookingTimeExceeded = "cooking_time_exceeded";
        public const string EmptyIngredients = "empty_ingredients";
        public const string InvalidRequestedMealType = "invalid_requested_meal_type";
        public const string InvalidRequestedServings = "invalid_requested_servings";
        public const string DuplicateRecipe = "duplicate_recipe";
        public const string EmptySteps = "empty_steps";
        public const string InvalidStep = "invalid_step";
        public const string InvalidCatalogProduct = "invalid_catalog_product";
        public const string AmbiguousProductAlias = "ambiguous_product_alias";
        public const string InvalidAvailableProduct = "invalid_available_product";
        public const string AvailableProductNotFound = "available_product_not_found";
        public const string InvalidIngredient = "invalid_ingredient";
        public const string InvalidIngredientName = "invalid_ingredient_name";
        public const string InvalidIngredientQuantity = "invalid_ingredient_quantity";
        public const string UnsupportedIngredientUnit = "unsupported_ingredient_unit";
        public const string UnknownIngredient = "unknown_ingredient";
        public const string IngredientUnitConflict = "ingredient_unit_conflict";
        public const string InsufficientInventory = "insufficient_inventory";
    }

    public RecipeValidationResult Validate(
        GeneratedRecipeDto recipe,
        RecipeGenerationRequest request,
        IReadOnlyCollection<ProductCatalogItem> productCatalog)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(productCatalog);

        var errors = new List<RecipeValidationError>();

        ValidateHeader(recipe, request, errors);
        ValidateNameUniqueness(recipe, request, errors);
        ValidateSteps(recipe, errors);

        var catalogLookup = BuildCatalogLookup(productCatalog, errors);
        var availableProducts = BuildAvailableProductsLookup(
            request.AvailableProducts ?? Array.Empty<AvailableProduct>(),
            catalogLookup,
            errors);

        var ingredients = ValidateIngredients(
            recipe.Ingredients,
            request,
            catalogLookup,
            availableProducts,
            errors);

        if (errors.Count > 0)
        {
            return RecipeValidationResult.Invalid(errors);
        }

        return RecipeValidationResult.Valid(
            new ValidatedRecipeDto(
                recipe.Name.Trim(),
                recipe.MealType,
                recipe.Servings,
                recipe.CookingTimeMinutes,
                ingredients,
                recipe.Steps
                    .Select(step => step.Trim())
                    .ToArray()));
    }

    private static void ValidateHeader(
        GeneratedRecipeDto recipe,
        RecipeGenerationRequest request,
        ICollection<RecipeValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(recipe.Name))
        {
            AddError(errors, ErrorCodes.EmptyName, "У рецепта отсутствует название");
        }

        if (!Enum.IsDefined(recipe.MealType))
        {
            AddError(errors, ErrorCodes.InvalidMealType, "У рецепта указан неизвестный приём пищи");
        }
        else if (recipe.MealType != request.MealType)
        {
            AddError(
                errors,
                ErrorCodes.MealTypeMismatch,
                $"Ожидался приём пищи {request.MealType}, а рецепт предназначен для {recipe.MealType}");
        }

        if (recipe.Servings <= 0)
        {
            AddError(errors, ErrorCodes.InvalidServings, "Количество порций должно быть больше нуля");
        }
        else if (recipe.Servings != request.Servings)
        {
            AddError(
                errors,
                ErrorCodes.ServingsMismatch,
                $"Ожидалось {request.Servings} порций, в рецепте указано {recipe.Servings}");
        }

        if (recipe.CookingTimeMinutes <= 0)
        {
            AddError(errors, ErrorCodes.InvalidCookingTime, "Время приготовления должно быть больше нуля");
        }
        else if (recipe.CookingTimeMinutes > request.MaxCookingTimeMinutes)
        {
            AddError(
                errors,
                ErrorCodes.CookingTimeExceeded,
                $"Время приготовления {recipe.CookingTimeMinutes} минут превышает максимум " +
                $"{request.MaxCookingTimeMinutes} минут");
        }

        if (recipe.Ingredients is null || recipe.Ingredients.Count == 0)
        {
            AddError(errors, ErrorCodes.EmptyIngredients, "В рецепте должен быть хотя бы один ингредиент");
        }

        if (!Enum.IsDefined(request.MealType))
        {
            AddError(errors, ErrorCodes.InvalidRequestedMealType, "В запросе указан неизвестный приём пищи");
        }

        if (request.Servings <= 0)
        {
            AddError(errors, ErrorCodes.InvalidRequestedServings, "В запросе указано некорректное количество порций");
        }
    }

    private static void ValidateNameUniqueness(
        GeneratedRecipeDto recipe,
        RecipeGenerationRequest request,
        ICollection<RecipeValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(recipe.Name))
        {
            return;
        }

        var normalizedRecipeName = NormalizeForComparison(recipe.Name);
        if ((request.ExcludedRecipeNames ?? Array.Empty<string>()).Any(name =>
                string.Equals(
                    NormalizeForComparison(name),
                    normalizedRecipeName,
                    StringComparison.Ordinal)))
        {
            AddError(
                errors,
                ErrorCodes.DuplicateRecipe,
                $"Блюдо с названием «{recipe.Name.Trim()}» уже есть в меню");
        }
    }

    private static void ValidateSteps(
        GeneratedRecipeDto recipe,
        ICollection<RecipeValidationError> errors)
    {
        if (recipe.Steps is null || recipe.Steps.Count == 0)
        {
            AddError(errors, ErrorCodes.EmptySteps, "В рецепте должна быть хотя бы одна инструкция");
            return;
        }

        if (recipe.Steps.Any(string.IsNullOrWhiteSpace))
        {
            AddError(errors, ErrorCodes.InvalidStep, "Шаг инструкции не должен быть пустым");
        }
    }

    private static Dictionary<string, ProductCatalogItem> BuildCatalogLookup(
        IReadOnlyCollection<ProductCatalogItem> productCatalog,
        ICollection<RecipeValidationError> errors)
    {
        var lookup = new Dictionary<string, ProductCatalogItem>(StringComparer.Ordinal);

        foreach (var product in productCatalog)
        {
            if (product is null
                || product.ProductId == Guid.Empty
                || string.IsNullOrWhiteSpace(product.Name))
            {
                AddError(errors, ErrorCodes.InvalidCatalogProduct, "В справочнике найден некорректный продукт");
                continue;
            }

            AddCatalogName(lookup, product.Name, product, errors);

            foreach (var alias in product.Aliases ?? Array.Empty<string>())
            {
                AddCatalogName(lookup, alias, product, errors);
            }
        }

        return lookup;
    }

    private static void AddCatalogName(
        IDictionary<string, ProductCatalogItem> lookup,
        string? name,
        ProductCatalogItem product,
        ICollection<RecipeValidationError> errors)
    {
        var normalizedName = TryNormalize(name);
        if (normalizedName is null)
        {
            AddError(errors, ErrorCodes.InvalidCatalogProduct, "В справочнике найдено некорректное название продукта");
            return;
        }

        if (lookup.TryGetValue(normalizedName, out var existing)
            && existing.ProductId != product.ProductId)
        {
            AddError(
                errors,
                ErrorCodes.AmbiguousProductAlias,
                $"Название продукта «{name?.Trim() ?? "<пустое>"}» связано с несколькими продуктами");
            return;
        }

        lookup[normalizedName] = product;
    }

    private static Dictionary<ProductKey, AvailableProductBalance> BuildAvailableProductsLookup(
        IReadOnlyCollection<AvailableProduct> availableProducts,
        IReadOnlyDictionary<string, ProductCatalogItem> catalogLookup,
        ICollection<RecipeValidationError> errors)
    {
        var lookup = new Dictionary<ProductKey, AvailableProductBalance>();

        foreach (var availableProduct in availableProducts)
        {
            if (availableProduct is null)
            {
                AddError(errors, ErrorCodes.InvalidAvailableProduct, "В списке запасов найден некорректный продукт");
                continue;
            }

            var normalizedName = TryNormalize(availableProduct.Name);
            if (normalizedName is null
                || availableProduct.Quantity <= 0
                || !Enum.IsDefined(availableProduct.Unit))
            {
                AddError(
                    errors,
                    ErrorCodes.InvalidAvailableProduct,
                    $"Запас «{availableProduct.Name}» имеет некорректное название, количество или единицу измерения");
                continue;
            }

            if (!catalogLookup.TryGetValue(normalizedName, out var catalogProduct))
            {
                AddError(
                    errors,
                    ErrorCodes.AvailableProductNotFound,
                    $"Продукт «{availableProduct.Name.Trim()}» отсутствует в справочнике");
                continue;
            }

            var key = new ProductKey(catalogProduct.ProductId, availableProduct.Unit);
            if (!lookup.TryGetValue(key, out var balance))
            {
                lookup[key] = new AvailableProductBalance(
                    availableProduct.Quantity);
            }
            else
            {
                lookup[key] = balance with
                {
                    Quantity = balance.Quantity + availableProduct.Quantity
                };
            }
        }

        return lookup;
    }

    private static IReadOnlyList<ValidatedRecipeIngredientDto> ValidateIngredients(
        IReadOnlyList<RecipeIngredientDto>? ingredients,
        RecipeGenerationRequest request,
        IReadOnlyDictionary<string, ProductCatalogItem> catalogLookup,
        IReadOnlyDictionary<ProductKey, AvailableProductBalance> availableProducts,
        ICollection<RecipeValidationError> errors)
    {
        if (ingredients is null)
        {
            return Array.Empty<ValidatedRecipeIngredientDto>();
        }

        var normalizedIngredients = new Dictionary<ProductKey, IngredientAccumulator>();

        foreach (var ingredient in ingredients)
        {
            if (ingredient is null)
            {
                AddError(errors, ErrorCodes.InvalidIngredient, "В рецепте найден пустой ингредиент");
                continue;
            }

            var normalizedName = TryNormalize(ingredient.ProductName);
            if (normalizedName is null)
            {
                AddError(
                    errors,
                    ErrorCodes.InvalidIngredientName,
                    "Название ингредиента не должно быть пустым и должно иметь допустимую длину");
                continue;
            }

            if (ingredient.Quantity <= 0)
            {
                AddError(
                    errors,
                    ErrorCodes.InvalidIngredientQuantity,
                    $"Количество ингредиента «{ingredient.ProductName.Trim()}» должно быть больше нуля");
                continue;
            }

            if (!Enum.IsDefined(ingredient.Unit))
            {
                AddError(
                    errors,
                    ErrorCodes.UnsupportedIngredientUnit,
                    $"Для ингредиента «{ingredient.ProductName.Trim()}» указана неподдерживаемая единица измерения");
                continue;
            }

            if (!catalogLookup.TryGetValue(normalizedName, out var catalogProduct))
            {
                AddError(
                    errors,
                    ErrorCodes.UnknownIngredient,
                    $"Продукт «{ingredient.ProductName.Trim()}» отсутствует в справочнике");
                continue;
            }

            var key = new ProductKey(catalogProduct.ProductId, ingredient.Unit);
            if (normalizedIngredients.TryGetValue(key, out var existingIngredient))
            {
                normalizedIngredients[key] = existingIngredient with
                {
                    Quantity = existingIngredient.Quantity + ingredient.Quantity,
                    IsOptional = existingIngredient.IsOptional && ingredient.IsOptional
                };
            }
            else
            {
                normalizedIngredients[key] = new IngredientAccumulator(
                    catalogProduct,
                    ingredient.Quantity,
                    ingredient.IsOptional);
            }
        }

        var productIdsWithMultipleUnits = normalizedIngredients.Keys
            .GroupBy(key => key.ProductId)
            .Where(group => group.Select(key => key.Unit).Distinct().Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();

        foreach (var productId in productIdsWithMultipleUnits)
        {
            var productName = normalizedIngredients
                .First(item => item.Key.ProductId == productId)
                .Value.Product.Name;

            AddError(
                errors,
                ErrorCodes.IngredientUnitConflict,
                $"Продукт «{productName}» указан в рецепте в разных единицах измерения");
        }

        var result = new List<ValidatedRecipeIngredientDto>(normalizedIngredients.Count);
        foreach (var (key, ingredient) in normalizedIngredients)
        {
            availableProducts.TryGetValue(key, out var balance);
            var availableQuantity = balance?.Quantity ?? 0m;
            var missingQuantity = ingredient.IsOptional
                ? 0m
                : Math.Max(0m, ingredient.Quantity - availableQuantity);

            if (missingQuantity > 0 && !request.AllowMissingIngredients)
            {
                AddError(
                    errors,
                    ErrorCodes.InsufficientInventory,
                    $"Для «{ingredient.Product.Name}» требуется {FormatQuantity(ingredient.Quantity)} " +
                    $"{FormatUnit(key.Unit)}, доступно только {FormatQuantity(availableQuantity)} " +
                    $"{FormatUnit(key.Unit)}");
            }

            result.Add(
                new ValidatedRecipeIngredientDto(
                    ingredient.Product.ProductId,
                    ingredient.Product.Name,
                    ingredient.Quantity,
                    key.Unit,
                    ingredient.IsOptional,
                    availableQuantity,
                    missingQuantity));
        }

        return result
            .OrderBy(ingredient => ingredient.ProductName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(ingredient => ingredient.Unit)
            .ToArray();
    }

    private static string? TryNormalize(string? name) =>
        ProductName.TryNormalize(name, out var normalizedName)
            ? normalizedName
            : null;

    private static string NormalizeForComparison(string value) =>
        TryNormalize(value) ?? value.Trim().ToUpperInvariant();

    private static string FormatQuantity(decimal quantity) =>
        quantity.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatUnit(MeasurementUnit unit) => unit switch
    {
        MeasurementUnit.Gram => "г",
        MeasurementUnit.Milliliter => "мл",
        MeasurementUnit.Piece => "шт",
        _ => unit.ToString()
    };

    private static void AddError(
        ICollection<RecipeValidationError> errors,
        string code,
        string message) =>
        errors.Add(new RecipeValidationError(code, message));

    private readonly record struct ProductKey(Guid ProductId, MeasurementUnit Unit);

    private sealed record AvailableProductBalance(decimal Quantity);

    private sealed record IngredientAccumulator(
        ProductCatalogItem Product,
        decimal Quantity,
        bool IsOptional);
}
