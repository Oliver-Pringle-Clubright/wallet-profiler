using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProfilerApi.Models;

/// <summary>
/// Etherscan V2 response wrapper. The "result" field can be either
/// a JSON array (success) or a plain string (error message).
/// </summary>
public class EtherscanResponse<T>
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("result")]
    [JsonConverter(typeof(ResultConverterFactory))]
    public List<T>? Result { get; set; }

    public bool IsSuccess => Status == "1";
}

/// <summary>
/// Handles the polymorphic "result" field: array → deserialize, string → return null.
/// </summary>
public class ResultConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(List<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var itemType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(ResultConverter<>).MakeGenericType(itemType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private class ResultConverter<TItem> : JsonConverter<List<TItem>?>
    {
        public override List<TItem>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                // Error response — "result" is a string like "Max rate limit reached"
                reader.GetString();
                return null;
            }

            if (reader.TokenType == JsonTokenType.StartArray)
            {
                return JsonSerializer.Deserialize<List<TItem>>(ref reader, options);
            }

            // Skip unexpected token types
            reader.Skip();
            return null;
        }

        public override void Write(Utf8JsonWriter writer, List<TItem>? value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, options);
        }
    }
}
