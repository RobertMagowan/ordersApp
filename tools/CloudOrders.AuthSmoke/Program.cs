using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Identity.Client;

var arguments = SmokeArguments.Parse(args);
var application = PublicClientApplicationBuilder
    .Create(arguments.ClientId.ToString("D"))
    .WithAuthority(arguments.Authority)
    .WithRedirectUri("http://localhost")
    .Build();

AuthenticationResult authentication;
try
{
    authentication = await application.AcquireTokenInteractive(arguments.Scopes)
        .WithUseEmbeddedWebView(false)
        .ExecuteAsync();
}
catch (MsalException)
{
    Console.Error.WriteLine("Interactive sign-in did not complete.");
    return 1;
}

using var client = new HttpClient { BaseAddress = arguments.ApiBaseUri };
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authentication.AccessToken);
using var response = await client.GetAsync("/api/v1/me");
var responseBody = await response.Content.ReadAsStringAsync();
var (errorCode, traceId) = SafeProblemFields.Read(responseBody);

Console.WriteLine($"Status: {(int)response.StatusCode} ({response.StatusCode})");
Console.WriteLine($"errorCode: {errorCode ?? "<none>"}");
Console.WriteLine($"traceId: {traceId ?? "<none>"}");
if (response.IsSuccessStatusCode)
{
    Console.WriteLine("/api/v1/me:");
    Console.WriteLine(responseBody);
}

return response.IsSuccessStatusCode ? 0 : 1;

internal sealed record SmokeArguments(Uri Authority, Guid ClientId, IReadOnlyList<string> Scopes, Uri ApiBaseUri)
{
    public static SmokeArguments Parse(string[] arguments)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Length; index += 2)
        {
            if (index + 1 >= arguments.Length || !arguments[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("Usage: --authority <https-url> --client-id <guid> --scope <api-scope> [--scope <api-scope>] --api-base-url <https-url>.");
            }

            values.TryAdd(arguments[index], []);
            values[arguments[index]].Add(arguments[index + 1]);
        }

        var authority = RequiredUri(values, "--authority");
        var apiBaseUri = RequiredUri(values, "--api-base-url");
        var clientId = Guid.TryParse(RequiredSingle(values, "--client-id"), out var parsedClientId)
            ? parsedClientId
            : throw new ArgumentException("--client-id must be a GUID.");
        var scopes = values.GetValueOrDefault("--scope") ?? throw new ArgumentException("At least one --scope is required.");
        if (scopes.Count == 0 || scopes.Any(scope => !scope.StartsWith("api://", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Each --scope must be a fully qualified api:// scope.");
        }

        return new SmokeArguments(authority, clientId, scopes, apiBaseUri);
    }

    private static Uri RequiredUri(IReadOnlyDictionary<string, List<string>> values, string name)
    {
        var value = RequiredSingle(values, name);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException($"{name} must be an HTTPS URL.");
        }

        return uri;
    }

    private static string RequiredSingle(IReadOnlyDictionary<string, List<string>> values, string name) =>
        values.TryGetValue(name, out var valuesForName) && valuesForName.Count == 1
            ? valuesForName[0]
            : throw new ArgumentException($"{name} must be specified exactly once.");
}

internal static class SafeProblemFields
{
    public static (string? ErrorCode, string? TraceId) Read(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            return (ReadString(root, "errorCode"), ReadString(root, "traceId"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? ReadString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
