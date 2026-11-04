using System.Text.Json;
using System.Text.Json.Serialization;

namespace Samaat.Core.Settings;

/// <summary>
/// Reads enums by name or number and writes camelCase names. An unknown name (from a newer build, or a
/// typo) reads as an undefined value instead of failing the whole settings file;
/// <see cref="AppSettings.Normalize"/> then puts that setting back to its default.
/// </summary>
public sealed class LenientEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(LenientEnumConverter<>).MakeGenericType(typeToConvert))!;

    private sealed class LenientEnumConverter<T> : JsonConverter<T>
        where T : struct, Enum
    {
        private static readonly T Unknown = (T)Enum.ToObject(typeof(T), int.MinValue);

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    var text = reader.GetString();
                    return !int.TryParse(text, out _) && Enum.TryParse<T>(text, ignoreCase: true, out var named) ? named : Unknown;
                case JsonTokenType.Number when reader.TryGetInt32(out var number):
                    return (T)Enum.ToObject(typeof(T), number);
                default:
                    reader.Skip();
                    return Unknown;
            }
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
    }
}
