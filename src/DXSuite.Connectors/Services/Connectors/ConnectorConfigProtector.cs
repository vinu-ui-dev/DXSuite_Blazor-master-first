using System.Text.Json;
using DXSuite.Shared.Services;
using Microsoft.AspNetCore.DataProtection;

namespace DXSuite.Connectors.Services.Connectors;

public class ConnectorConfigProtector
{
    private readonly IDataProtector _protector;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ConnectorConfigProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("ConnectorConfig");
    }

    public string Encrypt(PlatformConnectionConfig config)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        return _protector.Protect(json);
    }

    public PlatformConnectionConfig? Decrypt(string? configJson)
    {
        if (string.IsNullOrEmpty(configJson)) return null;

        try
        {
            if (configJson.TrimStart().StartsWith('{'))
                return JsonSerializer.Deserialize<PlatformConnectionConfig>(configJson, JsonOptions);

            var json = _protector.Unprotect(configJson);
            return JsonSerializer.Deserialize<PlatformConnectionConfig>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
