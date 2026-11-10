using System;
using System.Collections;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace hamburbur.Daedalus;

public sealed class DaedalusApiResult
{
    public bool    Ok;
    public long    Status;
    public string  ErrorClass;
    public JObject Body;
}

public static class DaedalusApi
{
    public const int TranscribeTimeoutSeconds = 70;
    public const int ChatTimeoutSeconds       = 120;

    public static IEnumerator TranscribeWav(byte[] wavBytes, Action<DaedalusApiResult> done)
    {
        JObject body = new()
        {
                ["input_audio"] = new JObject
                {
                        ["data"]   = Convert.ToBase64String(wavBytes ?? Array.Empty<byte>()),
                        ["format"] = "wav",
                },
        };

        yield return SendJson("api/v1/transcribe", body, TranscribeTimeoutSeconds, done);
    }

    public static IEnumerator ChatSpeech(
            string message,
            object gameSnapshot,
            int?   conversationId,
            Action<DaedalusApiResult> done)
    {
        JObject body = new()
        {
                ["message"] = message ?? "",
                ["model"]   = DaedalusConfig.SpeechModel,
                ["game"]    = gameSnapshot == null
                                      ? JValue.CreateNull()
                                      : JToken.FromObject(gameSnapshot),
        };

        if (conversationId.HasValue)
            body["conversation_id"] = conversationId.Value;

        yield return SendJson("api/v1/chat", body, ChatTimeoutSeconds, done);
    }

    public static string TranscribeText(DaedalusApiResult result)
    {
        if (result?.Body == null)
            return null;

        JToken text = result.Body["text"];
        return text?.Type == JTokenType.String ? ((string)text)?.Trim() : null;
    }

    public static string ChatReply(DaedalusApiResult result)
    {
        if (result?.Body == null)
            return "";

        string reply = (string)result.Body["reply"] ?? (string)result.Body["response"];
        return reply?.Trim() ?? "";
    }

    public static int? ChatConversationId(DaedalusApiResult result)
    {
        JToken id = result?.Body?["conversation_id"];
        if (id == null || id.Type == JTokenType.Null)
            return null;

        if (id.Type == JTokenType.Integer)
            return (int)id;

        return int.TryParse((string)id, out int parsed) ? parsed : (int?)null;
    }

    public static string ChatAudioDataUri(DaedalusApiResult result)
    {
        if (result?.Body == null)
            return "";

        foreach (string name in new[] { "audio", "speech_audio", "output_audio", "audio_data" })
        {
            JToken token = result.Body[name];
            string value = TokenToAudioUri(token);
            if (!string.IsNullOrEmpty(value))
                return value;
        }

        return "";
    }

    public static JArray ChatActions(DaedalusApiResult result)
    {
        JToken token = result?.Body?["actions"];
        return token as JArray;
    }

    private static string TokenToAudioUri(JToken token)
    {
        if (token == null || token.Type == JTokenType.Null)
            return "";

        if (token.Type == JTokenType.String)
        {
            string value = ((string)token)?.Trim();
            return string.IsNullOrEmpty(value) ? "" : value;
        }

        if (token.Type != JTokenType.Object)
            return "";

        string nested = ((string)token["data"] ?? (string)token["b64"] ?? (string)token["base64"])?.Trim();
        if (string.IsNullOrEmpty(nested))
            return "";

        if (nested.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return nested;

        string format = ((string)token["format"] ?? (string)token["mime"] ?? "wav").Trim().TrimStart('.');
        if (string.IsNullOrEmpty(format))
            format = "wav";

        if (!format.Contains("/"))
            format = "audio/" + format;

        return "data:" + format + ";base64," + nested;
    }

    private static IEnumerator SendJson(string relativePath, JObject body, int timeoutSeconds, Action<DaedalusApiResult> done)
    {
        DaedalusApiResult result = new();

        if (!DaedalusConfig.HasApiKey)
        {
            result.ErrorClass = "MissingKey";
            result.Status     = 401;
            done?.Invoke(result);
            yield break;
        }

        string baseUrl = DaedalusConfig.BaseUrl;
        if (!baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            result.ErrorClass = "InsecureBaseUrl";
            result.Status     = 400;
            done?.Invoke(result);
            yield break;
        }

        string url  = baseUrl + "/" + relativePath.TrimStart('/');
        byte[] json = Encoding.UTF8.GetBytes(body.ToString(Formatting.None));

        using UnityWebRequest request = new(url, UnityWebRequest.kHttpVerbPOST)
        {
                uploadHandler   = new UploadHandlerRaw(json),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout         = timeoutSeconds,
        };

        request.SetRequestHeader("Content-Type",  "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + DaedalusConfig.ApiKey);

        yield return request.SendWebRequest();

        result.Status = request.responseCode;

        string payload = request.downloadHandler?.text;
        if (!string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                result.Body = JObject.Parse(payload);
            }
            catch (JsonException)
            {
                result.Body = null;
            }
        }

        if (request.result == UnityWebRequest.Result.Success && request.responseCode >= 200 && request.responseCode < 300)
        {
            result.Ok = true;
            done?.Invoke(result);
            yield break;
        }

        result.ErrorClass = Classify(request);
        done?.Invoke(result);
    }

    private static string Classify(UnityWebRequest request)
    {
        long code = request.responseCode;

        return code switch
        {
                400 => "BadRequest",
                401 => "Unauthorized",
                403 => "Forbidden",
                413 => "PayloadTooLarge",
                429 => "RateLimited",
                502 => "BadGateway",
                503 => "Unavailable",
                var _ when request.result == UnityWebRequest.Result.ConnectionError => "ConnectionError",
                var _ when request.result == UnityWebRequest.Result.DataProcessingError => "DataProcessingError",
                var _ when request.result == UnityWebRequest.Result.ProtocolError => "ProtocolError",
                var _ => "RequestError",
        };
    }
}
