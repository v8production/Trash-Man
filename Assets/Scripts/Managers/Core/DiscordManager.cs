using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;

public readonly struct DiscordLobbyUser
{
    public DiscordLobbyUser(string userId, string displayName, bool isLocalUser)
    {
        UserId = userId;
        DisplayName = displayName;
        IsLocalUser = isLocalUser;
    }

    public string UserId { get; }
    public string DisplayName { get; }
    public bool IsLocalUser { get; }
}

public class DiscordManager
{
    private const string LegacyDefaultScopes = "openid sdk.social_layer";
    private const string ClientTypeName = "Discord.Sdk.Client, Discord.Sdk";
    private const string AuthArgsTypeName = "Discord.Sdk.AuthorizationArgs, Discord.Sdk";
    private const string AuthTokenTypeName = "Discord.Sdk.AuthorizationTokenType, Discord.Sdk";
    private const float SpeakingActiveTimeoutSeconds = 0.9f;

    private readonly Dictionary<string, DiscordLobbyUser> _members = new();
    private readonly Dictionary<string, bool> _voiceChatActiveByUserId = new();
    private readonly Dictionary<string, float> _lastSpeakingSignalTimeByUserId = new();
    private readonly List<string> _speakingTimeoutBuffer = new();
    private int _inviteSerial;

    private object _client;
    private Type _clientType;
    private object _activeVoiceCall;
    private Delegate _statusChangedCallback;
    private Delegate _createOrJoinLobbyCallback;
    private Delegate _sessionCreateOrJoinLobbyCallback;
    private Delegate _leaveLobbyCallback;
    private Delegate _lobbyUpdatedCallback;
    private Delegate _lobbyMemberAddedCallback;
    private Delegate _lobbyMemberRemovedCallback;
    private Delegate _callSpeakingStatusChangedCallback;
    private Delegate _callVoiceStateChangedCallback;
    private string _pendingCodeVerifier = string.Empty;
    private string _pendingVoiceLobbySecret = string.Empty;
    private string _pendingSessionLobbySecret = string.Empty;
    private string _activeVoiceLobbySecret = string.Empty;
    private ulong _applicationId;
    private ulong _activeVoiceLobbyId;
    private ulong _pendingLeaveLobbyId;
    private string _scopes = LegacyDefaultScopes;

    private readonly Dictionary<string, ulong> _sessionLobbyIdBySecret = new();
    private Action<bool, ulong, string> _sessionLobbyJoinCompletion;

    public event Action<DiscordLobbyUser> OnLobbyUserJoined;
    public event Action<string> OnInviteRequested;
    public event Action<string> OnInviteLinkCreated;
    public event Action<string> OnLocalDisplayNameChanged;
    public event Action OnAuthStateChanged;
    public event Action<string, bool> OnLobbyUserVoiceChatStateChanged;
    public event Action<ulong> OnSessionLobbyUpdated;
    public event Action<ulong, ulong> OnSessionLobbyMemberAdded;
    public event Action<ulong, ulong> OnSessionLobbyMemberRemoved;

    public bool IsLinked { get; private set; }
    public bool IsConnecting { get; private set; }
    public string LastAuthError { get; private set; }
    public string LocalUserId { get; private set; } = "local-user";
    public string LocalDisplayName { get; private set; } = "Player";

    public void Init()
    {
        LogVoice("Init called. Ensuring Discord client instance.");
        _ = EnsureClient();
    }

    public void OnUpdate()
    {
        if (_lastSpeakingSignalTimeByUserId.Count == 0)
            return;

        float now = Time.unscaledTime;
        _speakingTimeoutBuffer.Clear();

        foreach (KeyValuePair<string, float> pair in _lastSpeakingSignalTimeByUserId)
        {
            if (now - pair.Value >= SpeakingActiveTimeoutSeconds)
                _speakingTimeoutBuffer.Add(pair.Key);
        }

        for (int i = 0; i < _speakingTimeoutBuffer.Count; i++)
        {
            string userId = _speakingTimeoutBuffer[i];
            _lastSpeakingSignalTimeByUserId.Remove(userId);

            if (IsLobbyUserVoiceChatActive(userId))
            {
                LogVoice($"Speaking timeout elapsed. userId={userId}, forcing speaking=false");
                SetLobbyUserVoiceChatActive(userId, false);
            }
        }
    }

