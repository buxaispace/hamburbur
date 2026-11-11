using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GorillaNetworking;
using hamburbur.Components;
using hamburbur.Daedalus;
using hamburbur.GUI;
using hamburbur.Libs;
using hamburbur.Managers;
using hamburbur.Mod_Backend;
using hamburbur.Mods.Misc;
using hamburbur.Mods.Rig;
using hamburbur.Mods.Settings;
using hamburbur.Plugins;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Windows.Speech;
using Debug = UnityEngine.Debug;
using Random = System.Random;
using IEpoopenator = System.Collections.IEnumerator;

namespace hamburbur.Misc;

public class VoiceControls : Singleton<VoiceControls>
{
    private static readonly Regex JarvisCommandRegex = new(
            @"^\s*<\s*\[\s*(toggle|enable|disable|increment|decrement|press)\s*\]\s*([^\s>]+)\s*>",
            RegexOptions.IgnoreCase);

    private static readonly Regex LegacyJarvisCommandRegex = new(
            @"^\s*<\s*(toggle|enable|disable|increment|decrement|press)\s*>\s*([^\s<]+)",
            RegexOptions.IgnoreCase);

    private static readonly Regex FlexibleJarvisCommandRegex = new(
            @"<?\s*\[\s*(toggle|enable|disable|increment|decrement)\s*\]\s*([a-z0-9]+)\s*>?",
            RegexOptions.IgnoreCase);

    private static readonly Regex LooseJarvisCommandRegex = new(
            @"<?\s*\[\s*(toggle|enable|disable|increment|decrement|press)\s*\]\s*[a-z0-9]+\s*>?",
            RegexOptions.IgnoreCase);

    public string LastUsedWakeWord = "jarvis";

    private int? daedalusConversationId;

    private readonly string[] replyWords =
    [
            "Yes?",
            "How can I help?",
            "What can I do for you?",
            "I'm here.",
            "Go ahead.",
            "Need something?",
            "What's up?",
            "Hey, I'm listening.",
            "Yeah?",
            "You called?",
            "What's on your mind?",
            "At your service.",
            "Tell me.",
            "Ready when you are.",
            "What's the plan?",
            "I'm ready.",
            "You need me?",
            "How can I assist?",
            "Go on.",
            "Talk to me.",
            "I'm all ears.",
            "Listening.",
            "What do you need?",
            "What's going on?",
            "I'm with you.",
            "Right here.",
            "Yep?",
            "What is it?",
            "Standing by.",
            "Present.",
            "What can I help with?",
            "Fire away.",
            "Waiting.",
            "Here for you.",
            "You got me.",
            "I'm on it.",
            "Lay it on me.",
            "What do you got?",
    ];

    private readonly string[] wakeWords =
            ["jarvis", "system", "assistant", "friday", "echo", "cortana", "mainframe", "ultron", "terminal",];

    private bool              isListening;
    private KeywordRecognizer wakeRecognizer;

    private IEpoopenator Start()
    {
        if (DisableJarvis.IsEnabled)
            yield break;

        if (Application.platform != RuntimePlatform.WindowsPlayer || Environment.OSVersion.Version.Major < 10)
        {
            NotificationManager.SendNotification(
                    "<color=red>Error</color>",
                    "Your system cannot use the voice commands feature",
                    5f,
                    false,
                    false);

            yield break;
        }

        if (!Plugin.Instance.JarvisDidFirstInitialisation)
        {
            yield return new WaitForSeconds(5f);
            Plugin.Instance.JarvisDidFirstInitialisation = true;
        }

        // This gets annoying over time

        /*yield return AudioLib.Instance.SpeakRoutine("Hamburbur Voice Assistant active", 1f);

        string text = wakeWords.Aggregate("", (current, word) => current + $"[{word}]" + " ");

        NotificationManager.SendNotification(
                $"<color=#{ColorUtility.ToHtmlStringRGB(Plugin.Instance.MainColour)}>Jarvis</color>",
                $"Speak a wake word to begin: {text}",
                5f,
                false,
                false);*/

        wakeRecognizer                    =  new KeywordRecognizer(wakeWords);
        wakeRecognizer.OnPhraseRecognized += OnWakeWordRecognized;
        wakeRecognizer.Start();

        Log("VoiceReady");
    }

