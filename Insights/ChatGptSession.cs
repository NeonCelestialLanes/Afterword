using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RPChronicles.Insights;

/// <summary>
/// Signs in to the ChatGPT plan the player already pays for.
/// Summaries use that plan. They do not buy API credits.
/// </summary>
internal sealed class ChatGptSession
{
    private const string Authorize = "https://auth.openai.com/api/accounts/authorize";
    private const string Token = "https://auth.openai.com/api/accounts/oauth/token";
    private const string Resource = "https://api.openai.com/v1";
    private readonly object gate = new();
    private readonly string path;
    private LoginFile login;

    public ChatGptSession(string configDirectory)
    {
        Directory.CreateDirectory(configDirectory);
        this.path = Path.Combine(configDirectory, "chatgpt-login.json");
        this.login = Read();
        if (string.IsNullOrWhiteSpace(this.login.HostId))
        {
            this.login.HostId = "urn:uuid:" + Guid.NewGuid();
            this.Save();
        }
    }

    public bool Busy { get; private set; }

    public string Status { get; private set; } = string.Empty;

    public bool IsSignedIn =>
        !string.IsNullOrWhiteSpace(this.login.RefreshToken)
        && this.login.Scopes.Contains("chatgpt.tokens.use.direct");

    public string Email => this.login.Email;

    public void StartSignIn()
    {
        if (this.Busy)
            return;

        this.Busy = true;
        this.Status = "A browser window is opening. Sign in to ChatGPT and allow Afterword to use that plan.";
        _ = Task.Run(this.SignIn);
    }

    public void SignOut()
    {
        lock (this.gate)
        {
            var host = this.login.HostId;
            this.login = new LoginFile { HostId = host };
            this.Save();
        }

        this.Status = "Signed out on this computer. You can also disconnect Afterword in ChatGPT settings.";
    }

