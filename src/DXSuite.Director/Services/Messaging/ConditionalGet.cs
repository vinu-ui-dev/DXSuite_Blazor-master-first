using System.Security.Cryptography;

namespace DXSuite.Director.Services.Messaging;

/// <summary>Computes the ETag a conditional-GET route (the delivery document, INK-05) compares <c>If-None-Match</c>
/// against: a hash of the response body, so two servers serving the same bytes always agree on the tag.</summary>
public static class ConditionalGet
{
    public static string ETagOf(ReadOnlySpan<byte> content) => Convert.ToHexStringLower(SHA256.HashData(content));
}