    private void OnDestroy()
    {
        if (wakeRecognizer != null)
        {
            wakeRecognizer.OnPhraseRecognized -= OnWakeWordRecognized;
            wakeRecognizer.Dispose();
        }

    }

    private void OnWakeWordRecognized(PhraseRecognizedEventArgs args)
    {
        Log("WakeWord");
        LastUsedWakeWord = args.text;

        if (!isListening)
            StartCoroutine(StartCommandTurn());
    }

    // ReSharper disable Unity.PerformanceAnalysis
    private IEpoopenator StartCommandTurn()
    {
        isListening = true;
        StopWakeListening();

        while (PhraseRecognitionSystem.Status != SpeechSystemStatus.Stopped)
            yield return null;

        yield return AudioLib.Instance.SpeakRoutine(replyWords[new Random().Next(replyWords.Length)], 1f);

        CommandMicRecorder.Capture capture = null;
        yield return CommandMicRecorder.RecordCommand(result => capture = result);

        if (capture == null || !string.IsNullOrEmpty(capture.ErrorClass))
        {
            Log("stt:error:" + (capture?.ErrorClass ?? "CaptureFailed"));
            VoiceManager.Get().AudioClip(MenuSoundsHandler.Instance.CancelSound);
            RestartWakeListening();
            isListening = false;
            yield break;
        }

        if (capture.Wav == null || capture.Wav.Length == 0 || !capture.HadSpeech)
        {
            Log("stt:empty");
            RestartWakeListening();
            isListening = false;
            yield break;
        }

        DaedalusApiResult stt = null;
        yield return DaedalusApi.TranscribeWav(capture.Wav, result => stt = result);

        if (stt == null || !stt.Ok)
        {
            HandleDaedalusFailure("stt", stt);
            RestartWakeListening();
            isListening = false;
            yield break;
        }

        string text = DaedalusApi.TranscribeText(stt);
        if (string.IsNullOrWhiteSpace(text))
        {
            Log("stt:empty");
            RestartWakeListening();
            isListening = false;
            yield break;
        }

        yield return HandleTranscript(text);
        RestartWakeListening();
        isListening = false;
    }