    public async Task<string> SummarizeAsync(string instruction, string transcript, CancellationToken cancellationToken)
    {
        var token = await this.AccessToken(cancellationToken).ConfigureAwait(false);
        var model = this.login.Model;
        try
        {
            return await OnlineSummarizer.SummarizeWithPlanAsync(token, model, instruction, transcript, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("sign in again", StringComparison.OrdinalIgnoreCase))
        {
            token = await this.ForceRefresh(cancellationToken).ConfigureAwait(false);
            return await OnlineSummarizer.SummarizeWithPlanAsync(token, model, instruction, transcript, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SignIn()
    {
        HttpListener? listener = null;
        try
        {
            if (!TryListen(out listener, out var port) || listener == null)
                throw new InvalidOperationException("Afterword could not open the sign-in page. Try again.");

            var redirect = $"http://127.0.0.1:{port}/auth/callback";
            var state = RandomToken();
            var nonce = RandomToken();
            var verifier = RandomToken();
            var challenge = Challenge(verifier);
            var freshAccount = string.IsNullOrWhiteSpace(this.login.ClientId);
            var clientId = freshAccount ? "dynamic_agent_client" : this.login.ClientId;
            var url = BuildAuthorize(clientId, freshAccount, redirect, state, nonce, challenge);

            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            var context = await WaitForCallback(listener).ConfigureAwait(false);
            var query = ParseQuery(context.Request.Url?.Query);
            Reply(context);

            if (!string.Equals(query.GetValueOrDefault("state"), state, StringComparison.Ordinal))
                throw new InvalidOperationException("The sign-in reply did not match this attempt. Try again.");

            if (query.TryGetValue("error", out var error) && error.Length > 0)
                throw new InvalidOperationException("ChatGPT sign-in was cancelled.");

            if (!query.TryGetValue("code", out var code) || code.Length == 0)
                throw new InvalidOperationException("ChatGPT did not finish signing in. Try again.");

            var issued = query.GetValueOrDefault("client_id");
            if (freshAccount)
            {
                if (string.IsNullOrWhiteSpace(issued) || issued == "dynamic_agent_client")
                    throw new InvalidOperationException("ChatGPT did not finish registering Afterword. Try again.");
            }
            else if (!string.IsNullOrWhiteSpace(issued) && !string.Equals(issued, this.login.ClientId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("ChatGPT returned a different sign-in than the one already saved. Try again.");
            }

            var usedClient = string.IsNullOrWhiteSpace(issued) ? this.login.ClientId : issued;
            var token = await Exchange(usedClient, code, verifier, redirect).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token.AccessToken))
                throw new InvalidOperationException("ChatGPT did not finish signing in. Try again.");

            this.Accept(token, usedClient, nonce);
            await this.ChooseModel(token.AccessToken).ConfigureAwait(false);
            this.Status = string.IsNullOrWhiteSpace(this.login.Email)
                ? "Signed in with ChatGPT."
                : "Signed in as " + this.login.Email + ".";
        }
        catch (Exception ex)
        {
            this.Status = ex.Message;
        }
        finally
        {
            try
            {
                listener?.Stop();
                listener?.Close();
            }
            catch
            {
                // The listener is only used for this one sign-in.
            }

            this.Busy = false;
        }
    }

    private async Task<string> AccessToken(CancellationToken cancellationToken)
    {
        if (!this.IsSignedIn)
            throw new InvalidOperationException("Sign in with ChatGPT in Settings. That uses the plan you already pay for.");

        if (this.login.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
            return this.login.AccessToken;

        return await this.ForceRefresh(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> ForceRefresh(CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var request = new HttpRequestMessage(HttpMethod.Post, Token);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = this.login.ClientId,
            ["refresh_token"] = this.login.RefreshToken,
            ["resource"] = Resource,
        });

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("ChatGPT sign-in expired. Sign in again in Settings.");

        var token = JsonConvert.DeserializeObject<TokenResponse>(payload) ?? throw new InvalidOperationException("ChatGPT sign-in expired. Sign in again in Settings.");
        lock (this.gate)
        {
            this.login.AccessToken = token.AccessToken ?? this.login.AccessToken;
            if (!string.IsNullOrWhiteSpace(token.RefreshToken))
                this.login.RefreshToken = token.RefreshToken;
            if (!string.IsNullOrWhiteSpace(token.IdToken))
                this.login.IdToken = token.IdToken;
            this.login.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn <= 0 ? 3600 : token.ExpiresIn);
            this.Save();
        }

        return this.login.AccessToken;
    }

    private void Accept(TokenResponse token, string clientId, string nonce)
    {
        var identity = ReadIdentity(token.IdToken, clientId, nonce);
        var scopes = (token.Scope ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!scopes.Contains("chatgpt.tokens.use.direct"))
            throw new InvalidOperationException("Allow Afterword to use your ChatGPT plan when the browser asks. Sign-in alone is not enough.");

        lock (this.gate)
        {
            this.login.ClientId = clientId;
            this.login.Email = identity.Email;
            this.login.Subject = identity.Subject;
            this.login.IdToken = token.IdToken ?? string.Empty;
            this.login.AccessToken = token.AccessToken ?? string.Empty;
            this.login.RefreshToken = token.RefreshToken ?? string.Empty;
            this.login.Scopes = new List<string>(scopes);
            this.login.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn <= 0 ? 3600 : token.ExpiresIn);
            this.Save();
        }
    }

    private async Task ChooseModel(string accessToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Signed in, but ChatGPT did not share a model to use. Try again.");

        var root = JObject.Parse(payload);
        var list = root["models"] as JArray ?? root["data"] as JArray;
        if (list == null)
            throw new InvalidOperationException("Signed in, but ChatGPT did not share a model to use. Try again.");

        foreach (var item in list)
        {
            var visibility = item["visibility"]?.ToString();
            if (!string.IsNullOrWhiteSpace(visibility) && !string.Equals(visibility, "list", StringComparison.OrdinalIgnoreCase))
                continue;

            var slug = item["slug"]?.ToString() ?? item["id"]?.ToString();
            if (string.IsNullOrWhiteSpace(slug))
                continue;

            lock (this.gate)
            {
                this.login.Model = slug;
                this.Save();
            }

            return;
        }

        throw new InvalidOperationException("Signed in, but ChatGPT did not share a model to use. Try again.");
    }

    private static async Task<TokenResponse> Exchange(string clientId, string code, string verifier, string redirect)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var request = new HttpRequestMessage(HttpMethod.Post, Token);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = redirect,
            ["resource"] = Resource,
        });

        using var response = await client.SendAsync(request).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("ChatGPT did not finish signing in. Try again.");

