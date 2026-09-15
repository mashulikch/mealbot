using System.Text.Json;

namespace MealBot.Infrastructure.Recipes;

/// <summary>
/// The JSON Schema the recipe-generation model must follow (OpenAI structured outputs).
/// Kept separate from <see cref="OpenAiRecipeGenerator"/> so the schema definition can be
/// read and versioned independently of request/response handling.
/// </summary>
internal static class RecipeSchemaProvider
{
    public static readonly JsonElement Schema = Create();

    private static JsonElement Create()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "type": "object",
              "additionalProperties": false,
              "required": ["name", "mealType", "servings", "cookingTimeMinutes", "ingredients", "steps"],
              "properties": {
                "name": { "type": "string" },
                "mealType": { "type": "string", "enum": ["Breakfast", "Lunch", "Dinner"] },
                "servings": { "type": "integer", "minimum": 1 },
                "cookingTimeMinutes": { "type": "integer", "minimum": 1 },
                "ingredients": {
                  "type": "array",
                  "minItems": 1,
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["productName", "quantity", "unit", "isOptional"],
                    "properties": {
                      "productName": { "type": "string" },
                      "quantity": { "type": "number", "exclusiveMinimum": 0 },
                      "unit": { "type": "string", "enum": ["Gram", "Milliliter", "Piece"] },
                      "isOptional": { "type": "boolean" }
                    }
                  }
                },
                "steps": {
                  "type": "array",
                  "minItems": 1,
                  "items": { "type": "string" }
                }
              }
            }
            """);

        return document.RootElement.Clone();
    }
}
