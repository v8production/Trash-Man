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
    private const string DefaultScopes = "openid sdk.social_layer";
    private const string ClientTypeName = "Discord.Sdk.Client, Discord.Sdk";
    private const string AuthArgsTypeName = "Discord.Sdk.AuthorizationArgs, Discord.Sdk";
    private const string AuthTokenTypeName = "Discord.Sdk.AuthorizationTokenType, Discord.Sdk";

    private readonly Dictionary<string, DiscordLobbyUser> _members = new();
    private readonly Dictionary<string, bool> _voiceChatActiveByUserId = new();
    private int _inviteSerial;

    private object _client;
    private Type _clientType;
    private object _activeVoiceCall;
    private Delegate _statusChangedCallback;
    private Delegate _createOrJoinLobbyCallback;
    private Delegate _callSpeakingStatusChangedCallback;
    private Delegate _callVoiceStateChangedCallback;
    private string _pendingCodeVerifier = string.Empty;
    private string _pendingVoiceLobbySecret = string.Empty;
    private string _activeVoiceLobbySecret = string.Empty;
    private ulong _applicationId;
    private ulong _activeVoiceLobbyId;
    private string _scopes = DefaultScopes;

    public event Action<DiscordLobbyUser> OnLobbyUserJoined;
    public event Action<string> OnInviteRequested;
    public event Action<string> OnLocalDisplayNameChanged;
    public event Action OnAuthStateChanged;
    public event Action<string, bool> OnLobbyUserVoiceChatStateChanged;

    public bool IsLinked { get; private set; }
    public bool IsConnecting { get; private set; }
    public string LastAuthError { get; private set; }
    public string LocalUserId { get; private set; } = "local-user";
    public string LocalDisplayName { get; private set; } = "Player";

    public void Init()
    {
        _ = EnsureClient();
    }

    public void Connect(ulong applicationId, string scopes)
    {
        if (applicationId == 0)
        {
            SetConnectFailed("Discord connect failed: invalid application id.");
            return;
        }

        if (IsLinked || IsConnecting)
            return;

        if (!EnsureClient())
            return;

        _applicationId = applicationId;
        _scopes = string.IsNullOrWhiteSpace(scopes) ? DefaultScopes : scopes;
        _pendingCodeVerifier = string.Empty;
        IsConnecting = true;
        LastAuthError = null;
        OnAuthStateChanged?.Invoke();

        object verifier = null;
        object challenge = null;
        object authArgs = null;

        try
        {
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

        Debug.Log($"Discord link success: username={LocalDisplayName}");
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

        if (_voiceChatActiveByUserId.TryGetValue(userId, out bool previousState) && previousState == isActive)
            return;

        _voiceChatActiveByUserId[userId] = isActive;
        OnLobbyUserVoiceChatStateChanged?.Invoke(userId, isActive);
    }

    public void EnsureLobbyVoiceConnected(string lobbySecret)
    {
        if (string.IsNullOrWhiteSpace(lobbySecret) || !IsLinked)
            return;

        if (!EnsureClient())
            return;

        if (_activeVoiceCall != null && string.Equals(_activeVoiceLobbySecret, lobbySecret, StringComparison.Ordinal))
            return;

        if (_activeVoiceCall != null && !string.Equals(_activeVoiceLobbySecret, lobbySecret, StringComparison.Ordinal))
            EndActiveLobbyVoice();

        _pendingVoiceLobbySecret = lobbySecret;

        try
        {
            _createOrJoinLobbyCallback = CreateMethodCallback(_client, "CreateOrJoinLobby", 2, 1, HandleCreateOrJoinLobbyBridge);
            InvokeInstance(_client, "CreateOrJoinLobby", lobbySecret, _createOrJoinLobbyCallback);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Discord lobby voice connect failed while joining lobby: {e.Message}");
        }
    }

    public void EndActiveLobbyVoice()
    {
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
                SetConnectFailed("Discord SDK client type not found. Import Discord Social SDK package first.");
                return false;
            }

            _client = Activator.CreateInstance(_clientType);
            _statusChangedCallback = CreateClientCallback("OnStatusChanged", HandleClientStatusChangedBridge);
            InvokeInstance(_client, "SetStatusChangedCallback", _statusChangedCallback);
            return true;
        }
        catch (Exception e)
        {
            SetConnectFailed($"Discord SDK initialization failed: {e.Message}");
            return false;
        }
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
        }
        catch (Exception e)
        {
            SetConnectFailed($"Discord token update invocation failed: {e.Message}");
        }
    }

    private void HandleUpdateTokenBridge(object[] args)
    {
        object result = args.Length > 0 ? args[0] : null;
        if (!IsSdkResultSuccessful(result))
        {
            SetConnectFailed($"Discord token update failed: {GetSdkResultError(result)}");
            return;
        }

        try
        {
            InvokeInstance(_client, "Connect");
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
        StartOrReuseVoiceCall(lobbyId);
    }

    private void HandleSpeakingStatusChangedBridge(object[] args)
    {
        ulong userId = args.Length > 0 ? ConvertToUInt64(args[0]) : 0;
        bool isSpeaking = args.Length > 1 && args[1] is bool speaking && speaking;

        if (userId == 0)
            return;

        SetLobbyUserVoiceChatActive(userId.ToString(), isSpeaking);
    }

    private void HandleVoiceStateChangedBridge(object[] args)
    {
        ulong userId = args.Length > 0 ? ConvertToUInt64(args[0]) : 0;
        if (userId == 0)
            return;

        if (!IsLobbyUserVoiceChatActive(userId.ToString()))
            SetLobbyUserVoiceChatActive(userId.ToString(), false);
    }

    private void StartOrReuseVoiceCall(ulong lobbyId)
    {
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
        RegisterVoiceCallCallbacks(call);
    }

    private void RegisterVoiceCallCallbacks(object call)
    {
        try
        {
            _callVoiceStateChangedCallback = CreateMethodCallback(call, "SetOnVoiceStateChangedCallback", 1, 0, HandleVoiceStateChangedBridge);
            InvokeInstance(call, "SetOnVoiceStateChangedCallback", _callVoiceStateChangedCallback);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Discord voice-state callback registration failed: {e.Message}");
        }

        try
        {
            _callSpeakingStatusChangedCallback = CreateMethodCallback(call, "SetSpeakingStatusChangedCallback", 1, 0, HandleSpeakingStatusChangedBridge);
            InvokeInstance(call, "SetSpeakingStatusChangedCallback", _callSpeakingStatusChangedCallback);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Discord speaking callback registration failed: {e.Message}");
        }
    }

    private void LinkCurrentDiscordUser()
    {
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

    private void SetConnectFailed(string message)
    {
        IsConnecting = false;
        LastAuthError = message;
        _pendingCodeVerifier = string.Empty;
        Debug.LogWarning(message);
        OnAuthStateChanged?.Invoke();
    }
}