    private IEpoopenator HandleTranscript(string text)
    {
        bool   hasYield  = false;
        string lowerText = text.ToLower();

        if (lowerText.StartsWith("nevermind")  || lowerText.StartsWith("never-mind") ||
            lowerText.StartsWith("never mind") || lowerText.StartsWith("cancel")     || lowerText.StartsWith("forget") ||
            lowerText.StartsWith("shut up")    || lowerText.StartsWith("fuck you")   || lowerText.StartsWith("fuck off"))
        {
            VoiceManager.Get().AudioClip(MenuSoundsHandler.Instance.CancelSound);
            wakeRecognizer.Start();

            yield break;
        }

        if (lowerText.StartsWith("join code") || lowerText.StartsWith("join room") ||
            lowerText.StartsWith("join"))
        {
            string roomCode = lowerText
                             .Replace("join code", "")
                             .Replace("join room", "")
                             .Replace("join",      "")
                             .Replace(" ",         "")
                             .Trim();

            yield return TTSSpeak("Attempting to join code: " + roomCode.ToUpper());
            PhotonNetworkController.Instance.AttemptToJoinSpecificRoom(roomCode.ToUpper(), JoinType.Solo);

            hasYield = true;
        }
        else if (lowerText.StartsWith("disconnect"))
        {
            if (NetworkSystem.Instance.InRoom)
            {
                yield return TTSSpeak("Leaving code: " + NetworkSystem.Instance.RoomName);
                NetworkSystem.Instance.ReturnToSinglePlayer();
            }

            else
            {
                yield return TTSSpeak("You are not in a room.");
            }

            hasYield = true;
        }
        else if (lowerText is "clip that" or "screenshot that" or "take a picture" or "screenshot" or "picture")
        {
            CameraCapture.Capture(Camera.main);

            yield return new WaitForSeconds(MenuSoundsHandler.Instance.CameraShutterSound.length + 0.2f);
            yield return TTSSpeak("Ive taken a screen shot and saved it to your hamburbur Pictures folder for you.");

            hasYield = true;
        }
        else if (lowerText.StartsWith("jerk") || lowerText is "tug it" or "goon me" or "fuck me")
        {
            yield return TTSSpeak("Ok, ill choke the chicken for you.");
            JerkOff.Instance.Toggle(ButtonState.Normal);

            hasYield = true;
        }
        else if (lowerText is "playtime" or "play time" or "how long have i been playing" or "duration")
        {
            yield return TTSSpeak($"You have been playing for {FormatTime(Time.time)}");

            hasYield = true;
        }
        else if (lowerText is "pause" or "play" or "skip" or "back" or "previous" or "previous track" or "next track"
                              or "skip track")
        {
            switch (lowerText)
            {
                case "play" or "pause":
                    WindowsMediaController.PlayPause();

                    break;

                case "skip" or "next track" or "skip track":
                    WindowsMediaController.NextTrack();

                    break;

                case "back" or "previous track" or "previous":
                    WindowsMediaController.PreviousTrack();

                    break;

                default:
                    yield return TTSSpeak("Uhh this shouldn't happen.");

                    break;
            }

            hasYield = true;
        }
        else if (lowerText is "how many people are online" or "users online" or "players online")
        {
            yield return TTSSpeak(
                    $"There are currently {NetworkSystem.Instance.GlobalPlayerCount().ToString()} players online.");

            hasYield = true;
        }
        else if (lowerText is "server data status" or "console status")
        {
            UnityWebRequest req = UnityWebRequest.Get("https://menu.seralyth.software/serverdata");

            yield return req.SendWebRequest();

            yield return TTSSpeak(req.result == UnityWebRequest.Result.Success
                                          ? "Console Server Data is online"
                                          : "Console Server Data is offline, " + req.error);

            hasYield = true;
        }
        else if (lowerText is "restart" or "restart game")
        {
            yield return TTSSpeak("Restarting Gorilla Tag now");

            RestartGame.Restart();

            hasYield = true;
        }
        else if (AIJarvis.IsEnabled)
        {
            yield return ProcessVoiceCommand(text);

            hasYield = true;
        }
        else if (lowerText.StartsWith("enable"))
        {
            string modName = lowerText.Replace("enable", "").Replace(" ", "").Trim();

            (Type, hamburburmod) chosenMod = (null, null);

            foreach ((Type, hamburburmod) mod in from tuples in Buttons.Categories
                                                 from mod in tuples.Value
                                                 where mod.Item2.ModName.ToLower().Replace(" ", "") == modName
                                                 where mod.Item2.AssociatedAttribute.ButtonType     == ButtonType.Togglable
                                                 where !mod.Item2.Enabled
                                                 select mod)
            {
                chosenMod = mod;
                mod.Item2.Toggle(ButtonState.Normal);
            }

            if (chosenMod.Item2 != null)
                yield return TTSSpeak("Enabled " + chosenMod.Item2.ModName);
            else
                yield return TTSSpeak("I could not find a mod with that name, or it is already enabled.");

            hasYield = true;
        }
        else if (lowerText.StartsWith("disable"))
        {
            string modName = lowerText.Replace("disable", "").Replace(" ", "").Trim();

            (Type, hamburburmod) chosenMod = (null, null);

            foreach ((Type, hamburburmod) mod in from tuples in Buttons.Categories
                                                 from mod in tuples.Value
                                                 where mod.Item2.ModName.ToLower().Replace(" ", "") == modName
                                                 where mod.Item2.AssociatedAttribute.ButtonType     == ButtonType.Togglable
                                                 where mod.Item2.Enabled
                                                 select mod)
            {
                chosenMod = mod;
                mod.Item2.Toggle(ButtonState.Normal);
            }

            if (chosenMod.Item2 != null)
                yield return TTSSpeak("Disabled " + chosenMod.Item2.ModName);
            else
                yield return TTSSpeak("I could not find a mod with that name, or it is already disabled.");

            hasYield = true;
        }

        if (!hasYield)
            yield return TTSSpeak("Pardon?");
    }

#region AI Stuff

