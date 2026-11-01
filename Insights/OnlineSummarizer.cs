using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace RPChronicles.Insights;

/// <summary>
/// Sends one piece of scene text to OpenAI and returns a summary.
/// Callers have to ask the player before using this.
/// </summary>
internal static class OnlineSummarizer
{
    private const string Address = "https://api.openai.com/v1/chat/completions";

    public static async Task<string> SummarizeAsync(string apiKey, string model, string instruction, string transcript, CancellationToken cancellationToken)
    {
        var key = apiKey.Trim();
        if (key.Length == 0)
            throw new InvalidOperationException("Add your OpenAI key in Settings first.");

        var chosenModel = string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini" : model.Trim();
        var body = new JObject
        {
            ["model"] = chosenModel,
            ["temperature"] = 0.4,
            ["messages"] = new JArray
            {
                new JObject
                {
                    ["role"] = "system",
                    ["content"] = instruction,
                },
                new JObject
                {
                    ["role"] = "user",
                    ["content"] = transcript,
                },
            },
        };

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        var json = body.ToString();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Address);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new InvalidOperationException("The summary could not be reached. Check your internet connection.");
            }

            using (response)
            {
                var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var root = JObject.Parse(payload);
                    var summary = root["choices"]?[0]?["message"]?["content"]?.ToString()?.Trim();
                    if (string.IsNullOrWhiteSpace(summary))
                        throw new InvalidOperationException("The model sent back an empty summary.");

                    return summary;
                }

                var reason = Explain(response.StatusCode, payload);
                if ((int)response.StatusCode == 429 && attempt < 3 && IsTemporary(payload))
                {
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw new InvalidOperationException(reason);
            }
        }

        throw new InvalidOperationException("OpenAI did not accept the summary. Try again in a minute.");
    }

    public static async Task<string> SummarizeWithPlanAsync(string accessToken, string model, string instruction, string transcript, CancellationToken cancellationToken)
        => await SummarizeWithPlanAsync(accessToken, model, instruction, transcript, cancellationToken, "https://api.openai.com/v1/responses", "ChatGPT").ConfigureAwait(false);

    public static async Task<string> SummarizeWithPlanAsync(string accessToken, string model, string instruction, string transcript, CancellationToken cancellationToken, string endpoint, string service)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException($"Sign in with {service} in Settings. That uses the plan you already pay for.");

        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException($"Sign in with {service} again so Afterword can choose a model from that plan.");

        var grok = endpoint.Contains("cli-chat-proxy.grok.com", StringComparison.OrdinalIgnoreCase);
        var body = new JObject
        {
            ["model"] = model,
            ["instructions"] = instruction,
            ["input"] = grok
                ? new JArray
                {
                    new JObject
                    {
                        ["type"] = "message",
                        ["role"] = "user",
                        ["content"] = new JArray
                        {
                            new JObject
                            {
                                ["type"] = "input_text",
                                ["text"] = transcript,
                            },
                        },
                    },
                }
                : new JArray
                {
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = transcript,
                    },
                },
            ["store"] = false,
            ["stream"] = true,
        };

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
        if (grok)
            ApplyGrokClientHeaders(request);
        request.Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new InvalidOperationException("The summary could not be reached. Check your internet connection.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new InvalidOperationException(DescribePlan((int)response.StatusCode, payload, service));
            }

            var text = new StringBuilder();
            var completed = false;
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            while (true)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line == null)
                    break;

                if (!line.StartsWith("data:", StringComparison.Ordinal))
                    continue;

                var data = line["data:".Length..].Trim();
                if (data.Length == 0 || data == "[DONE]")
                    continue;

                JObject eventObject;
                try
                {
                    eventObject = JObject.Parse(data);
                }
                catch
                {
                    continue;
                }

                var type = eventObject["type"]?.ToString();
                if (type == "response.output_text.delta")
                {
                    text.Append(eventObject["delta"]?.ToString());
                }
                else if (type == "response.output_text.done" && text.Length == 0)
                {
                    text.Append(eventObject["text"]?.ToString());
                }
                else if (type is "error" or "response.failed" or "response.incomplete")
                {
                    var error = eventObject["error"] ?? eventObject["response"]?["error"];
                    throw new InvalidOperationException(DescribePlan(0, error?.ToString() ?? data, service));
                }
                else if (type == "response.completed")
                {
                    completed = true;
                    if (text.Length == 0)
                        AppendOutput(text, eventObject["response"]);
                }
            }

            if (!completed || text.Length == 0)
                throw new InvalidOperationException($"{service} did not finish the summary. Try again.");

            return text.ToString().Trim();
        }
    }

    private static void AppendOutput(StringBuilder text, JToken? response)
    {
        if (response?["output"] is not JArray output)
            return;

        foreach (var item in output)
        {
            if (item["content"] is not JArray content)
                continue;

            foreach (var part in content)
            {
                var kind = part["type"]?.ToString();
                if (kind is "output_text" or "text")
                    text.Append(part["text"]?.ToString());
            }
        }
    }

    private static string DescribePlan(int status, string payload, string service)
    {
        var code = string.Empty;
        var message = string.Empty;
        var detail = string.Empty;
        try
        {
            var root = JObject.Parse(payload);
            if (root["detail"]?.Type == JTokenType.String)
                detail = root["detail"]!.ToString();

            if (root["error"]?.Type == JTokenType.String)
                message = root["error"]!.ToString();

            var error = root["error"] as JObject ?? root["response"]?["error"] as JObject;
            if (error != null)
            {
                code = error["code"]?.ToString() ?? string.Empty;
                var objectMessage = error["message"]?.ToString() ?? string.Empty;
                if (objectMessage.Length > 0)
                    message = objectMessage;
            }
        }
        catch
        {
            message = payload.Trim();
        }

        if (code.Contains("usage_limit", StringComparison.OrdinalIgnoreCase))
            return $"Your {service} plan has used its included amount for now. Wait until it resets, then try again. This does not ask you to pay again.";

        if (code.Contains("not_eligible", StringComparison.OrdinalIgnoreCase))
            return $"This {service} account cannot write summaries in other apps.";

        if (status == 401 || code.Contains("invalid_user", StringComparison.OrdinalIgnoreCase))
            return $"{service} sign-in expired. Sign in again in Settings.";

        if (code.Contains("scope_not_authorized", StringComparison.OrdinalIgnoreCase)
            || code.Contains("invalid_authorization", StringComparison.OrdinalIgnoreCase))
            return $"Sign in with {service} again and allow Afterword to use that plan.";

        if (status == 503 || code.Contains("unavailable", StringComparison.OrdinalIgnoreCase))
            return $"{service} could not check your plan just now. Wait a moment and try Summarize again.";

        var readable = message.Length > 0 ? message : detail;
        if (readable.Length > 180)
            readable = readable[..180].TrimEnd() + "…";

        return readable.Length == 0
            ? $"{service} did not send a summary. Try again."
            : readable;
    }

    internal static void ApplyGrokClientHeaders(HttpRequestMessage request)
    {
        const string version = "1.0.46";
        request.Headers.TryAddWithoutValidation("X-XAI-Token-Auth", "xai-grok-cli");
        request.Headers.TryAddWithoutValidation("x-grok-client-version", version);
        request.Headers.TryAddWithoutValidation("x-grok-client-mode", "cli");
        request.Headers.TryAddWithoutValidation("User-Agent", "xai-grok-workspace/" + version);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
    }

    private static bool IsTemporary(string payload)
    {
        var code = ErrorCode(payload);
        if (code is "insufficient_quota")
            return false;

        try
        {
            var message = JObject.Parse(payload)["error"]?["message"]?.ToString() ?? string.Empty;
            if (message.Contains("quota", StringComparison.OrdinalIgnoreCase) || message.Contains("billing", StringComparison.OrdinalIgnoreCase))
                return false;
        }
        catch
        {
            return true;
        }

        return code is "rate_limit_exceeded" or "rate_limit" or "";
    }

    private static string ErrorCode(string payload)
    {
        try
        {
            return JObject.Parse(payload)["error"]?["code"]?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string Explain(System.Net.HttpStatusCode status, string payload)
    {
        if (status == System.Net.HttpStatusCode.Unauthorized || status == System.Net.HttpStatusCode.Forbidden)
            return "That key was rejected. Check the key in Settings.";

        var code = ErrorCode(payload);
        var message = string.Empty;
        try
        {
            message = JObject.Parse(payload)["error"]?["message"]?.ToString()?.Trim() ?? string.Empty;
        }
        catch
        {
            message = string.Empty;
        }

        if ((int)status == 429)
        {
            if (code is "insufficient_quota" || message.Contains("quota", StringComparison.OrdinalIgnoreCase) || message.Contains("billing", StringComparison.OrdinalIgnoreCase))
                return "OpenAI refused this because the key has no credit. Open platform.openai.com/billing, add a payment method, then try Summarize again.";

            return "OpenAI is limiting how fast this key can be used. Wait a minute, then try Summarize again.";
        }

        if (message.Length > 180)
            message = message[..180].TrimEnd() + "…";

        return message.Length == 0
            ? "The summary did not come back."
            : message;
    }
}
