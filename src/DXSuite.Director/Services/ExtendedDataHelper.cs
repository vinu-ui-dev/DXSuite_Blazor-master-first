using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DXSuite.Director.Services;

/// <summary>
/// Helper class for working with ExtendedData JSON columns
/// </summary>
public static class ExtendedDataHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>
    /// Parse JSON string to typed object
    /// </summary>
    public static T? Parse<T>(string? json) where T : class
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Serialize object to JSON string
    /// </summary>
    public static string Serialize<T>(T data) where T : class
    {
        return JsonSerializer.Serialize(data, JsonOptions);
    }

    /// <summary>
    /// Update a property within existing JSON, preserving other data.
    /// If JSON is null/empty, creates new object.
    /// </summary>
    public static string UpdateProperty<T>(string? existingJson, Action<T> updateAction) where T : class, new()
    {
        var data = Parse<T>(existingJson) ?? new T();
        updateAction(data);
        return Serialize(data);
    }

    /// <summary>
    /// Get a nested property from ExtendedData, returning default if not found
    /// </summary>
    public static TValue? GetProperty<T, TValue>(string? json, Func<T, TValue?> selector) where T : class
    {
        var data = Parse<T>(json);
        if (data == null)
            return default;

        return selector(data);
    }
}