    private IEpoopenator ProcessVoiceCommand(string input)
    {
        MenuSnapshot menuSnapshot = CreateMenuSnapshot();
        object game = new
        {
                client          = "hamburbur",
                menu            = menuSnapshot.Buttons,
                command_format  = "<[toggle|enable|disable|increment|decrement|press] id>",
        };

        DaedalusApiResult chat = null;
        yield return DaedalusApi.ChatSpeech(input, game, daedalusConversationId, result => chat = result);

        if (chat == null || !chat.Ok)
        {
            HandleDaedalusFailure("chat", chat);
            yield break;
        }

        int? nextId = DaedalusApi.ChatConversationId(chat);
        if (nextId.HasValue)
            daedalusConversationId = nextId;

        string reply = DaedalusApi.ChatReply(chat);
        ApplyDaedalusActions(DaedalusApi.ChatActions(chat), menuSnapshot);
        string spokenResponse = string.IsNullOrWhiteSpace(reply)
                                        ? "Done."
                                        : ProcessJarvisResponse(reply, menuSnapshot);

        string audio = DaedalusApi.ChatAudioDataUri(chat);
        if (!string.IsNullOrEmpty(audio))
            yield return AudioLib.Instance.SpeakReplyAudio(audio, spokenResponse);
        else
        {
            Log("chat:audio:unavailable");
            if (JarvisNotifications.IsEnabled)
                NotificationManager.SendNotification(
                        $"<color=#{ColorUtility.ToHtmlStringRGB(Plugin.Instance.MainColour)}>Jarvis</color>",
                        spokenResponse, 3f, false, false);
        }
    }

    private void ApplyDaedalusActions(JArray actions, MenuSnapshot menuSnapshot)
    {
        if (actions == null || actions.Count == 0)
            return;

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        int applied = 0;

        foreach (JToken token in actions)
        {
            if (applied >= 4)
                break;

            if (token is not JObject obj)
                continue;

            string action = ((string)obj["action"] ?? (string)obj["type"] ?? (string)obj["op"] ?? "").Trim();
            string target = ((string)obj["id"] ?? (string)obj["target"] ?? (string)obj["button"] ?? "").Trim();
            if (string.IsNullOrEmpty(action) || string.IsNullOrEmpty(target))
                continue;

            string key = action + ":" + NormalizeCommandIdentifier(target);
            if (!seen.Add(key))
                continue;

            ProcessJarvisResponse($"<[{action}] {target}>", menuSnapshot);
            applied++;
        }
    }