    public void Connect(ulong applicationId, string scopes)
    {
        LogVoice($"Connect requested. appId={applicationId}, scopes={(string.IsNullOrWhiteSpace(scopes) ? "<default>" : scopes)}, linked={IsLinked}, connecting={IsConnecting}");

        if (applicationId == 0)
        {
            SetConnectFailed("Discord connect failed: invalid application id.");
            return;
        }

        if (IsLinked || IsConnecting)
        {
            LogVoice($"Connect request ignored. linked={IsLinked}, connecting={IsConnecting}");
            return;
        }

        if (!EnsureClient())
        {
            LogVoice("Connect aborted because Discord client is unavailable.");
            return;
        }

        _applicationId = applicationId;
        _scopes = ResolveScopes(scopes);
        _pendingCodeVerifier = string.Empty;
        IsConnecting = true;
        LastAuthError = null;
        OnAuthStateChanged?.Invoke();

        object verifier = null;
        object challenge = null;
        object authArgs = null;

        try
        {
            LogVoice("Starting Discord authorization flow.");
            verifier = InvokeInstance(_client, "CreateAuthorizationCodeVerifier");
            _pendingCodeVerifier = InvokeInstance(verifier, "Verifier") as string;
            challenge = InvokeInstance(verifier, "Challenge");

            Type authArgsType = Type.GetType(AuthArgsTypeName, false);
            if (authArgsType == null)
            {
                SetConnectFailed("Discord connect failed: AuthorizationArgs type missing.");
                return;
            }

            authArgs = Activator.CreateInstance(authArgsType);
            InvokeInstance(authArgs, "SetClientId", _applicationId);
            InvokeInstance(authArgs, "SetScopes", _scopes);
            InvokeInstance(authArgs, "SetCodeChallenge", challenge);

            Delegate authorizeCallback = CreateClientCallback("AuthorizationCallback", HandleAuthorizeResultBridge);
            InvokeInstance(_client, "Authorize", authArgs, authorizeCallback);
            LogVoice("Authorize request dispatched to Discord SDK.");
        }
        catch (Exception e)
        {
            SetConnectFailed($"Discord connect failed during authorize setup: {e.Message}");
        }
        finally
        {
            DisposeIfNeeded(authArgs);
            DisposeIfNeeded(challenge);
            DisposeIfNeeded(verifier);
        }
    }

    public void Clear()
    {
        EndActiveLobbyVoice();
        _members.Clear();
        _voiceChatActiveByUserId.Clear();
        _lastSpeakingSignalTimeByUserId.Clear();
        _speakingTimeoutBuffer.Clear();
        _sessionLobbyIdBySecret.Clear();
        _pendingSessionLobbySecret = string.Empty;
        _sessionLobbyJoinCompletion = null;
        _pendingLeaveLobbyId = 0;
        _inviteSerial = 0;
        IsConnecting = false;
        LastAuthError = null;
        _pendingCodeVerifier = string.Empty;
        OnAuthStateChanged?.Invoke();
    }

    public void LinkLocalAccount(string userId, string displayName)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = "Player";

        IsLinked = true;
        LocalUserId = userId;
        LocalDisplayName = displayName;

        DiscordLobbyUser localUser = new(LocalUserId, LocalDisplayName, true);
        _members[LocalUserId] = localUser;
        _voiceChatActiveByUserId[LocalUserId] = false;

