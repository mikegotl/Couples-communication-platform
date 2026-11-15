using System.Net.Http.Json;
using Couples.Application.Mobile;

namespace Couples.Client;

/// <summary>
/// Calls the server's <c>/api/v1</c> endpoints. Later phases add one client per Application service
/// interface (for example a messaging client implementing <c>IMessagingService</c>) so shared pages
/// work unchanged on the phone; those clients never send a user id, which the server takes from the
/// signed-in token.
/// </summary>
public sealed class BridgeTogetherApiClient(HttpClient http)
{
    /// <summary>
    /// The server's status, or a failure the app can show calmly ("can't reach BridgeTogether right
    /// now") instead of an exception.
    /// </summary>
    public async Task<ApiResult<AppStatus>> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.GetAsync(new Uri("api/v1/app-status", UriKind.Relative), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return ApiResult.Fail<AppStatus>(ApiFailure.ServerError);
            }

            var status = await response.Content.ReadFromJsonAsync<AppStatus>(cancellationToken);
            return status is { Service: "BridgeTogether" }
                ? ApiResult.Ok(status)
                : ApiResult.Fail<AppStatus>(ApiFailure.UnexpectedServer);
        }
        catch (HttpRequestException)
        {
            return ApiResult.Fail<AppStatus>(ApiFailure.Unreachable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ApiResult.Fail<AppStatus>(ApiFailure.Unreachable);
        }
        catch (System.Text.Json.JsonException)
        {
            return ApiResult.Fail<AppStatus>(ApiFailure.UnexpectedServer);
        }
    }
}

/// <summary>Why an API call didn't produce a value.</summary>
public enum ApiFailure
{
    None,

    /// <summary>No network, DNS failure, or timeout.</summary>
    Unreachable,

    /// <summary>The server answered with an error status.</summary>
    ServerError,

    /// <summary>Something answered, but not BridgeTogether (captive portal, wrong address).</summary>
    UnexpectedServer,
}

/// <summary>A value or the reason there isn't one.</summary>
public sealed record ApiResult<T>(T? Value, ApiFailure Failure)
{
    public bool Succeeded => Failure == ApiFailure.None;
}

/// <summary>Creates <see cref="ApiResult{T}"/> values.</summary>
public static class ApiResult
{
    public static ApiResult<T> Ok<T>(T value) => new(value, ApiFailure.None);

    public static ApiResult<T> Fail<T>(ApiFailure failure) => new(default, failure);
}