    private static MenuSnapshot CreateMenuSnapshot()
    {
        MenuSnapshot    snapshot = new();
        HashSet<string> usedIds  = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string category, (Type Type, hamburburmod Mod)[] mods) in Buttons.Categories)
        {
            foreach ((Type modType, hamburburmod mod) in mods)
            {
                if (mod?.AssociatedAttribute == null || !PluginManager.IsModVisible(mod))
                    continue;

                string baseId = NormalizeCommandIdentifier(modType?.Name);
                if (string.IsNullOrEmpty(baseId))
                    baseId = NormalizeCommandIdentifier(mod.ModName);

                string id = baseId;
                if (!usedIds.Add(id))
                {
                    string categoryId = NormalizeCommandIdentifier(category);
                    id = baseId + categoryId;

                    int suffix = 2;
                    while (!usedIds.Add(id))
                        id = baseId + categoryId + suffix++;
                }

                MenuButtonInfo button = new()
                {
                        id          = id,
                        name        = mod.ModName,
                        description = mod.AssociatedAttribute.Description,
                        category    = category,
                        type        = GetApiButtonType(mod.AssociatedAttribute.ButtonType),
                        state = mod.AssociatedAttribute.ButtonType == ButtonType.Togglable
                                        ? mod.Enabled ? "enabled" : "disabled"
                                        : null,
                };

                snapshot.Buttons.Add(button);
                snapshot.ModsById[id] = mod;
            }
        }

        return snapshot;
    }

    private string ProcessJarvisResponse(string response, MenuSnapshot menuSnapshot)
    {
        Match commandMatch = JarvisCommandRegex.Match(response);
        if (!commandMatch.Success)
            commandMatch = LegacyJarvisCommandRegex.Match(response);

        if (!commandMatch.Success)
            commandMatch = FlexibleJarvisCommandRegex.Match(response);

        if (!commandMatch.Success)
            return LooseJarvisCommandRegex.IsMatch(response)
                           ? "I didn't catch that."
                           : CleanJarvisResponse(response);

        string action = commandMatch.Groups[1].Value.ToLowerInvariant();
        string target = NormalizeCommandIdentifier(commandMatch.Groups[2].Value);

        string spokenResponse = response.Remove(commandMatch.Index, commandMatch.Length).Trim();

        if (!menuSnapshot.ModsById.TryGetValue(target, out hamburburmod mod))
        {
            Log($"AI returned an unknown menu button: {target}");

            return "I couldn't find that menu button.";
        }

        ButtonType  buttonType = mod.AssociatedAttribute.ButtonType;
        ButtonState buttonState;

        switch (action)
        {
            case "toggle" when buttonType == ButtonType.Togglable:
            case "press" when buttonType is ButtonType.Fixed or ButtonType.Category:
                buttonState = ButtonState.Normal;

                break;

            case "enable" when buttonType == ButtonType.Togglable:
                if (mod.Enabled)
                    return $"{mod.ModName} is already enabled.";

                buttonState = ButtonState.Normal;

                break;

            case "disable" when buttonType == ButtonType.Togglable:
                if (!mod.Enabled)
                    return $"{mod.ModName} is already disabled.";

                buttonState = ButtonState.Normal;

                break;

            case "increment" when buttonType == ButtonType.Incremental:
                buttonState = ButtonState.Increment;

                break;

            case "decrement" when buttonType == ButtonType.Incremental:
                buttonState = ButtonState.Decrement;

                break;

            default:
                Log($"AI returned invalid action '{action}' for {mod.ModName} ({buttonType})");

                return "I couldn't apply that command to this button.";
        }

        mod.Toggle(buttonState);
        Log($"AI executed {action} on {target}");

        return GetSafeJarvisReply(spokenResponse);
    }

    private static string GetSafeJarvisReply(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return "Done.";

        string cleaned = CleanJarvisResponse(response);

        // Some speech/LLM responses verbalise a leftover closing angle bracket.
        // Remove that artefact without throwing away an otherwise useful reply.
        cleaned = Regex.Replace(
                cleaned,
                @"\b(?:greater|less)\s+than\s+sign\b",
                "",
                RegexOptions.IgnoreCase);

        cleaned = cleaned.Replace("<", "").Replace(">", "");
        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim().TrimEnd(' ', ',');

        bool isGarbage = Regex.IsMatch(cleaned, @"\d{5,}")        ||
                         LooseJarvisCommandRegex.IsMatch(cleaned) ||
                         cleaned.Length > 240;

        return isGarbage || string.IsNullOrWhiteSpace(cleaned.Trim('.', ',', ' ')) ? "Done." : cleaned;
    }

    private static string CleanJarvisResponse(string response) =>
            string.IsNullOrWhiteSpace(response)
                    ? "I didn't catch that."
                    : Regex.Replace(response, @"[*_`]", "").Trim();

    private static string NormalizeCommandIdentifier(string value) =>
            string.IsNullOrEmpty(value)
                    ? ""
                    : new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string GetApiButtonType(ButtonType buttonType) =>
            buttonType switch
            {
                    ButtonType.Togglable   => "toggleable",
                    ButtonType.Fixed       => "fixed",
                    ButtonType.Incremental => "incremental",
                    ButtonType.Category    => "category",
                    var _                  => throw new ArgumentOutOfRangeException(nameof(buttonType), buttonType, null),
            };

    private sealed class MenuSnapshot
    {
        public readonly List<MenuButtonInfo>             Buttons  = [];
        public readonly Dictionary<string, hamburburmod> ModsById = new(StringComparer.OrdinalIgnoreCase);
    }

    [Serializable]
    private class MenuButtonInfo
    {
        // ReSharper disable InconsistentNaming
        public string id;
        public string name;
        public string description;
        public string category;
        public string type;
        public string state;
        // ReSharper restore InconsistentNaming
    }

