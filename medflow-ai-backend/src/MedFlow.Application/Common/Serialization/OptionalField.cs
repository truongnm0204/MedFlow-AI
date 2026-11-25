using System.Text.Json;
using System.Text.Json.Serialization;

namespace MedFlow.Application.Common.Serialization;

/// <summary>
/// Distinguishes an omitted JSON property (<see cref="IsSpecified"/> = <c>false</c>)
/// from an explicitly supplied value including <c>null</c> (<see cref="IsSpecified"/> = <c>true</c>).
/// </summary>
[JsonConverter(typeof(OptionalFieldJsonConverterFactory))]
public readonly struct OptionalField<T>
{
    public bool IsSpecified { get; }
    public T? Value { get; }

    private OptionalField(T? value, bool isSpecified)
    {
        Value = value;
        IsSpecified = isSpecified;
    }

    public static OptionalField<T> Unspecified => default;

    public static OptionalField<T> FromValue(T? value) => new(value, isSpecified: true);

    public static implicit operator OptionalField<T>(T? value) => FromValue(value);
}

public sealed class OptionalFieldJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert.IsGenericType &&
           typeToConvert.GetGenericTypeDefinition() == typeof(OptionalField<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(OptionalFieldJsonConverter<>).MakeGenericType(valueType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private sealed class OptionalFieldJsonConverter<T> : JsonConverter<OptionalField<T>>
    {
        public override bool HandleNull => true;

        public override OptionalField<T> Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return OptionalField<T>.FromValue(default);
            }

            var value = JsonSerializer.Deserialize<T>(ref reader, options);
            return OptionalField<T>.FromValue(value);
        }

        public override void Write(
            Utf8JsonWriter writer,
            OptionalField<T> value,
            JsonSerializerOptions options)
        {
            if (!value.IsSpecified || value.Value is null)
            {
                writer.WriteNullValue();
                return;
            }

            JsonSerializer.Serialize(writer, value.Value, options);
        }
    }
}
