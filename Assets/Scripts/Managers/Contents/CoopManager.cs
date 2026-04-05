using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public class CoopManager
{
    private const string DiscordApiBaseUrl = "https://discord.com/api/v10";
    private const int MaxMembers = 5;
    private const string SignalPrefix = "[COOP_SIGNAL]";
    private const int ProcessedSignalCacheLimit = 256;

    private readonly Dictionary<string, CoopMember> _members = new();
    private readonly HashSet<string> _processedSignalMessageIds = new();
    private readonly Queue<string> _processedSignalMessageOrder = new();

    private DiscordCoopConfig _config = DiscordCoopConfig.Default;
    private CoopSessionState _sessionState = CoopSessionState.Idle;
    private string _voiceChannelId;
    private string _signalChannelId;
    private string _voiceInviteUrl;
    private string _lastSignalMessageId;
    private string _lastError;
    private bool _pollInFlight;
    private float _nextSignalPollTime;

    public event Action<CoopSessionState> OnSessionStateChanged;
    public event Action<CoopSignal> OnSignalReceived;

    public CoopSessionState SessionState => _sessionState;
    public int MemberCount => _members.Count;
    public IReadOnlyDictionary<string, CoopMember> Members => _members;
    public string VoiceInviteUrl => _voiceInviteUrl;
    public string LastError => _lastError;

    public void Init()
    {
        Clear();
    }

    public void Clear()
    {
        _members.Clear();
        _processedSignalMessageIds.Clear();
        _processedSignalMessageOrder.Clear();
        _voiceChannelId = null;
        _signalChannelId = null;
        _voiceInviteUrl = null;
        _lastSignalMessageId = null;
        _lastError = null;
        _pollInFlight = false;
        _nextSignalPollTime = 0f;
        SetState(CoopSessionState.Idle);
    }

    public void OnUpdate()
    {
        if (_sessionState != CoopSessionState.Active || !_config.EnableSignalPolling)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_signalChannelId) || _pollInFlight)
        {
            return;
        }

        if (Time.unscaledTime < _nextSignalPollTime)
        {
            return;
        }

        _nextSignalPollTime = Time.unscaledTime + Mathf.Max(0.05f, _config.SignalPollIntervalSeconds);
        _ = PollSignalChannelAsync();
    }

    public void Configure(DiscordCoopConfig config)
    {
        _config = config;
    }

    public async Task<bool> CreateHostSessionAsync(string hostDiscordUserId, string hostDisplayName)
    {
        if (_sessionState == CoopSessionState.Active)
        {
            _lastError = "Coop session is already active.";
            return false;
        }

        if (!ValidateDiscordConfig())
        {
            return false;
        }

        SetState(CoopSessionState.Creating);

        string suffix = DateTime.UtcNow.ToString("HHmmss");
        string voiceChannelName = string.IsNullOrWhiteSpace(_config.VoiceChannelName)
            ? $"trashman-coop-{suffix}"
            : _config.VoiceChannelName;
        string signalChannelName = string.IsNullOrWhiteSpace(_config.SignalChannelName)
            ? $"trashman-signal-{suffix}"
            : _config.SignalChannelName;

        string voiceChannelId = await CreateChannelAsync(voiceChannelName, 2, _config.VoiceCategoryId, MaxMembers);
        if (string.IsNullOrWhiteSpace(voiceChannelId))
        {
            SetState(CoopSessionState.Error);
            return false;
        }

        string signalChannelId = await CreateChannelAsync(signalChannelName, 0, _config.SignalCategoryId, 0);
        if (string.IsNullOrWhiteSpace(signalChannelId))
        {
            SetState(CoopSessionState.Error);
            return false;
        }

        string inviteCode = await CreateChannelInviteCodeAsync(voiceChannelId);
        if (string.IsNullOrWhiteSpace(inviteCode))
        {
            SetState(CoopSessionState.Error);
            return false;
        }

        _voiceChannelId = voiceChannelId;
        _signalChannelId = signalChannelId;
        _voiceInviteUrl = $"https://discord.gg/{inviteCode}";
        _lastSignalMessageId = null;
        _processedSignalMessageIds.Clear();
        _processedSignalMessageOrder.Clear();
        _members.Clear();

        TryRegisterMember(hostDiscordUserId, hostDisplayName, isHost: true);
        SetState(CoopSessionState.Active);
        return true;
    }

    public bool TryRegisterMember(string discordUserId, string displayName, bool isHost = false)
    {
        if (string.IsNullOrWhiteSpace(discordUserId))
        {
            return false;
        }

        if (_members.ContainsKey(discordUserId))
        {
            CoopMember existing = _members[discordUserId];
            existing.DisplayName = string.IsNullOrWhiteSpace(displayName) ? existing.DisplayName : displayName;
            existing.IsHost = existing.IsHost || isHost;
            _members[discordUserId] = existing;
            return true;
        }

        if (_members.Count >= MaxMembers)
        {
            _lastError = "Maximum coop member count reached (5).";
            return false;
        }

        _members[discordUserId] = new CoopMember
        {
            DiscordUserId = discordUserId,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? discordUserId : displayName,
            IsHost = isHost,
            JoinedAtUtc = DateTime.UtcNow,
        };

        return true;
    }

    public bool RemoveMember(string discordUserId)
    {
        if (string.IsNullOrWhiteSpace(discordUserId))
        {
            return false;
        }

        return _members.Remove(discordUserId);
    }

    public async Task<bool> SendRealtimeSignalAsync(string senderDiscordUserId, string signalType, string payload)
    {
        if (_sessionState != CoopSessionState.Active)
        {
            _lastError = "Coop session is not active.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(_signalChannelId))
        {
            _lastError = "Signal channel is not ready.";
            return false;
        }

        CoopSignalEnvelope envelope = new CoopSignalEnvelope
        {
            sender = senderDiscordUserId,
            type = signalType,
            payload = payload,
            timestampUtc = DateTime.UtcNow.ToString("o"),
        };

        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonUtility.ToJson(envelope)));
        DiscordCreateMessageRequest request = new DiscordCreateMessageRequest
        {
            content = $"{SignalPrefix}{encoded}",
        };

        DiscordHttpResult result = await SendDiscordRequestAsync($"/channels/{_signalChannelId}/messages", "POST", JsonUtility.ToJson(request));
        if (!result.Success)
        {
            _lastError = result.Error;
            return false;
        }

        return true;
    }

    public async Task EndSessionAsync()
    {
        string voiceChannelId = _voiceChannelId;
        string signalChannelId = _signalChannelId;

        Clear();

        if (!_config.AutoDeleteChannelsOnEnd)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(voiceChannelId))
        {
            _ = SendDiscordRequestAsync($"/channels/{voiceChannelId}", "DELETE", null);
        }

        if (!string.IsNullOrWhiteSpace(signalChannelId))
        {
            _ = SendDiscordRequestAsync($"/channels/{signalChannelId}", "DELETE", null);
        }

        await Task.CompletedTask;
    }

    private async Task PollSignalChannelAsync()
    {
        _pollInFlight = true;
        try
        {
            string route = string.IsNullOrWhiteSpace(_lastSignalMessageId)
                ? $"/channels/{_signalChannelId}/messages?limit=20"
                : $"/channels/{_signalChannelId}/messages?after={_lastSignalMessageId}&limit=20";
            DiscordHttpResult result = await SendDiscordRequestAsync(route, "GET", null);
            if (!result.Success || string.IsNullOrWhiteSpace(result.Body))
            {
                return;
            }

            DiscordMessageDto[] messages = DeserializeDiscordMessages(result.Body);
            if (messages == null || messages.Length == 0)
            {
                return;
            }

            Array.Sort(messages, CompareDiscordMessageIdAscending);
            for (int i = 0; i < messages.Length; i++)
            {
                DiscordMessageDto message = messages[i];
                if (message == null || string.IsNullOrWhiteSpace(message.id))
                {
                    continue;
                }

                _lastSignalMessageId = message.id;
                if (_processedSignalMessageIds.Contains(message.id))
                {
                    continue;
                }

                CacheProcessedSignalId(message.id);

                if (string.IsNullOrWhiteSpace(message.content) || !message.content.StartsWith(SignalPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                string encoded = message.content.Substring(SignalPrefix.Length);
                if (string.IsNullOrWhiteSpace(encoded))
                {
                    continue;
                }

                string json;
                try
                {
                    json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                }
                catch
                {
                    continue;
                }

                CoopSignalEnvelope envelope = JsonUtility.FromJson<CoopSignalEnvelope>(json);
                if (envelope == null)
                {
                    continue;
                }

                CoopSignal signal = new CoopSignal
                {
                    SenderDiscordUserId = envelope.sender,
                    SignalType = envelope.type,
                    Payload = envelope.payload,
                    TimestampUtc = envelope.timestampUtc,
                };
                OnSignalReceived?.Invoke(signal);
            }
        }
        finally
        {
            _pollInFlight = false;
        }
    }

    private bool ValidateDiscordConfig()
    {
        if (string.IsNullOrWhiteSpace(_config.BotToken))
        {
            _lastError = "Discord bot token is missing.";
            SetState(CoopSessionState.Error);
            return false;
        }

        if (string.IsNullOrWhiteSpace(_config.GuildId))
        {
            _lastError = "Discord guild id is missing.";
            SetState(CoopSessionState.Error);
            return false;
        }

        return true;
    }

    private async Task<string> CreateChannelAsync(string name, int channelType, string categoryId, int userLimit)
    {
        DiscordCreateChannelRequest request = new DiscordCreateChannelRequest
        {
            name = string.IsNullOrWhiteSpace(name) ? "trashman-coop" : name,
            type = channelType,
            parent_id = string.IsNullOrWhiteSpace(categoryId) ? null : categoryId,
            user_limit = userLimit,
        };

        DiscordHttpResult result = await SendDiscordRequestAsync($"/guilds/{_config.GuildId}/channels", "POST", JsonUtility.ToJson(request));
        if (!result.Success)
        {
            _lastError = result.Error;
            return null;
        }

        DiscordChannelDto channel = JsonUtility.FromJson<DiscordChannelDto>(result.Body);
        return channel?.id;
    }

    private async Task<string> CreateChannelInviteCodeAsync(string channelId)
    {
        DiscordCreateInviteRequest request = new DiscordCreateInviteRequest
        {
            max_age = 0,
            max_uses = 0,
            temporary = false,
            unique = true,
        };

        DiscordHttpResult result = await SendDiscordRequestAsync($"/channels/{channelId}/invites", "POST", JsonUtility.ToJson(request));
        if (!result.Success)
        {
            _lastError = result.Error;
            return null;
        }

        DiscordInviteDto invite = JsonUtility.FromJson<DiscordInviteDto>(result.Body);
        return invite?.code;
    }

    private async Task<DiscordHttpResult> SendDiscordRequestAsync(string route, string method, string jsonBody)
    {
        string url = $"{DiscordApiBaseUrl}{route}";
        using UnityWebRequest request = new UnityWebRequest(url, method);

        if (!string.IsNullOrWhiteSpace(jsonBody))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
            request.SetRequestHeader("Content-Type", "application/json");
        }

        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Authorization", $"Bot {_config.BotToken}");

        UnityWebRequestAsyncOperation operation = request.SendWebRequest();
        await AwaitRequest(operation);

        if (request.result != UnityWebRequest.Result.Success)
        {
            return new DiscordHttpResult
            {
                Success = false,
                Body = request.downloadHandler != null ? request.downloadHandler.text : null,
                Error = request.error,
            };
        }

        return new DiscordHttpResult
        {
            Success = true,
            Body = request.downloadHandler != null ? request.downloadHandler.text : null,
            Error = null,
        };
    }

    private static Task AwaitRequest(AsyncOperation operation)
    {
        TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
        operation.completed += _ => tcs.TrySetResult(true);
        return tcs.Task;
    }

    private static int CompareDiscordMessageIdAscending(DiscordMessageDto a, DiscordMessageDto b)
    {
        ulong aId = ParseSnowflake(a != null ? a.id : null);
        ulong bId = ParseSnowflake(b != null ? b.id : null);
        return aId.CompareTo(bId);
    }

    private static ulong ParseSnowflake(string value)
    {
        if (ulong.TryParse(value, out ulong parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static DiscordMessageDto[] DeserializeDiscordMessages(string jsonArray)
    {
        if (string.IsNullOrWhiteSpace(jsonArray) || jsonArray == "[]")
        {
            return Array.Empty<DiscordMessageDto>();
        }

        string wrapped = "{\"items\":" + jsonArray + "}";
        DiscordMessageArrayWrapper wrapper = JsonUtility.FromJson<DiscordMessageArrayWrapper>(wrapped);
        return wrapper != null && wrapper.items != null ? wrapper.items : Array.Empty<DiscordMessageDto>();
    }

    private void CacheProcessedSignalId(string messageId)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return;
        }

        if (_processedSignalMessageIds.Contains(messageId))
        {
            return;
        }

        _processedSignalMessageIds.Add(messageId);
        _processedSignalMessageOrder.Enqueue(messageId);

        while (_processedSignalMessageOrder.Count > ProcessedSignalCacheLimit)
        {
            string oldest = _processedSignalMessageOrder.Dequeue();
            _processedSignalMessageIds.Remove(oldest);
        }
    }

    private void SetState(CoopSessionState next)
    {
        if (_sessionState == next)
        {
            return;
        }

        _sessionState = next;
        OnSessionStateChanged?.Invoke(_sessionState);
    }

    [Serializable]
    private class DiscordCreateChannelRequest
    {
        public string name;
        public int type;
        public string parent_id;
        public int user_limit;
    }

    [Serializable]
    private class DiscordCreateInviteRequest
    {
        public int max_age;
        public int max_uses;
        public bool temporary;
        public bool unique;
    }

    [Serializable]
    private class DiscordCreateMessageRequest
    {
        public string content;
    }

    [Serializable]
    private class DiscordChannelDto
    {
        public string id = string.Empty;
    }

    [Serializable]
    private class DiscordInviteDto
    {
        public string code = string.Empty;
    }

    [Serializable]
    private class DiscordMessageDto
    {
        public string id = string.Empty;
        public string content = string.Empty;
    }

    [Serializable]
    private class DiscordMessageArrayWrapper
    {
        public DiscordMessageDto[] items = Array.Empty<DiscordMessageDto>();
    }

    [Serializable]
    private class CoopSignalEnvelope
    {
        public string sender;
        public string type;
        public string payload;
        public string timestampUtc;
    }

    private struct DiscordHttpResult
    {
        public bool Success;
        public string Body;
        public string Error;
    }
}

[Serializable]
public struct DiscordCoopConfig
{
    public string BotToken;
    public string GuildId;
    public string VoiceCategoryId;
    public string SignalCategoryId;
    public string VoiceChannelName;
    public string SignalChannelName;
    public float SignalPollIntervalSeconds;
    public bool EnableSignalPolling;
    public bool AutoDeleteChannelsOnEnd;

    public static DiscordCoopConfig Default => new DiscordCoopConfig
    {
        BotToken = string.Empty,
        GuildId = string.Empty,
        VoiceCategoryId = string.Empty,
        SignalCategoryId = string.Empty,
        VoiceChannelName = "",
        SignalChannelName = "",
        SignalPollIntervalSeconds = 0.2f,
        EnableSignalPolling = true,
        AutoDeleteChannelsOnEnd = false,
    };
}

public enum CoopSessionState
{
    Idle = 0,
    Creating = 1,
    Active = 2,
    Error = 3,
}

[Serializable]
public struct CoopMember
{
    public string DiscordUserId;
    public string DisplayName;
    public bool IsHost;
    public DateTime JoinedAtUtc;
}

[Serializable]
public struct CoopSignal
{
    public string SenderDiscordUserId;
    public string SignalType;
    public string Payload;
    public string TimestampUtc;
}
