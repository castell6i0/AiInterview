using System.Text.Json;

namespace AiInterview.Infrastructure.AI;

/// <summary>Validates the JSON Schema subset used by this application's fixed AI contracts before DTO defaults can hide missing fields.</summary>
internal static class StructuredOutputValidator
{
    public static bool IsValid(string content, object schema)
    {
        using var document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 64 });
        using var contract = JsonDocument.Parse(JsonSerializer.Serialize(schema, GeminiProtocol.JsonOptions), new JsonDocumentOptions { MaxDepth = 64 });
        return Matches(document.RootElement, contract.RootElement, 0);
    }

    private static bool Matches(JsonElement value, JsonElement schema, int depth)
    {
        if (depth > 64) return false;
        if (schema.ValueKind == JsonValueKind.True) return true;
        if (schema.ValueKind != JsonValueKind.Object) return false;
        if (schema.TryGetProperty("anyOf", out var anyOf)
            && !anyOf.EnumerateArray().Any(branch => Matches(value, branch, depth + 1))) return false;
        if (schema.TryGetProperty("oneOf", out var oneOf)
            && oneOf.EnumerateArray().Count(branch => Matches(value, branch, depth + 1)) != 1) return false;
        if (schema.TryGetProperty("type", out var type))
        {
            var matchingType = type.ValueKind == JsonValueKind.Array
                ? type.EnumerateArray().Any(t => HasType(value, t.GetString()))
                : HasType(value, type.GetString());
            if (!matchingType) return false;
        }
        if (schema.TryGetProperty("enum", out var enumeration)
            && !enumeration.EnumerateArray().Any(x => SamePrimitive(value, x))) return false;
        if (value.ValueKind == JsonValueKind.Object)
        {
            var fields = value.EnumerateObject().ToArray();
            if (fields.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != fields.Length) return false;
            if (schema.TryGetProperty("required", out var required)
                && required.EnumerateArray().Any(name => !value.TryGetProperty(name.GetString()!, out _))) return false;
            var hasProperties = schema.TryGetProperty("properties", out var properties);
            foreach (var field in fields)
            {
                if (hasProperties && properties.TryGetProperty(field.Name, out var propertySchema))
                {
                    if (!Matches(field.Value, propertySchema, depth + 1)) return false;
                }
                else if (schema.TryGetProperty("additionalProperties", out var additional)
                         && !Matches(field.Value, additional, depth + 1)) return false;
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            if (!InBounds(value.GetArrayLength(), schema, "minItems", "maxItems")) return false;
            if (schema.TryGetProperty("items", out var itemSchema)
                && value.EnumerateArray().Any(item => !Matches(item, itemSchema, depth + 1))) return false;
        }
        else if (value.ValueKind == JsonValueKind.Number)
        {
            if (!value.TryGetDecimal(out var number) || !InBounds(number, schema, "minimum", "maximum")) return false;
        }
        else if (value.ValueKind == JsonValueKind.String
                 && !InBounds(value.GetString()!.EnumerateRunes().Count(), schema, "minLength", "maxLength")) return false;
        return true;
    }

    private static bool HasType(JsonElement value, string? type) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "number" => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var integer) && integer == decimal.Truncate(integer),
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => false
    };

    private static bool InBounds(decimal number, JsonElement schema, string lower, string upper)
        => (!schema.TryGetProperty(lower, out var minimum) || minimum.TryGetDecimal(out var min) && number >= min)
        && (!schema.TryGetProperty(upper, out var maximum) || maximum.TryGetDecimal(out var max) && number <= max);

    private static bool SamePrimitive(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind) return false;
        return left.ValueKind switch
        {
            JsonValueKind.String => left.GetString() == right.GetString(),
            JsonValueKind.Number => left.TryGetDecimal(out var l) && right.TryGetDecimal(out var r) && l == r,
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
            _ => false
        };
    }
}
