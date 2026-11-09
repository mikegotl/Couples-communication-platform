using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Couples.AI;

/// <summary>
/// Configurable live adapter for a model gateway the owner operates. It posts a
/// provider-neutral request (instructions, data, JSON schema, limits) and expects the gateway
/// to return the model's text and token usage. Choosing the model vendor behind the gateway,
/// and its data processing terms, is an owner decision; a vendor-specific adapter can replace
/// this class without touching the coach.
/// </summary>
internal sealed class GatewayCoachModelClient(HttpClient http, IOptions<CoachingOptions> options) : ICoachModelClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public string Name => "gateway";

    public async Task<ModelResponse> CompleteAsync(ModelPrompt prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var gateway = options.Value.Gateway;
        if (!gateway.IsConfigured)
        {
            throw new InvalidOperationException("The coaching gateway is not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, gateway.Endpoint)
        {
            Content = JsonContent.Create(
                new GatewayRequest(gateway.Model!, prompt.PromptVersion, prompt.System, prompt.User, prompt.JsonSchema, prompt.MaxOutputTokens),
                options: Json),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", gateway.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new CoachTransientException("Gateway connection failed.", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout)
            {
                throw new CoachTransientException($"Gateway returned {(int)response.StatusCode}.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Gateway returned {(int)response.StatusCode}.");
            }

            GatewayResponse? body;
            try
            {
                body = await response.Content.ReadFromJsonAsync<GatewayResponse>(Json, cancellationToken);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Gateway response was not valid JSON.", ex);
            }

            return body is null
                ? throw new InvalidOperationException("Gateway response was empty.")
                : new ModelResponse(body.Output ?? string.Empty, body.Usage?.InputTokens ?? 0, body.Usage?.OutputTokens ?? 0, body.Refused);
        }
    }

    private sealed record GatewayRequest(string Model, string PromptVersion, string System, string Input, [property: JsonPropertyName("response_schema")] string ResponseSchema, int MaxOutputTokens);

    private sealed record GatewayResponse(string? Output, GatewayUsage? Usage, bool Refused);

    private sealed record GatewayUsage(int InputTokens, int OutputTokens);
}