        LogVoice($"Discord link success. userId={LocalUserId}, username={LocalDisplayName}");
        OnAuthStateChanged?.Invoke();
        OnLocalDisplayNameChanged?.Invoke(LocalDisplayName);
        OnLobbyUserJoined?.Invoke(localUser);
    }

    public void RequestFriendInvite(string friendDisplayName)
    {
        if (string.IsNullOrWhiteSpace(friendDisplayName))
            friendDisplayName = $"Friend{_inviteSerial + 1}";

        _inviteSerial++;
        string generatedUserId = $"friend-{_inviteSerial:D3}";

        OnInviteRequested?.Invoke(friendDisplayName);
        NotifyLobbyUserJoined(generatedUserId, friendDisplayName, false);
    }

    public void RequestLobbyInvite(string joinCode)
    {
        string normalizedCode = LobbySessionManager.NormalizeJoinCode(joinCode);
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            Debug.LogWarning("[Lobby] Invite skipped: invalid join code.");
            return;
        }

        string deepLink = $"trashman://join?code={normalizedCode}";
        GUIUtility.systemCopyBuffer = deepLink;

        try
        {
            if (EnsureClient())
            {
                // Best-effort reflection call. Different SDK versions expose invite popups under different names.
                MethodInfo openInvitePopup = _client.GetType().GetMethod("OpenInvitePopup", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                openInvitePopup?.Invoke(_client, null);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Lobby] Discord invite popup open failed: {e.Message}");
        }

        OnInviteRequested?.Invoke(normalizedCode);
        OnInviteLinkCreated?.Invoke(deepLink);
        Debug.Log($"[Lobby] Invite deep-link copied: {deepLink}");
    }

    public bool CreateOrJoinSessionLobby(string lobbySecret, Dictionary<string, string> lobbyMetadata, Dictionary<string, string> memberMetadata, bool writeMetadata, Action<bool, ulong, string> onCompleted)
    {
        if (string.IsNullOrWhiteSpace(lobbySecret))
            return false;

        if (!EnsureClient())
            return false;

        _pendingSessionLobbySecret = lobbySecret;
        _sessionLobbyJoinCompletion = onCompleted;

        try
        {
            if (writeMetadata)
            {
                _sessionCreateOrJoinLobbyCallback = CreateMethodCallback(_client, "CreateOrJoinLobbyWithMetadata", 4, 3, HandleSessionCreateOrJoinLobbyBridge);
                InvokeInstance(_client,
                    "CreateOrJoinLobbyWithMetadata",
                    lobbySecret,
                    lobbyMetadata ?? new Dictionary<string, string>(),
                    memberMetadata ?? new Dictionary<string, string>(),
                    _sessionCreateOrJoinLobbyCallback);
                LogVoice($"Session lobby CreateOrJoinLobbyWithMetadata requested. secret({SummarizeSecret(lobbySecret)})");
            }
            else
            {
                _sessionCreateOrJoinLobbyCallback = CreateMethodCallback(_client, "CreateOrJoinLobby", 2, 1, HandleSessionCreateOrJoinLobbyBridge);
                InvokeInstance(_client, "CreateOrJoinLobby", lobbySecret, _sessionCreateOrJoinLobbyCallback);
                LogVoice($"Session lobby CreateOrJoinLobby requested. secret({SummarizeSecret(lobbySecret)})");
            }

            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Lobby] Session lobby join request failed: {e.Message}");
            _sessionLobbyJoinCompletion?.Invoke(false, 0, e.Message);
            _sessionLobbyJoinCompletion = null;
            return false;
        }
    }

    public bool TryGetSessionLobbyMetadata(ulong lobbyId, out Dictionary<string, string> metadata)
    {
        metadata = null;
        if (lobbyId == 0 || !EnsureClient())
            return false;

        object lobbyHandle = null;
        object metadataObject = null;

        try
        {
            lobbyHandle = InvokeInstance(_client, "GetLobbyHandle", lobbyId);
            if (lobbyHandle == null)
                return false;

            metadataObject = InvokeInstance(lobbyHandle, "Metadata");
            return TryConvertMetadataObject(metadataObject, out metadata);
        }
        catch
        {
            return false;
        }
        finally
        {
            DisposeIfNeeded(metadataObject);
            DisposeIfNeeded(lobbyHandle);
        }
    }

    public bool TryGetSessionLobbyMemberIds(ulong lobbyId, out ulong[] memberIds)
    {
        memberIds = Array.Empty<ulong>();
        if (lobbyId == 0 || !EnsureClient())
            return false;

        object lobbyHandle = null;

        try
        {
            lobbyHandle = InvokeInstance(_client, "GetLobbyHandle", lobbyId);
            if (lobbyHandle == null)
                return false;

            object ids = InvokeInstance(lobbyHandle, "LobbyMemberIds");
            if (ids is ulong[] typed)
            {
                memberIds = typed;
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            DisposeIfNeeded(lobbyHandle);
        }
    }

    public bool TryGetSessionLobbyIdBySecret(string lobbySecret, out ulong lobbyId)
    {
        if (string.IsNullOrWhiteSpace(lobbySecret))
        {
            lobbyId = 0;
            return false;
        }

        return _sessionLobbyIdBySecret.TryGetValue(lobbySecret, out lobbyId);
    }

    public void LeaveSessionLobby(ulong lobbyId)
    {
        if (lobbyId == 0 || !EnsureClient())
            return;

        _pendingLeaveLobbyId = lobbyId;

        try
        {
            _leaveLobbyCallback = CreateMethodCallback(_client, "LeaveLobby", 2, 1, HandleLeaveSessionLobbyBridge);
            InvokeInstance(_client, "LeaveLobby", lobbyId, _leaveLobbyCallback);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Lobby] LeaveSessionLobby invoke failed: {e.Message}");
        }
    }

    public void NotifyLobbyUserJoined(string userId, string displayName, bool isLocalUser)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = isLocalUser ? LocalDisplayName : "Friend";

        DiscordLobbyUser user = new(userId, displayName, isLocalUser);
        _members[userId] = user;
        if (!_voiceChatActiveByUserId.ContainsKey(userId))
            _voiceChatActiveByUserId[userId] = false;
        OnLobbyUserJoined?.Invoke(user);
    }

    public bool IsLobbyUserVoiceChatActive(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        return _voiceChatActiveByUserId.TryGetValue(userId, out bool isActive) && isActive;
    }

    public void SetLobbyUserVoiceChatActive(string userId, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        if (isActive)
            _lastSpeakingSignalTimeByUserId[userId] = Time.unscaledTime;
        else
            _lastSpeakingSignalTimeByUserId.Remove(userId);

        if (_voiceChatActiveByUserId.TryGetValue(userId, out bool previousState) && previousState == isActive)
            return;

        _voiceChatActiveByUserId[userId] = isActive;
        LogVoice($"Speaking state changed. userId={userId}, speaking={isActive}");
        Managers.LobbySession.SetRangerNicknameVoiceActive(userId, isActive);
        OnLobbyUserVoiceChatStateChanged?.Invoke(userId, isActive);
    }

    public void EnsureLobbyVoiceConnected(string lobbySecret)
    {
        LogVoice($"EnsureLobbyVoiceConnected called. linked={IsLinked}, hasSecret={!string.IsNullOrWhiteSpace(lobbySecret)}, activeLobbyId={_activeVoiceLobbyId}");

        if (string.IsNullOrWhiteSpace(lobbySecret) || !IsLinked)
        {
            LogVoice($"EnsureLobbyVoiceConnected skipped. reason={(string.IsNullOrWhiteSpace(lobbySecret) ? "empty-secret" : "not-linked")}");
            return;
        }

        if (!EnsureClient())
        {
            LogVoice("EnsureLobbyVoiceConnected aborted: Discord client unavailable.");
            return;
        }

        if (_activeVoiceCall != null && string.Equals(_activeVoiceLobbySecret, lobbySecret, StringComparison.Ordinal))
        {
            LogVoice($"Lobby voice already connected for secret({SummarizeSecret(lobbySecret)}). Reuse existing call.");
            return;
        }

        if (_activeVoiceCall != null && !string.Equals(_activeVoiceLobbySecret, lobbySecret, StringComparison.Ordinal))
        {
            LogVoice($"Switching lobby voice from secret({SummarizeSecret(_activeVoiceLobbySecret)}) to secret({SummarizeSecret(lobbySecret)}).");
            EndActiveLobbyVoice();
        }

        _pendingVoiceLobbySecret = lobbySecret;

        try
        {
            _createOrJoinLobbyCallback = CreateMethodCallback(_client, "CreateOrJoinLobby", 2, 1, HandleCreateOrJoinLobbyBridge);
            InvokeInstance(_client, "CreateOrJoinLobby", lobbySecret, _createOrJoinLobbyCallback);
            LogVoice($"CreateOrJoinLobby requested. secret({SummarizeSecret(lobbySecret)})");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Discord lobby voice connect failed while joining lobby: {e.Message}");
        }
    }

    public void EndActiveLobbyVoice()
    {
        LogVoice($"EndActiveLobbyVoice called. activeLobbyId={_activeVoiceLobbyId}, hasCall={_activeVoiceCall != null}");
        try
        {
            if (_client != null && _activeVoiceLobbyId != 0)
                InvokeInstance(_client, "EndCall", _activeVoiceLobbyId, null);
        }
        catch
        {
        }

        _activeVoiceCall = null;
        _activeVoiceLobbyId = 0;
        _activeVoiceLobbySecret = string.Empty;
        _pendingVoiceLobbySecret = string.Empty;
        ResetAllLobbyVoiceChatStates();
    }

    private bool EnsureClient()
    {
        if (_client != null)
            return true;

        try
        {
            _clientType = Type.GetType(ClientTypeName, false);
            if (_clientType == null)
            {
                LogVoice("Discord SDK client type was not found.");
                SetConnectFailed("Discord SDK client type not found. Import Discord Social SDK package first.");
                return false;
            }

            _client = Activator.CreateInstance(_clientType);
            _statusChangedCallback = CreateClientCallback("OnStatusChanged", HandleClientStatusChangedBridge);
            InvokeInstance(_client, "SetStatusChangedCallback", _statusChangedCallback);

            _lobbyUpdatedCallback = CreateClientCallback("LobbyUpdatedCallback", HandleLobbyUpdatedBridge);
            InvokeInstance(_client, "SetLobbyUpdatedCallback", _lobbyUpdatedCallback);

            _lobbyMemberAddedCallback = CreateClientCallback("LobbyMemberAddedCallback", HandleLobbyMemberAddedBridge);
            InvokeInstance(_client, "SetLobbyMemberAddedCallback", _lobbyMemberAddedCallback);

            _lobbyMemberRemovedCallback = CreateClientCallback("LobbyMemberRemovedCallback", HandleLobbyMemberRemovedBridge);
            InvokeInstance(_client, "SetLobbyMemberRemovedCallback", _lobbyMemberRemovedCallback);

            LogVoice("Discord client created and status callback registered.");
            return true;
        }
        catch (Exception e)
        {
            SetConnectFailed($"Discord SDK initialization failed: {e.Message}");
            return false;
        }
    }

    private string ResolveScopes(string scopes)
    {
        if (!string.IsNullOrWhiteSpace(scopes))
            return scopes;

        try
        {
            Type clientType = Type.GetType(ClientTypeName, false);
            MethodInfo defaultCommunicationScopesMethod = clientType?.GetMethod("GetDefaultCommunicationScopes", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (defaultCommunicationScopesMethod?.Invoke(null, null) is string defaultCommunicationScopes && !string.IsNullOrWhiteSpace(defaultCommunicationScopes))
                return defaultCommunicationScopes;
        }
        catch (Exception e)
        {
            LogVoice($"Falling back to legacy Discord scopes because default communication scopes lookup failed: {e.Message}");
        }

        return LegacyDefaultScopes;
    }

    private Delegate CreateClientCallback(string nestedDelegateName, Action<object[]> bridge)
    {
        if (_clientType == null)
            throw new InvalidOperationException("Discord client type not initialized.");

        Type delegateType = _clientType.GetNestedType(nestedDelegateName, BindingFlags.Public);
        if (delegateType == null)
            throw new InvalidOperationException($"Discord callback type '{nestedDelegateName}' not found.");

        MethodInfo invokeMethod = delegateType.GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance);
        ParameterInfo[] parameters = invokeMethod.GetParameters();
        ParameterExpression[] args = parameters.Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();

        NewArrayExpression packedArgs = Expression.NewArrayInit(typeof(object), args.Select(p => Expression.Convert(p, typeof(object))));
        MethodCallExpression bridgeCall = Expression.Call(Expression.Constant(bridge), typeof(Action<object[]>).GetMethod("Invoke"), packedArgs);
        LambdaExpression lambda = Expression.Lambda(delegateType, bridgeCall, args);
        return lambda.Compile();
    }

    private static Delegate CreateDelegateCallback(Type delegateType, Action<object[]> bridge)
    {
        MethodInfo invokeMethod = delegateType.GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance);
        ParameterInfo[] parameters = invokeMethod.GetParameters();
        ParameterExpression[] args = parameters.Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();

        NewArrayExpression packedArgs = Expression.NewArrayInit(typeof(object), args.Select(p => Expression.Convert(p, typeof(object))));
        MethodCallExpression bridgeCall = Expression.Call(Expression.Constant(bridge), typeof(Action<object[]>).GetMethod("Invoke"), packedArgs);
        LambdaExpression lambda = Expression.Lambda(delegateType, bridgeCall, args);
        return lambda.Compile();
    }

    private static Delegate CreateMethodCallback(object target, string methodName, int parameterCount, int delegateParameterIndex, Action<object[]> bridge)
    {
        if (target == null)
            throw new InvalidOperationException($"Cannot create callback for {methodName}: target is null.");

        MethodInfo method = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m =>
            {
                if (m.Name != methodName)
                    return false;

                ParameterInfo[] parameters = m.GetParameters();
                return parameters.Length == parameterCount
                    && delegateParameterIndex >= 0
                    && delegateParameterIndex < parameters.Length
                    && typeof(Delegate).IsAssignableFrom(parameters[delegateParameterIndex].ParameterType);
            });

        if (method == null)
            throw new MissingMethodException(target.GetType().FullName, methodName);

        Type delegateType = method.GetParameters()[delegateParameterIndex].ParameterType;
        return CreateDelegateCallback(delegateType, bridge);
    }

    private void HandleAuthorizeResultBridge(object[] args)
    {
        object result = args.Length > 0 ? args[0] : null;
        string code = args.Length > 1 ? args[1] as string : null;
        string redirectUri = args.Length > 2 ? args[2] as string : null;

        LogVoice($"Authorize callback received. success={IsSdkResultSuccessful(result)}, hasCode={!string.IsNullOrWhiteSpace(code)}, hasRedirect={!string.IsNullOrWhiteSpace(redirectUri)}");

        if (!IsSdkResultSuccessful(result))
        {
            SetConnectFailed($"Discord authorize failed: {GetSdkResultError(result)}");
            return;
        }

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(redirectUri) || string.IsNullOrWhiteSpace(_pendingCodeVerifier))
        {
            SetConnectFailed("Discord authorize failed: missing auth code or verifier.");
            return;
        }

        try
        {
            Delegate tokenCallback = CreateClientCallback("TokenExchangeCallback", HandleTokenExchangeResultBridge);
            InvokeInstance(_client, "GetToken", _applicationId, code, _pendingCodeVerifier, redirectUri, tokenCallback);
            LogVoice("Token exchange requested.");
        }
        catch (Exception e)
        {
            SetConnectFailed($"Discord token exchange invocation failed: {e.Message}");
        }
    }

    private void HandleTokenExchangeResultBridge(object[] args)
    {
        object result = args.Length > 0 ? args[0] : null;
        string accessToken = args.Length > 1 ? args[1] as string : null;

        LogVoice($"Token exchange callback received. success={IsSdkResultSuccessful(result)}, hasAccessToken={!string.IsNullOrWhiteSpace(accessToken)}");

        if (!IsSdkResultSuccessful(result) || string.IsNullOrWhiteSpace(accessToken))
        {
            SetConnectFailed($"Discord token exchange failed: {GetSdkResultError(result)}");
            return;
        }

        _pendingCodeVerifier = string.Empty;

        try
        {
            Type tokenType = Type.GetType(AuthTokenTypeName, false);
            if (tokenType == null)
            {
                SetConnectFailed("Discord token type enum not found.");
                return;
            }

            object bearer = Enum.Parse(tokenType, "Bearer");
            Delegate updateTokenCallback = CreateClientCallback("UpdateTokenCallback", HandleUpdateTokenBridge);
            InvokeInstance(_client, "UpdateToken", bearer, accessToken, updateTokenCallback);
            LogVoice("UpdateToken requested.");
        }
        catch (Exception e)
        {
            SetConnectFailed($"Discord token update invocation failed: {e.Message}");
        }
    }

    private void HandleUpdateTokenBridge(object[] args)
    {
        object result = args.Length > 0 ? args[0] : null;
        LogVoice($"UpdateToken callback received. success={IsSdkResultSuccessful(result)}");
        if (!IsSdkResultSuccessful(result))
        {
            SetConnectFailed($"Discord token update failed: {GetSdkResultError(result)}");
            return;
        }

        try
        {
            InvokeInstance(_client, "Connect");
            LogVoice("Discord client Connect invoked.");
        }
        catch (Exception e)
        {
            SetConnectFailed($"Discord connect invocation failed: {e.Message}");
        }
    }

    private void HandleClientStatusChangedBridge(object[] args)
    {
        string status = args.Length > 0 ? args[0]?.ToString() : string.Empty;
        string error = args.Length > 1 ? args[1]?.ToString() : string.Empty;
        string errorDetail = args.Length > 2 ? args[2]?.ToString() : "0";

        LogVoice($"Client status changed. status={status}, error={error}, detail={errorDetail}, connecting={IsConnecting}, linked={IsLinked}");

        if (status == "Ready")
        {
            LinkCurrentDiscordUser();
            return;
        }

        if (status == "Disconnected" && IsConnecting)
            SetConnectFailed($"Discord disconnected: {error} ({errorDetail})");
    }

    private void HandleCreateOrJoinLobbyBridge(object[] args)
    {
        object result = args.Length > 0 ? args[0] : null;
        ulong lobbyId = args.Length > 1 ? ConvertToUInt64(args[1]) : 0;

        if (!IsSdkResultSuccessful(result) || lobbyId == 0)
        {
            Debug.LogWarning($"Discord lobby voice connect failed: {GetSdkResultError(result)}");
            return;
        }

        _activeVoiceLobbyId = lobbyId;
        _activeVoiceLobbySecret = _pendingVoiceLobbySecret;
        LogVoice($"Lobby voice join success. lobbyId={lobbyId}, secret({SummarizeSecret(_activeVoiceLobbySecret)})");
        StartOrReuseVoiceCall(lobbyId);
    }

    private void HandleSessionCreateOrJoinLobbyBridge(object[] args)
    {
        object result = args.Length > 0 ? args[0] : null;
        ulong lobbyId = args.Length > 1 ? ConvertToUInt64(args[1]) : 0;

        if (!IsSdkResultSuccessful(result) || lobbyId == 0)
        {
            string error = GetSdkResultError(result);
            Debug.LogWarning($"[Lobby] Session lobby join failed: {error}");
            _sessionLobbyJoinCompletion?.Invoke(false, 0, error);
            _sessionLobbyJoinCompletion = null;
            return;
        }

        _sessionLobbyIdBySecret[_pendingSessionLobbySecret] = lobbyId;
        _sessionLobbyJoinCompletion?.Invoke(true, lobbyId, string.Empty);
        _sessionLobbyJoinCompletion = null;
    }

    private void HandleLeaveSessionLobbyBridge(object[] args)
    {
        object result = args.Length > 0 ? args[0] : null;
        if (!IsSdkResultSuccessful(result))
        {
            Debug.LogWarning($"[Lobby] LeaveSessionLobby failed: {GetSdkResultError(result)}");
            return;
        }

        string[] secrets = _sessionLobbyIdBySecret
            .Where(pair => pair.Value == _pendingLeaveLobbyId)
            .Select(pair => pair.Key)
            .ToArray();

        for (int i = 0; i < secrets.Length; i++)
            _sessionLobbyIdBySecret.Remove(secrets[i]);
    }

    private void HandleLobbyUpdatedBridge(object[] args)
    {
        ulong lobbyId = args.Length > 0 ? ConvertToUInt64(args[0]) : 0;
        if (lobbyId == 0)
            return;

        OnSessionLobbyUpdated?.Invoke(lobbyId);
    }

    private void HandleLobbyMemberAddedBridge(object[] args)
    {
        ulong lobbyId = args.Length > 0 ? ConvertToUInt64(args[0]) : 0;
        ulong memberId = args.Length > 1 ? ConvertToUInt64(args[1]) : 0;
        if (lobbyId == 0 || memberId == 0)
            return;

        OnSessionLobbyMemberAdded?.Invoke(lobbyId, memberId);
    }

    private void HandleLobbyMemberRemovedBridge(object[] args)
    {
        ulong lobbyId = args.Length > 0 ? ConvertToUInt64(args[0]) : 0;
        ulong memberId = args.Length > 1 ? ConvertToUInt64(args[1]) : 0;
        if (lobbyId == 0 || memberId == 0)
            return;

        OnSessionLobbyMemberRemoved?.Invoke(lobbyId, memberId);
    }

    private void HandleSpeakingStatusChangedBridge(object[] args)
    {
        ulong userId = args.Length > 0 ? ConvertToUInt64(args[0]) : 0;
        object speakingPayload = args.Length > 1 ? args[1] : null;
        bool isPayloadParsed = TryConvertToBool(speakingPayload, out bool parsedSpeaking);
        bool isSpeaking = isPayloadParsed && parsedSpeaking;

        if (userId == 0)
            return;

        if (speakingPayload != null && !isPayloadParsed)
            LogVoice($"Speaking callback payload type unsupported. type={speakingPayload.GetType().Name}, value={speakingPayload}");

        LogVoice($"Speaking callback received. userId={userId}, speaking={isSpeaking}");
        SetLobbyUserVoiceChatActive(userId.ToString(), isSpeaking);
    }

    private void HandleVoiceStateChangedBridge(object[] args)
    {
        ulong userId = args.Length > 0 ? ConvertToUInt64(args[0]) : 0;
        if (userId == 0)
            return;

        if (args.Length <= 1 || args[1] is not bool isInVoiceCall)
        {
            LogVoice($"Voice connection state callback received with unsupported payload. userId={userId}, argCount={args.Length}");
            return;
        }

        LogVoice($"Voice connection state callback received. userId={userId}, inVoiceCall={isInVoiceCall}");
        if (!isInVoiceCall)
            SetLobbyUserVoiceChatActive(userId.ToString(), false);
    }

    private void StartOrReuseVoiceCall(ulong lobbyId)
    {
        LogVoice($"StartOrReuseVoiceCall called. lobbyId={lobbyId}");
        object call = null;

        try
        {
            call = InvokeInstance(_client, "StartCall", lobbyId);
            if (call == null)
                call = InvokeInstance(_client, "GetCall", lobbyId);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Discord voice call start failed: {e.Message}");
            return;
        }

        if (call == null)
        {
            Debug.LogWarning("Discord voice call start returned null and no existing call was found.");
            return;
        }

        _activeVoiceCall = call;
        LogVoice($"Voice call ready. lobbyId={lobbyId}, callType={call.GetType().Name}");
        RegisterVoiceCallCallbacks(call);
    }

    private void RegisterVoiceCallCallbacks(object call)
    {
        try
        {
            _callVoiceStateChangedCallback = CreateMethodCallback(call, "SetOnVoiceStateChangedCallback", 1, 0, HandleVoiceStateChangedBridge);
            InvokeInstance(call, "SetOnVoiceStateChangedCallback", _callVoiceStateChangedCallback);
            LogVoice("Voice-state callback registered.");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Discord voice-state callback registration failed: {e.Message}");
        }

        try
        {
            _callSpeakingStatusChangedCallback = CreateMethodCallback(call, "SetSpeakingStatusChangedCallback", 1, 0, HandleSpeakingStatusChangedBridge);
            InvokeInstance(call, "SetSpeakingStatusChangedCallback", _callSpeakingStatusChangedCallback);
            LogVoice("Speaking-status callback registered.");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Discord speaking callback registration failed: {e.Message}");
        }
    }

    private void LinkCurrentDiscordUser()
    {
        LogVoice("LinkCurrentDiscordUser called.");
        object user = null;
        try
        {
            user = InvokeInstance(_client, "GetCurrentUserV2");
            if (user == null)
            {
                SetConnectFailed("Discord connect failed: current user unavailable.");
                return;
            }

            object idValue = InvokeInstance(user, "Id");
            string userId = idValue?.ToString();
            string displayName = InvokeInstance(user, "DisplayName") as string;

            if (string.IsNullOrWhiteSpace(userId))
            {
                SetConnectFailed("Discord connect failed: invalid user id.");
                return;
            }

            if (string.IsNullOrWhiteSpace(displayName))
                displayName = "Player";

            IsConnecting = false;
            LastAuthError = null;
            LogVoice($"Current Discord user resolved. userId={userId}, displayName={displayName}");
            LinkLocalAccount(userId, displayName);
        }
        catch (Exception e)
        {
            SetConnectFailed($"Discord user link failed: {e.Message}");
        }
        finally
        {
            DisposeIfNeeded(user);
        }
    }

    private static object InvokeInstance(object target, string methodName, params object[] args)
    {
        if (target == null)
            throw new InvalidOperationException($"Cannot invoke {methodName}: target is null.");

        Type type = target.GetType();
        MethodInfo method = FindMethod(type, methodName, args);
        if (method == null)
            throw new MissingMethodException(type.FullName, methodName);

        return method.Invoke(target, args);
    }

    private static MethodInfo FindMethod(Type type, string methodName, object[] args)
    {
        MethodInfo[] candidates = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == methodName)
            .ToArray();

        foreach (MethodInfo candidate in candidates)
        {
            ParameterInfo[] parameters = candidate.GetParameters();
            if (parameters.Length != args.Length)
                continue;

            bool matched = true;
            for (int i = 0; i < parameters.Length; i++)
            {
                object arg = args[i];
                Type expected = parameters[i].ParameterType;

                if (arg == null)
                {
                    if (expected.IsValueType && Nullable.GetUnderlyingType(expected) == null)
                    {
                        matched = false;
                        break;
                    }

                    continue;
                }

                if (!expected.IsInstanceOfType(arg) && !(expected.IsEnum && arg is string))
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
                return candidate;
        }

        return null;
    }

    private void ResetAllLobbyVoiceChatStates()
    {
        string[] userIds = _voiceChatActiveByUserId.Keys.ToArray();
        for (int i = 0; i < userIds.Length; i++)
            SetLobbyUserVoiceChatActive(userIds[i], false);
    }

    private static ulong ConvertToUInt64(object value)
    {
        if (value == null)
            return 0;

        if (value is ulong ulongValue)
            return ulongValue;

        if (value is long longValue && longValue >= 0)
            return (ulong)longValue;

        if (value is int intValue && intValue >= 0)
            return (ulong)intValue;

        return ulong.TryParse(value.ToString(), out ulong parsedValue) ? parsedValue : 0;
    }

    private static bool IsSdkResultSuccessful(object result)
    {
        if (result == null)
            return false;

        MethodInfo successfulMethod = result.GetType().GetMethod("Successful", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
        if (successfulMethod == null)
            return false;

        object value = successfulMethod.Invoke(result, null);
        return value is bool success && success;
    }

    private static string GetSdkResultError(object result)
    {
        if (result == null)
            return "unknown";

        MethodInfo errorMethod = result.GetType().GetMethod("Error", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
        if (errorMethod == null)
            return "unknown";

        object value = errorMethod.Invoke(result, null);
        return value?.ToString() ?? "unknown";
    }

    private static void DisposeIfNeeded(object value)
    {
        if (value is IDisposable disposable)
            disposable.Dispose();
    }

    private static void LogVoice(string message)
    {
        Debug.Log($"[DiscordVoice] {message}");
    }

    private static string SummarizeSecret(string secret)
    {
        return string.IsNullOrWhiteSpace(secret) ? "empty" : $"len={secret.Length}";
    }

    private static bool TryConvertMetadataObject(object metadataObject, out Dictionary<string, string> metadata)
    {
        metadata = null;

        if (metadataObject is Dictionary<string, string> typed)
        {
            metadata = new Dictionary<string, string>(typed);
            return true;
        }

        if (metadataObject is IDictionary<string, string> mapped)
        {
            metadata = mapped.ToDictionary(pair => pair.Key, pair => pair.Value);
            return true;
        }

        return false;
    }

    private static bool TryConvertToBool(object value, out bool result)
    {
        if (value is null)
        {
            result = false;
            return true;
        }

        if (value is bool boolValue)
        {
            result = boolValue;
            return true;
        }

        if (value is byte byteValue)
        {
            result = byteValue != 0;
            return true;
        }

        if (value is sbyte sbyteValue)
        {
            result = sbyteValue != 0;
            return true;
        }

        if (value is short shortValue)
        {
            result = shortValue != 0;
            return true;
        }

        if (value is ushort ushortValue)
        {
            result = ushortValue != 0;
            return true;
        }

        if (value is int intValue)
        {
            result = intValue != 0;
            return true;
        }

        if (value is uint uintValue)
        {
            result = uintValue != 0;
            return true;
        }

        if (value is long longValue)
        {
            result = longValue != 0;
            return true;
        }

        if (value is ulong ulongValue)
        {
            result = ulongValue != 0;
            return true;
        }

        if (value is string stringValue)
        {
            if (bool.TryParse(stringValue, out bool parsedBool))
            {
                result = parsedBool;
                return true;
            }

            if (long.TryParse(stringValue, out long parsedLong))
            {
                result = parsedLong != 0;
                return true;
            }
        }

        result = false;
        return false;
    }

    private void SetConnectFailed(string message)
    {
        IsConnecting = false;
        LastAuthError = message;
        _pendingCodeVerifier = string.Empty;
        Debug.LogWarning(message);
        OnAuthStateChanged?.Invoke();
    }
}
