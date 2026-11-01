using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RPChronicles.Insights;

/// <summary>
/// Signs in to the Grok plan the player already pays for.
/// Summaries use that plan. They do not buy API credits.
/// </summary>
internal sealed class GrokSession
{
    private const string ClientId = "b1a00492-073a-47ea-816f-4c329264a828";
    private const string DeviceUrl = "https://auth.x.ai/oauth2/device/code";
    private const string TokenUrl = "https://auth.x.ai/oauth2/token";
    private const string Scope = "openid profile email offline_access grok-cli:access api:access conversations:read conversations:write";
    private const string Endpoint = "https://cli-chat-proxy.grok.com/v1/responses";
    private readonly object gate = new();
    private readonly string path;
    private LoginFile login;

    public GrokSession(string configDirectory)
    {
        Directory.CreateDirectory(configDirectory);
        this.path = Path.Combine(configDirectory, "grok-login.json");
        this.login = this.Read();
    }

    public bool Busy { get; private set; }

    public string Status { get; private set; } = string.Empty;

    public bool IsSignedIn => !string.IsNullOrWhiteSpace(this.login.RefreshToken);

    public string Email => this.login.Email;

    public void StartSignIn()
    {
        if (this.Busy)
            return;

        this.Busy = true;
        this.Status = "A browser window is opening. Sign in to Grok and allow Afterword to use that plan.";
        _ = Task.Run(this.SignIn);
    }

    public void SignOut()
    {
        lock (this.gate)
        {
            this.login = new LoginFile();
            this.Save();
        }

        this.Status = "Signed out of Grok on this computer.";
    }

    public async Task<string> SummarizeAsync(string instruction, string transcript, CancellationToken cancellationToken)
    {
        var token = await this.AccessToken(cancellationToken).ConfigureAwait(false);
        var model = string.IsNullOrWhiteSpace(this.login.Model) ? "grok-4" : this.login.Model;
        return await OnlineSummarizer.SummarizeWithPlanAsync(token, model, instruction, transcript, cancellationToken, Endpoint, "Grok").ConfigureAwait(false);
    }