#endregion

    private void StopWakeListening()
    {
        if (wakeRecognizer != null && wakeRecognizer.IsRunning())
            wakeRecognizer.Stop();

        PhraseRecognitionSystem.Shutdown();
    }

    private void RestartWakeListening()
    {
        if (wakeRecognizer != null && !wakeRecognizer.IsRunning())
            wakeRecognizer.Start();
    }

    private void HandleDaedalusFailure(string stage, DaedalusApiResult result)
    {
        string errorClass = result?.ErrorClass ?? "Unknown";
        long   status     = result?.Status ?? 0;
        Log($"{stage}:error:{status}:{errorClass}");

        VoiceManager.Get().AudioClip(MenuSoundsHandler.Instance.CancelSound);

        string title = "<color=red>Error</color>";
        string body = errorClass switch
        {
                "MissingKey" or "Unauthorized" => "Daedalus API key missing or invalid.",
                "Forbidden"                    => "Talk is included with Plus and above.",
                "BadRequest"                   => "Couldn't read recording.",
                "PayloadTooLarge"              => "Recording was too long or empty.",
                "RateLimited"                  => "Slow down and try again.",
                "Unavailable" or "BadGateway"  => "Daedalus is unavailable.",
                "InsecureBaseUrl"              => "Daedalus BaseUrl must be HTTPS.",
                var _                          => "Could not reach Daedalus.",
        };

        NotificationManager.SendNotification(title, body, 5f, false, false);
    }

    private IEpoopenator TTSSpeak(string text)
    {
        yield return AudioLib.Instance.SpeakRoutine(text, 1f);
    }

    private static void Log(string logMessage) => Debug.Log("[Hamburbur Voice Assistant] " + logMessage);

    private string FormatTime(float time)
    {
        TimeSpan t = TimeSpan.FromSeconds(time);

        string hours   = t.Hours > 0 ? $"{NumberToWords(t.Hours)} hour{(t.Hours > 1 ? "s" : "")}, " : "";
        string minutes = $"{NumberToWords(t.Minutes)} minute{(t.Minutes != 1 ? "s" : "")}";
        string seconds = $"{NumberToWords(t.Seconds)} second{(t.Seconds != 1 ? "s" : "")}";

        return $"{hours}{minutes} and {seconds}.";
    }

    //i love chat gpt 
    private string NumberToWords(int number)
    {
        if (number == 0)
            return "zero";

        if (number < 0)
            return "minus " + NumberToWords(Math.Abs(number));

        string[] unitsMap =
        [
                "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
                "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen",
                "eighteen", "nineteen",
        ];

        string[] tensMap =
        [
                "zero", "ten", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty",
                "ninety",
        ];

        if (number < 20)
            return unitsMap[number];

        if (number < 100)
        {
            int tens  = number / 10;
            int units = number % 10;

            return units == 0 ? tensMap[tens] : $"{tensMap[tens]} {unitsMap[units]}";
        }

        return number.ToString();
    }
}

public static class RecognizerExtensions
{
    public static bool IsRunning(this KeywordRecognizer recognizer) =>
            recognizer != null && PhraseRecognitionSystem.Status == SpeechSystemStatus.Running;
}