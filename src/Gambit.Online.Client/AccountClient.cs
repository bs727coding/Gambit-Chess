using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Gambit.Online.Client;

/// <summary>An account request the server refused, or a session it no longer accepts. The message is for the player.</summary>
public sealed class OnlineAccountException(string message, bool signInRequired = false) : Exception(message)
{
    /// <summary>The saved sign-in is no longer valid: ask the player to sign in again.</summary>
    public bool SignInRequired { get; } = signInRequired;
}

/// <summary>Account calls to a Gambit server (<see cref="AccountApi"/>): sign up, sign in, invites.</summary>
public static class AccountClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly HttpClient Downloads = new() { Timeout = TimeSpan.FromMinutes(10) };

    public static Task<ServerInfoDto> GetInfoAsync(string serverUrl, CancellationToken ct = default) =>
        SendAsync<ServerInfoDto>(HttpMethod.Get, serverUrl, AccountApi.Info, null, null, ct);

    public static Task<SessionDto> RegisterAsync(string serverUrl, string username, string password, string? inviteCode, string? device, CancellationToken ct = default) =>
        SendAsync<SessionDto>(HttpMethod.Post, serverUrl, AccountApi.Register, null, new RegisterRequest(username, password, inviteCode, device), ct);

    public static Task<SessionDto> SignInAsync(string serverUrl, string username, string password, string? device, CancellationToken ct = default) =>
        SendAsync<SessionDto>(HttpMethod.Post, serverUrl, AccountApi.SignIn, null, new SignInRequest(username, password, device), ct);

    /// <summary>Ends the session on the server (best effort: a session the server already forgot is fine).</summary>
    public static async Task SignOutAsync(string serverUrl, string token, CancellationToken ct = default)
    {
        try
        {
            await SendAsync<object?>(HttpMethod.Post, serverUrl, AccountApi.SignOut, token, null, ct);
        }
        catch (OnlineAccountException ex) when (ex.SignInRequired)
        {
        }
    }

    public static Task<AccountDto> GetAccountAsync(string serverUrl, string token, CancellationToken ct = default) =>
        SendAsync<AccountDto>(HttpMethod.Get, serverUrl, AccountApi.Me, token, null, ct);

    public static Task<InviteDto> CreateInviteAsync(string serverUrl, string token, CancellationToken ct = default) =>
        SendAsync<InviteDto>(HttpMethod.Post, serverUrl, AccountApi.Invites, token, null, ct);

    public static Task<List<InviteDto>> GetInvitesAsync(string serverUrl, string token, CancellationToken ct = default) =>
        SendAsync<List<InviteDto>>(HttpMethod.Get, serverUrl, AccountApi.Invites, token, null, ct);

    public static Task ChangePasswordAsync(string serverUrl, string token, string current, string replacement, CancellationToken ct = default) =>
        SendAsync<object?>(HttpMethod.Post, serverUrl, AccountApi.Password, token, new ChangePasswordRequest(current, replacement), ct);

    /// <summary>Copies the server's database into <paramref name="destination"/> (admins only).</summary>
    public static async Task DownloadBackupAsync(string serverUrl, string token, Stream destination, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, serverUrl.TrimEnd('/') + AccountApi.Backup);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpResponseMessage response;
        try
        {
            response = await Downloads.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new OnlineAccountException($"Couldn't reach the server ({ex.Message}).");
        }
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Forbidden) throw new OnlineAccountException("Only the server's admins can download its database.");
            if (!response.IsSuccessStatusCode) throw await ErrorAsync(response, token, ct).ConfigureAwait(false);
            await response.Content.CopyToAsync(destination, ct).ConfigureAwait(false);
        }
    }

    private static async Task<T> SendAsync<T>(HttpMethod method, string serverUrl, string path, string? token, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, serverUrl.TrimEnd('/') + path);
        if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body != null) request.Content = JsonContent.Create(body, body.GetType());

        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new OnlineAccountException($"Couldn't reach the server ({ex.Message}).");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new OnlineAccountException("The server didn't answer in time.");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NoContent || typeof(T) == typeof(object)) return default!;
                return (await response.Content.ReadFromJsonAsync<T>(ct).ConfigureAwait(false))!;
            }

            throw await ErrorAsync(response, token, ct).ConfigureAwait(false);
        }
    }

    /// <summary>The player-facing error for a failed response (the server's <see cref="ApiError"/> message when it sent one).</summary>
    private static async Task<OnlineAccountException> ErrorAsync(HttpResponseMessage response, string? token, CancellationToken ct)
    {
        string? message = null;
        try
        {
            message = (await response.Content.ReadFromJsonAsync<ApiError>(ct).ConfigureAwait(false))?.Message;
        }
        catch (Exception)
        {
            // Not an ApiError body (e.g. a proxy's error page).
        }
        return response.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => new OnlineAccountException("Too many attempts from your network. Wait a minute and try again."),
            HttpStatusCode.Unauthorized when token != null => new OnlineAccountException("Please sign in again.", signInRequired: true),
            HttpStatusCode.NotFound => new OnlineAccountException("That server doesn't support accounts. Is it an older Gambit server?"),
            _ => new OnlineAccountException(message ?? $"The server said: {(int)response.StatusCode} {response.ReasonPhrase}."),
        };
    }
}