    private async Task SignIn()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var request = new HttpRequestMessage(HttpMethod.Post, DeviceUrl);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["scope"] = Scope,
                ["referrer"] = "afterword",
            });

            using var response = await client.SendAsync(request).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("Grok did not open a sign-in. Try again.");

            var device = JsonConvert.DeserializeObject<DeviceCode>(payload)
                ?? throw new InvalidOperationException("Grok did not open a sign-in. Try again.");
            if (string.IsNullOrWhiteSpace(device.Code) || string.IsNullOrWhiteSpace(device.UserCode))
                throw new InvalidOperationException("Grok did not open a sign-in. Try again.");

            var page = string.IsNullOrWhiteSpace(device.VerificationUriComplete) ? device.VerificationUri : device.VerificationUriComplete;
            if (!string.IsNullOrWhiteSpace(page))
                Process.Start(new ProcessStartInfo(page) { UseShellExecute = true });

            this.Status = "In the browser, sign in to Grok. If it asks for a code, enter " + device.UserCode + ".";
            var token = await Poll(client, device).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token.AccessToken) || string.IsNullOrWhiteSpace(token.RefreshToken))
                throw new InvalidOperationException("Grok did not finish signing in. Try again.");

            lock (this.gate)
            {
                this.login.AccessToken = token.AccessToken;
                this.login.RefreshToken = token.RefreshToken;
                this.login.Email = EmailFrom(token.IdToken);
                this.login.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn <= 0 ? 3600 : token.ExpiresIn);
                this.Save();
            }

            await this.ChooseModel(token.AccessToken).ConfigureAwait(false);
            this.Status = string.IsNullOrWhiteSpace(this.login.Email)
                ? "Signed in with Grok."
                : "Signed in to Grok as " + this.login.Email + ".";
        }
        catch (Exception ex)
        {
            this.Status = ex.Message;
        }
        finally
        {
            this.Busy = false;
        }
    }

    private static async Task<TokenResponse> Poll(HttpClient client, DeviceCode device)
    {
        var interval = device.Interval <= 0 ? 5 : device.Interval;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(device.ExpiresIn <= 0 ? 900 : device.ExpiresIn);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval)).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["client_id"] = ClientId,
                ["device_code"] = device.Code,
            });

            using var response = await client.SendAsync(request).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return JsonConvert.DeserializeObject<TokenResponse>(payload) ?? throw new InvalidOperationException("Grok did not finish signing in. Try again.");

            var error = string.Empty;
            try
            {
                error = JObject.Parse(payload)["error"]?.ToString() ?? string.Empty;
            }
            catch
            {
                error = string.Empty;
            }

            if (error == "authorization_pending")
                continue;

            if (error == "slow_down")
            {
                interval += 5;
                continue;
            }

            if (error is "access_denied" or "expired_token")
                throw new InvalidOperationException("Grok sign-in was cancelled.");

            throw new InvalidOperationException("Grok did not finish signing in. Try again.");
        }

        throw new InvalidOperationException("Grok sign-in timed out. Try again.");
    }

    private async Task<string> AccessToken(CancellationToken cancellationToken)
    {
        if (!this.IsSignedIn)
            throw new InvalidOperationException("Sign in with Grok in Settings. That uses the plan you already pay for.");

        if (this.login.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2) && !string.IsNullOrWhiteSpace(this.login.AccessToken))
            return this.login.AccessToken;

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = ClientId,
            ["refresh_token"] = this.login.RefreshToken,
        });

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Grok sign-in expired. Sign in again in Settings.");

        var token = JsonConvert.DeserializeObject<TokenResponse>(payload)
            ?? throw new InvalidOperationException("Grok sign-in expired. Sign in again in Settings.");
        if (string.IsNullOrWhiteSpace(token.AccessToken))
            throw new InvalidOperationException("Grok sign-in expired. Sign in again in Settings.");

        lock (this.gate)
        {
            this.login.AccessToken = token.AccessToken;
            if (!string.IsNullOrWhiteSpace(token.RefreshToken))
                this.login.RefreshToken = token.RefreshToken;
            this.login.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn <= 0 ? 3600 : token.ExpiresIn);
            this.Save();
        }

        return this.login.AccessToken;
    }

    private async Task ChooseModel(string accessToken)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://cli-chat-proxy.grok.com/v1/models");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await client.SendAsync(request).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return;

            var root = JObject.Parse(payload);
            var list = root["models"] as JArray ?? root["data"] as JArray;
            if (list == null)
                return;

            foreach (var item in list)
            {
                var slug = item["slug"]?.ToString() ?? item["id"]?.ToString();
                if (string.IsNullOrWhiteSpace(slug) || !slug.StartsWith("grok", StringComparison.OrdinalIgnoreCase))
                    continue;

                lock (this.gate)
                {
                    this.login.Model = slug;
                    this.Save();
                }

                return;
            }
        }
        catch
        {
            // Sign-in still stands. Summaries use grok-4 until a model list is available.
        }
    }

    private static string EmailFrom(string? idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken))
            return string.Empty;

        var parts = idToken.Split('.');
        if (parts.Length < 2)
            return string.Empty;

        try
        {
            var json = System.Text.Encoding.UTF8.GetString(Base64Url(parts[1]));
            return JObject.Parse(json)["email"]?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
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

    private LoginFile Read()
    {
        if (!File.Exists(this.path))
            return new LoginFile();

        try
        {
            return JsonConvert.DeserializeObject<LoginFile>(File.ReadAllText(this.path)) ?? new LoginFile();
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

    private sealed class LoginFile
    {
        public string Email { get; set; } = string.Empty;

        public string AccessToken { get; set; } = string.Empty;

        public string RefreshToken { get; set; } = string.Empty;

        public string Model { get; set; } = "grok-4";

        public DateTimeOffset ExpiresAt { get; set; }
    }

    private sealed class DeviceCode
    {
        [JsonProperty("device_code")]
        public string Code { get; set; } = string.Empty;

        [JsonProperty("user_code")]
        public string UserCode { get; set; } = string.Empty;

        [JsonProperty("verification_uri")]
        public string VerificationUri { get; set; } = string.Empty;

        [JsonProperty("verification_uri_complete")]
        public string? VerificationUriComplete { get; set; }

        [JsonProperty("interval")]
        public int Interval { get; set; }

        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }
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
    }
}