        return JsonConvert.DeserializeObject<TokenResponse>(payload)
            ?? throw new InvalidOperationException("ChatGPT did not finish signing in. Try again.");
    }

    private string BuildAuthorize(string clientId, bool freshAccount, string redirect, string state, string nonce, string challenge)
    {
        var query = new List<string>
        {
            "client_id=" + Uri.EscapeDataString(clientId),
            "response_type=code",
            "redirect_uri=" + Uri.EscapeDataString(redirect),
            "scope=" + Uri.EscapeDataString("openid profile email offline_access resource.invoke chatgpt.tokens.use.direct"),
            "resource=" + Uri.EscapeDataString(Resource),
            "state=" + Uri.EscapeDataString(state),
            "nonce=" + Uri.EscapeDataString(nonce),
            "code_challenge_method=S256",
            "code_challenge=" + Uri.EscapeDataString(challenge),
            "ext_agent_host_id=" + Uri.EscapeDataString(this.login.HostId),
        };

        if (freshAccount)
            query.Add("agent_name_hint=" + Uri.EscapeDataString("Afterword"));
        else if (!string.IsNullOrWhiteSpace(this.login.IdToken))
            query.Add("id_token_hint=" + Uri.EscapeDataString(this.login.IdToken));

        return Authorize + "?" + string.Join("&", query);
    }

    private static bool TryListen(out HttpListener? listener, out int port)
    {
        listener = null;
        port = 0;
        for (var candidate = 1455; candidate < 1470; candidate++)
        {
            var trial = new HttpListener();
            try
            {
                trial.Prefixes.Add($"http://127.0.0.1:{candidate}/auth/callback/");
                trial.Start();
                listener = trial;
                port = candidate;
                return true;
            }
            catch
            {
                trial.Close();
            }
        }

        return false;
    }

    private static async Task<HttpListenerContext> WaitForCallback(HttpListener listener)
    {
        var wait = listener.GetContextAsync();
        var done = await Task.WhenAny(wait, Task.Delay(TimeSpan.FromMinutes(3))).ConfigureAwait(false);
        if (done != wait)
            throw new InvalidOperationException("Sign-in timed out. Try again and finish it in the browser.");

        return await wait.ConfigureAwait(false);
    }

    private static void Reply(HttpListenerContext context)
    {
        var page = Encoding.UTF8.GetBytes("<html><body><p>You can close this tab and return to the game.</p></body></html>");
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = page.Length;
        context.Response.OutputStream.Write(page, 0, page.Length);
        context.Response.OutputStream.Close();
    }

    private static Identity ReadIdentity(string? idToken, string clientId, string nonce)
    {
        if (string.IsNullOrWhiteSpace(idToken))
            throw new InvalidOperationException("ChatGPT did not confirm the account. Try again.");

        var parts = idToken.Split('.');
        if (parts.Length < 2)
            throw new InvalidOperationException("ChatGPT did not confirm the account. Try again.");

        var json = Encoding.UTF8.GetString(Base64Url(parts[1]));
        var payload = JObject.Parse(json);
        if (!string.Equals(payload["nonce"]?.ToString(), nonce, StringComparison.Ordinal))
            throw new InvalidOperationException("The sign-in reply did not match this attempt. Try again.");

        if (!string.Equals(payload["iss"]?.ToString(), "https://auth.openai.com", StringComparison.Ordinal))
            throw new InvalidOperationException("ChatGPT did not confirm the account. Try again.");

        var audience = payload["aud"];
        var audienceText = audience?.Type == JTokenType.Array ? audience.First?.ToString() : audience?.ToString();
        if (!string.Equals(audienceText, clientId, StringComparison.Ordinal))
            throw new InvalidOperationException("ChatGPT did not confirm the account. Try again.");

        var exp = payload["exp"]?.Value<long>() ?? 0;
        if (exp > 0 && DateTimeOffset.FromUnixTimeSeconds(exp) < DateTimeOffset.UtcNow)
            throw new InvalidOperationException("ChatGPT sign-in expired. Try again.");

        return new Identity
        {
            Email = payload["email"]?.ToString() ?? string.Empty,
            Subject = payload["sub"]?.ToString() ?? string.Empty,
        };
    }

    private LoginFile Read()
    {
        if (!File.Exists(this.path))
            return new LoginFile();

        try
        {
            var loaded = JsonConvert.DeserializeObject<LoginFile>(File.ReadAllText(this.path)) ?? new LoginFile();
            loaded.Scopes ??= new List<string>();
            return loaded;
        }
        catch
        {
            return new LoginFile();
        }
    }

    private void Save()
    {
        var json = JsonConvert.SerializeObject(this.login, Formatting.Indented);
        var temp = this.path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, this.path, overwrite: true);
    }

    private static Dictionary<string, string> ParseQuery(string? query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(query))
            return values;

        var text = query.StartsWith('?') ? query[1..] : query;
        foreach (var pair in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = pair.IndexOf('=');
            if (split < 0)
                continue;

            values[Uri.UnescapeDataString(pair[..split])] = Uri.UnescapeDataString(pair[(split + 1)..]);
        }

        return values;
    }

    private static string RandomToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Challenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64Url(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        return Convert.FromBase64String(padded);
    }

    private sealed class LoginFile
    {
        public string HostId { get; set; } = string.Empty;

        public string ClientId { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string Subject { get; set; } = string.Empty;

        public string IdToken { get; set; } = string.Empty;

        public string AccessToken { get; set; } = string.Empty;

        public string RefreshToken { get; set; } = string.Empty;

        public string Model { get; set; } = string.Empty;

        public DateTimeOffset ExpiresAt { get; set; }

        public List<string> Scopes { get; set; } = new();
    }

    private sealed class TokenResponse
    {
        [JsonProperty("access_token")]
        public string? AccessToken { get; set; }

        [JsonProperty("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonProperty("id_token")]
        public string? IdToken { get; set; }

        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonProperty("scope")]
        public string? Scope { get; set; }
    }

    private sealed class Identity
    {
        public string Email { get; set; } = string.Empty;

        public string Subject { get; set; } = string.Empty;
    }
}
