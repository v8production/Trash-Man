using System;
using System.Collections.Generic;
using Netcode.Transports;
using Unity.Netcode;
using UnityEngine;

public class LobbySessionManager
{
    private const int JoinCodeLength = 6;
    private const string VoiceSecretPrefix = "trash-man-lobby";
    private const string SessionSecretSuffix = "-session";
    private const string LobbyMetadataJoinCode = "join_code";
    private const string LobbyMetadataHostUserId = "host_user_id";
    private const string LobbyMetadataVoiceSecret = "voice_secret";
    private const float LobbyStateSyncIntervalSeconds = 1f;
    private const string LobbyMetadataHostSteamId = "host_steam_id";
    private const string MemberMetadataSteamId = "steam_id";
    private const string MemberMetadataIsHost = "is_host";

    private static readonly char[] JoinCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789".ToCharArray();

    private readonly Dictionary<string, RangerController> _rangersByUserId = new();
    private readonly Dictionary<string, UI_Nickname> _nicknamesByUserId = new();

    private string _currentVoiceSecret = string.Empty;
    private ulong _currentDiscordLobbyId;
    private float _nextLobbyStateSyncTime;
    private ulong _currentHostSteamId;

    private bool _pendingSteamClientConnect;
    private bool _hasRequestedSteamClientStart;
    private float _pendingSteamClientConnectDeadline;
    private bool _hasShownPendingSteamConnectToast;

    private static bool s_loggedNetcodeMissing;
    private static bool s_loggedNetworkManagerMissing;
    private static bool s_loggedTransportMissing;

    public bool IsHosting { get; private set; }
    public string HostUserId { get; private set; } = string.Empty;
    public string CurrentJoinCode { get; private set; } = string.Empty;
    public string CurrentVoiceSecret => _currentVoiceSecret;
    public bool HasJoinedLobbySession => _currentDiscordLobbyId != 0;

    public bool HasLobbyNetworkConnectionFailed { get; private set; }
    public string LastLobbyNetworkError { get; private set; } = string.Empty;

    public void Init()
    {
        Managers.Discord.OnLocalDisplayNameChanged -= HandleLocalDisplayNameChanged;
        Managers.Discord.OnLocalDisplayNameChanged += HandleLocalDisplayNameChanged;
        Managers.Discord.OnLobbyUserVoiceChatStateChanged -= HandleLobbyUserVoiceChatStateChanged;
        Managers.Discord.OnLobbyUserVoiceChatStateChanged += HandleLobbyUserVoiceChatStateChanged;
        Managers.Discord.OnSessionLobbyUpdated -= HandleSessionLobbyUpdated;
        Managers.Discord.OnSessionLobbyUpdated += HandleSessionLobbyUpdated;
        Managers.Discord.OnSessionLobbyMemberAdded -= HandleSessionLobbyMemberAdded;
        Managers.Discord.OnSessionLobbyMemberAdded += HandleSessionLobbyMemberAdded;
        Managers.Discord.OnSessionLobbyMemberRemoved -= HandleSessionLobbyMemberRemoved;
        Managers.Discord.OnSessionLobbyMemberRemoved += HandleSessionLobbyMemberRemoved;
        Debug.Log("[LobbyVoice] LobbySessionManager subscribed to lobby voice state events.");
    }

    public void OnUpdate()
    {
        // Lobby host/client reconciliation should only run while in the LobbyScene.
        // In GameScene, running this loop can accidentally restart/shutdown UTP.
        if (Managers.Scene.CurrentScene == null || Managers.Scene.CurrentScene.SceneType != Define.Scene.Lobby)
            return;

        if (_currentDiscordLobbyId == 0)
            return;

        if (Time.unscaledTime < _nextLobbyStateSyncTime)
            return;

        _nextLobbyStateSyncTime = Time.unscaledTime + LobbyStateSyncIntervalSeconds;
        RefreshHostStateFromDiscordMetadata();
        TryResolvePendingSteamClientConnect();
    }

    public void Clear()
    {
        Managers.Discord.OnLocalDisplayNameChanged -= HandleLocalDisplayNameChanged;
        Managers.Discord.OnLobbyUserVoiceChatStateChanged -= HandleLobbyUserVoiceChatStateChanged;
        Managers.Discord.OnSessionLobbyUpdated -= HandleSessionLobbyUpdated;
        Managers.Discord.OnSessionLobbyMemberAdded -= HandleSessionLobbyMemberAdded;
        Managers.Discord.OnSessionLobbyMemberRemoved -= HandleSessionLobbyMemberRemoved;
        _rangersByUserId.Clear();
        _nicknamesByUserId.Clear();
        IsHosting = false;
        HostUserId = string.Empty;
        CurrentJoinCode = string.Empty;
        _currentVoiceSecret = string.Empty;
        _currentHostSteamId = 0;
        _currentDiscordLobbyId = 0;
        _nextLobbyStateSyncTime = 0f;
        _pendingSteamClientConnect = false;
        _hasRequestedSteamClientStart = false;
        _pendingSteamClientConnectDeadline = 0f;
        _hasShownPendingSteamConnectToast = false;
        ResetClientConnectionTracking();
    }

    public static string NormalizeJoinCode(string value) => Util.NormalizeLobbyJoinCode(value);

    public bool HasJoinCode(string rawJoinCode)
    {
        string joinCode = NormalizeJoinCode(rawJoinCode);
        return !string.IsNullOrWhiteSpace(joinCode);
    }

    public void RegisterLobbyUserObjects(string userId, RangerController ranger, UI_Nickname nickname)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        if (nickname != null && _nicknamesByUserId.TryGetValue(userId, out UI_Nickname prevNickname) && prevNickname != null && prevNickname != nickname)
            UnityEngine.Object.Destroy(prevNickname.gameObject);

        if (ranger != null)
            _rangersByUserId[userId] = ranger;

        if (nickname != null)
            _nicknamesByUserId[userId] = nickname;

        LobbyScene.RegisterUserObjects(userId, ranger, nickname);
    }

    public bool TryGetLocalRangerTransform(out Transform rangerTransform)
    {
        rangerTransform = null;

        string localUserId = Managers.Discord.LocalUserId;
        if (!string.IsNullOrWhiteSpace(localUserId) && _rangersByUserId.TryGetValue(localUserId, out RangerController cachedRanger) && cachedRanger != null)
        {
            rangerTransform = cachedRanger.transform;
            if (rangerTransform != null)
                return true;
        }

        LobbyNetworkPlayer[] networkPlayers = UnityEngine.Object.FindObjectsByType<LobbyNetworkPlayer>();
        for (int i = 0; i < networkPlayers.Length; i++)
        {
            LobbyNetworkPlayer networkPlayer = networkPlayers[i];
            if (networkPlayer == null || !networkPlayer.IsOwner)
                continue;

            if (networkPlayer.TryGetLobbyRangerTransform(out Transform lobbyRangerTransform) && lobbyRangerTransform != null)
            {
                rangerTransform = lobbyRangerTransform;
                return true;
            }
        }

        return false;
    }

    public void UnregisterLobbyUserObjects(string userId, RangerController ranger, UI_Nickname nickname)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        if (_rangersByUserId.TryGetValue(userId, out RangerController storedRanger) && storedRanger == ranger)
            _rangersByUserId.Remove(userId);

        if (_nicknamesByUserId.TryGetValue(userId, out UI_Nickname storedNickname) && storedNickname == nickname)
            _nicknamesByUserId.Remove(userId);

        LobbyScene.UnregisterUserObjects(userId, ranger, nickname);
    }

    public bool JoinLobbyByCode(string rawJoinCode)
    {
        string joinCode = NormalizeJoinCode(rawJoinCode);
        if (string.IsNullOrWhiteSpace(joinCode))
        {
            Debug.LogWarning("[Lobby] Join failed: invalid join code format.");
            return false;
        }

        CleanupExistingLobbyObjects();

        CurrentJoinCode = joinCode;
        string sessionSecret = BuildSessionSecret(joinCode);
        string voiceSecret = BuildVoiceSecret(joinCode);
        _currentVoiceSecret = voiceSecret;
        _nextLobbyStateSyncTime = 0f;
        _pendingSteamClientConnect = false;
        _hasRequestedSteamClientStart = false;
        _pendingSteamClientConnectDeadline = 0f;
        _hasShownPendingSteamConnectToast = false;

        bool requested = Managers.Discord.CreateOrJoinSessionLobby(
            sessionSecret,
            null,
            BuildLocalMemberMetadata(false),
            false,
            (success, lobbyId, error) => HandleDiscordLobbyJoined(success, lobbyId, voiceSecret, false, error));

        if (!requested)
        {
            Debug.LogWarning($"[Lobby] Join failed: Discord session request not issued for code={joinCode}");
            return false;
        }

        return true;
    }

    public void QuitCurrentRoom()
    {
        if (_currentDiscordLobbyId != 0)
            Managers.Discord.LeaveSessionLobby(_currentDiscordLobbyId);

        TryStopNetwork();
        Managers.Discord.EndActiveLobbyVoice();
        _rangersByUserId.Clear();
        _nicknamesByUserId.Clear();
        IsHosting = false;
        HostUserId = string.Empty;
        CurrentJoinCode = string.Empty;
        _currentVoiceSecret = string.Empty;
        _currentHostSteamId = 0;
        _currentDiscordLobbyId = 0;
        _nextLobbyStateSyncTime = 0f;
        _pendingSteamClientConnect = false;
        _hasRequestedSteamClientStart = false;
        _pendingSteamClientConnectDeadline = 0f;
        _hasShownPendingSteamConnectToast = false;
        ResetClientConnectionTracking();
    }

    public void SetRangerNicknameVoiceActive(string userId, bool isVoiceChatActive)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        // Voice indicators are authored for the LobbyScene (ranger + nickname UI).
        // In GameScene the lobby ranger objects are destroyed, so ignore voice updates.
        bool isLobbyScene = Managers.Scene.CurrentScene != null && Managers.Scene.CurrentScene.SceneType == Define.Scene.Lobby;
        if (!isLobbyScene)
            return;

        if (LobbyScene.TrySetNicknameSpeakerActive(userId, isVoiceChatActive))
            return;

        if (!_rangersByUserId.TryGetValue(userId, out RangerController ranger) || ranger == null)
        {
            return;
        }

        UI_Nickname nicknameUI = ranger.GetComponentInChildren<UI_Nickname>(true);
        if (nicknameUI == null)
        {
            return;
        }

        nicknameUI.SetActive(isVoiceChatActive);
        _nicknamesByUserId[userId] = nicknameUI;
        LobbyScene.RegisterUserObjects(userId, ranger, nicknameUI);
    }

    public void BootstrapLocalHostLobby()
    {
        if (!Managers.Discord.IsLinked)
        {
            Debug.LogWarning("Lobby host bootstrap skipped: Discord account is not linked.");
            return;
        }

        if (!Managers.Steam.IsInitialized)
        {
            Managers.Toast.EnqueueMessage(
    $"Steam is not initialized.\n{Managers.Steam.LastInitError}",
    3f);
            return;
        }

        CleanupExistingLobbyObjects();

        string joinCode = GenerateUniqueJoinCode();
        string voiceSecret = BuildVoiceSecret(joinCode);
        string sessionSecret = BuildSessionSecret(joinCode);

        HostUserId = Managers.Discord.LocalUserId;
        CurrentJoinCode = joinCode;
        _currentVoiceSecret = voiceSecret;
        _currentHostSteamId = Managers.Steam.LocalSteamId.m_SteamID;
        _nextLobbyStateSyncTime = 0f;

        _pendingSteamClientConnect = false;
        _hasRequestedSteamClientStart = false;
        _pendingSteamClientConnectDeadline = 0f;
        _hasShownPendingSteamConnectToast = false;

        IsHosting = TryStartSteamHost();

        if (!IsHosting)
        {
            Debug.LogWarning("[Lobby] Host bootstrap failed: Steam host did not start.");
            Managers.Toast.EnqueueMessage("Failed to start lobby host.\nCheck Steam/Netcode setup.", 3f);
            HostUserId = string.Empty;
            _currentHostSteamId = 0;
            return;
        }

        GUIUtility.systemCopyBuffer = CurrentJoinCode;
        Managers.Toast.EnqueueMessage("Enter code is copied on clipboard.", 2.5f);

        Dictionary<string, string> lobbyMetadata = BuildHostLobbyMetadata(
            CurrentJoinCode,
            _currentHostSteamId,
            _currentVoiceSecret,
            HostUserId);

        bool requested = Managers.Discord.CreateOrJoinSessionLobby(
            sessionSecret,
            lobbyMetadata,
            BuildLocalMemberMetadata(true),
            true,
            (success, lobbyId, error) => HandleDiscordLobbyJoined(success, lobbyId, _currentVoiceSecret, true, error));

        if (!requested)
        {
            Debug.LogWarning("[Lobby] Host bootstrap failed: Discord session request not issued.");
            TryStopNetwork();
            IsHosting = false;
            HostUserId = string.Empty;
            _currentHostSteamId = 0;
        }
    }

    private void HandleDiscordLobbyJoined(bool success, ulong lobbyId, string lobbySecret, bool requestedAsHost, string error)
    {
        if (!success || lobbyId == 0)
        {
            Debug.LogWarning($"[Lobby] Discord lobby join failed. requestedAsHost={requestedAsHost}, error={error}");
            if (requestedAsHost)
            {
                TryStopNetwork();
                IsHosting = false;
                HostUserId = string.Empty;
            }

            Managers.Toast.EnqueueMessage("Lobby join failed. Please try again.", 2.5f);
            Managers.Scene.LoadScene(Define.Scene.Intro);
            return;
        }

        _currentDiscordLobbyId = lobbyId;

        _pendingSteamClientConnect = false;
        _hasRequestedSteamClientStart = false;
        _pendingSteamClientConnectDeadline = 0f;
        _hasShownPendingSteamConnectToast = false;

        if (Managers.Discord.TryGetSessionLobbyMetadata(lobbyId, out Dictionary<string, string> metadata) && metadata != null)
        {
            if (metadata.TryGetValue(LobbyMetadataJoinCode, out string metadataJoinCode) &&
                !string.IsNullOrWhiteSpace(metadataJoinCode))
                CurrentJoinCode = NormalizeJoinCode(metadataJoinCode);

            if (metadata.TryGetValue(LobbyMetadataHostUserId, out string metadataHostUserId) &&
                !string.IsNullOrWhiteSpace(metadataHostUserId))
                HostUserId = metadataHostUserId;

            if (metadata.TryGetValue(LobbyMetadataHostSteamId, out string metadataHostSteamId) &&
                ulong.TryParse(metadataHostSteamId, out ulong parsedHostSteamId))
                _currentHostSteamId = parsedHostSteamId;

            if (metadata.TryGetValue(LobbyMetadataVoiceSecret, out string metadataVoiceSecret) &&
                !string.IsNullOrWhiteSpace(metadataVoiceSecret))
                _currentVoiceSecret = metadataVoiceSecret;
        }

        if (string.IsNullOrWhiteSpace(HostUserId))
            HostUserId = requestedAsHost ? Managers.Discord.LocalUserId : string.Empty;

        IsHosting = requestedAsHost || string.Equals(HostUserId, Managers.Discord.LocalUserId, StringComparison.Ordinal);

        if (!IsHosting)
        {
            if (_currentHostSteamId == 0)
            {
                // Metadata can arrive after the join callback.
                // Keep the client in LobbyScene and retry until metadata appears (or timeout).
                _pendingSteamClientConnect = true;
                _pendingSteamClientConnectDeadline = Time.unscaledTime + 6f;

                if (!_hasShownPendingSteamConnectToast)
                {
                    _hasShownPendingSteamConnectToast = true;
                    Managers.Toast.EnqueueMessage("Connecting to lobby host...", 2.0f);
                }

                Debug.LogWarning("[Lobby] Discord lobby is missing host SteamID metadata. Waiting for lobby update.");
                return;
            }

            if (!TryStartSteamClient(_currentHostSteamId))
            {
                HasLobbyNetworkConnectionFailed = true;
                LastLobbyNetworkError = $"Failed to start Steam client. hostSteamId={_currentHostSteamId}";
                Debug.LogWarning($"[Lobby] {LastLobbyNetworkError}");
                Managers.Toast.EnqueueMessage("Failed to connect to lobby host.", 2.5f);
                Managers.Scene.LoadScene(Define.Scene.Intro);
                return;
            }

            _hasRequestedSteamClientStart = true;
        }

        Managers.Discord.NotifyLobbyUserJoined(Managers.Discord.LocalUserId, Managers.Discord.LocalDisplayName, true);
        Managers.Discord.EnsureLobbyVoiceConnected(_currentVoiceSecret);
        _nextLobbyStateSyncTime = 0f;

        Debug.Log($"[Lobby] Discord lobby ready. lobbyId={_currentDiscordLobbyId}, joinCode={CurrentJoinCode}, host={HostUserId}, localHosting={IsHosting}");
    }

    private static Dictionary<string, string> BuildHostLobbyMetadata(
        string joinCode,
        ulong hostSteamId,
        string voiceSecret,
        string hostUserId)
    {
        return new Dictionary<string, string>
        {
            [LobbyMetadataJoinCode] = joinCode,
            [LobbyMetadataHostUserId] = hostUserId,
            [LobbyMetadataHostSteamId] = hostSteamId.ToString(),
            [LobbyMetadataVoiceSecret] = voiceSecret,
        };
    }

    private static Dictionary<string, string> BuildLocalMemberMetadata(bool isHost)
    {
        ulong steamId = Managers.Steam.IsInitialized ? Managers.Steam.LocalSteamId.m_SteamID : 0;

        return new Dictionary<string, string>
        {
            ["discord_user_id"] = Managers.Discord.LocalUserId,
            ["display_name"] = Managers.Discord.LocalDisplayName,
            [MemberMetadataSteamId] = steamId != 0 ? steamId.ToString() : string.Empty,
            [MemberMetadataIsHost] = isHost ? "1" : "0",
        };
    }

    private static void CleanupExistingLobbyObjects()
    {
        RangerController[] rangers = UnityEngine.Object.FindObjectsByType<RangerController>();
        for (int i = 0; i < rangers.Length; i++)
            UnityEngine.Object.Destroy(rangers[i].gameObject);

        LobbyCameraController[] cameras = UnityEngine.Object.FindObjectsByType<LobbyCameraController>();
        for (int i = 0; i < cameras.Length; i++)
            UnityEngine.Object.Destroy(cameras[i].gameObject);
    }

    private void HandleLocalDisplayNameChanged(string displayName)
    {
        if (_nicknamesByUserId.TryGetValue(Managers.Discord.LocalUserId, out UI_Nickname nicknameUI) && nicknameUI != null)
            nicknameUI.SetText(displayName);
    }

    private void HandleLobbyUserVoiceChatStateChanged(string userId, bool isActive)
    {
        // Debug noise while investigating input routing.
        // Debug.Log($"[LobbyVoice] Lobby user speaking indicator event. userId={userId}, speaking={isActive}");
        if (Managers.Scene.CurrentScene == null || Managers.Scene.CurrentScene.SceneType != Define.Scene.Lobby)
            return;

        SetRangerNicknameVoiceActive(userId, isActive);
    }

    private static string GenerateJoinCode()
    {
        char[] chars = new char[JoinCodeLength];
        for (int i = 0; i < chars.Length; i++)
            chars[i] = JoinCodeAlphabet[UnityEngine.Random.Range(0, JoinCodeAlphabet.Length)];

        return new string(chars);
    }

    private static string GenerateUniqueJoinCode()
    {
        return GenerateJoinCode();
    }

    private static string BuildVoiceSecret(string joinCode)
    {
        return $"{VoiceSecretPrefix}-{joinCode.ToLowerInvariant()}";
    }

    private static string BuildSessionSecret(string joinCode)
    {
        return $"{VoiceSecretPrefix}-{joinCode.ToLowerInvariant()}{SessionSecretSuffix}";
    }

    private void HandleSessionLobbyUpdated(ulong lobbyId)
    {
        if (lobbyId != _currentDiscordLobbyId)
            return;

        _nextLobbyStateSyncTime = 0f;
        RefreshHostStateFromDiscordMetadata();
        TryResolvePendingSteamClientConnect();
    }

    private void HandleSessionLobbyMemberAdded(ulong lobbyId, ulong memberId)
    {
        if (lobbyId != _currentDiscordLobbyId)
            return;

        _nextLobbyStateSyncTime = 0f;
        RefreshHostStateFromDiscordMetadata();
        TryResolvePendingSteamClientConnect();
    }

    private void HandleSessionLobbyMemberRemoved(ulong lobbyId, ulong memberId)
    {
        if (lobbyId != _currentDiscordLobbyId)
            return;

        _nextLobbyStateSyncTime = 0f;
        RefreshHostStateFromDiscordMetadata();
        TryResolvePendingSteamClientConnect();
    }

    private void RefreshHostStateFromDiscordMetadata()
    {
        if (_currentDiscordLobbyId == 0)
            return;

        if (!Managers.Discord.TryGetSessionLobbyMetadata(_currentDiscordLobbyId, out Dictionary<string, string> metadata) ||
            metadata == null)
            return;

        if (metadata.TryGetValue(LobbyMetadataJoinCode, out string metadataJoinCode) &&
            !string.IsNullOrWhiteSpace(metadataJoinCode))
            CurrentJoinCode = NormalizeJoinCode(metadataJoinCode);

        if (metadata.TryGetValue(LobbyMetadataHostUserId, out string metadataHostUserId) &&
            !string.IsNullOrWhiteSpace(metadataHostUserId))
            HostUserId = metadataHostUserId;

        if (metadata.TryGetValue(LobbyMetadataHostSteamId, out string metadataHostSteamId) &&
            ulong.TryParse(metadataHostSteamId, out ulong parsedHostSteamId))
            _currentHostSteamId = parsedHostSteamId;

        if (metadata.TryGetValue(LobbyMetadataVoiceSecret, out string metadataVoiceSecret) &&
            !string.IsNullOrWhiteSpace(metadataVoiceSecret))
            _currentVoiceSecret = metadataVoiceSecret;

        if (_currentHostSteamId == 0)
            TryResolveHostSteamIdFromMemberMetadata();
    }

    private void TryResolveHostSteamIdFromMemberMetadata()
    {
        if (_currentDiscordLobbyId == 0)
            return;

        if (!Managers.Discord.TryGetSessionLobbyMemberIds(_currentDiscordLobbyId, out ulong[] memberIds) || memberIds == null || memberIds.Length == 0)
            return;

        ulong hostDiscordId = 0;
        ulong.TryParse(HostUserId, out hostDiscordId);

        for (int i = 0; i < memberIds.Length; i++)
        {
            ulong memberId = memberIds[i];
            if (memberId == 0)
                continue;

            if (!Managers.Discord.TryGetSessionLobbyMemberMetadata(_currentDiscordLobbyId, memberId, out Dictionary<string, string> memberMetadata) || memberMetadata == null)
                continue;

            bool isHost = false;
            if (memberMetadata.TryGetValue(MemberMetadataIsHost, out string isHostValue))
                isHost = string.Equals(isHostValue, "1", StringComparison.Ordinal) || string.Equals(isHostValue, "true", StringComparison.OrdinalIgnoreCase);

            if (!isHost && hostDiscordId != 0)
                isHost = memberId == hostDiscordId;

            if (!isHost)
                continue;

            if (memberMetadata.TryGetValue(MemberMetadataSteamId, out string steamIdValue) && ulong.TryParse(steamIdValue, out ulong steamId) && steamId != 0)
            {
                _currentHostSteamId = steamId;
                return;
            }
        }
    }

    private void TryResolvePendingSteamClientConnect()
    {
        if (IsHosting)
            return;

        if (!_pendingSteamClientConnect || _hasRequestedSteamClientStart)
            return;

        if (_currentDiscordLobbyId == 0)
            return;

        if (_currentHostSteamId != 0)
        {
            if (!TryStartSteamClient(_currentHostSteamId))
            {
                HasLobbyNetworkConnectionFailed = true;
                LastLobbyNetworkError = $"Failed to start Steam client. hostSteamId={_currentHostSteamId}";
                Debug.LogWarning($"[Lobby] {LastLobbyNetworkError}");
                Managers.Toast.EnqueueMessage("Failed to connect to lobby host.", 2.5f);
                Managers.Scene.LoadScene(Define.Scene.Intro);
                return;
            }

            _hasRequestedSteamClientStart = true;
            _pendingSteamClientConnect = false;
            return;
        }

        if (Time.unscaledTime >= _pendingSteamClientConnectDeadline)
        {
            HasLobbyNetworkConnectionFailed = true;
            LastLobbyNetworkError = "Discord lobby is missing host SteamID metadata.";
            Debug.LogWarning($"[Lobby] {LastLobbyNetworkError}");
            Managers.Toast.EnqueueMessage("Failed to connect to lobby host.", 2.5f);
            Managers.Scene.LoadScene(Define.Scene.Intro);
        }
    }

    private bool TryStartSteamHost()
    {
        if (!TryResolveNetworkObjects(out NetworkManager networkManager, out SteamNetworkingSocketsTransport steamTransport))
        {
            Debug.LogWarning("[Lobby] TryStartSteamHost failed: TryResolveNetworkObjects returned false.");
            return false;
        }

        if (networkManager.IsListening)
        {
            if (networkManager.IsHost)
                return true;

            networkManager.Shutdown();
            Debug.Log("[Lobby] Existing NetworkManager was listening but not host. Shutdown requested before retry.");
            return false;
        }

        HasLobbyNetworkConnectionFailed = false;
        LastLobbyNetworkError = string.Empty;

        return networkManager.StartHost();
    }

    private bool TryStartSteamClient(ulong hostSteamId)
    {
        if (!TryResolveNetworkObjects(out NetworkManager networkManager, out SteamNetworkingSocketsTransport steamTransport))
            return false;

        if (networkManager.IsListening)
            networkManager.Shutdown();

        HasLobbyNetworkConnectionFailed = false;
        LastLobbyNetworkError = string.Empty;
        _currentHostSteamId = hostSteamId;

        steamTransport.ConnectToSteamID = hostSteamId;

        RegisterClientConnectionCallbacks(networkManager);

        return networkManager.StartClient();
    }

    private void TryStopNetwork()
    {
        NetworkManager networkManager = UnityEngine.Object.FindAnyObjectByType<NetworkManager>();
        if (networkManager == null)
            return;

        networkManager.OnClientConnectedCallback -= HandleClientConnected;
        networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;

        if (networkManager.IsListening)
            networkManager.Shutdown();

        ResetClientConnectionTracking();
    }

    private void ResetClientConnectionTracking()
    {
        _currentHostSteamId = 0;
        HasLobbyNetworkConnectionFailed = false;
        LastLobbyNetworkError = string.Empty;

        _pendingSteamClientConnect = false;
        _hasRequestedSteamClientStart = false;
        _pendingSteamClientConnectDeadline = 0f;
        _hasShownPendingSteamConnectToast = false;
    }

    private static bool TryResolveNetworkObjects(
        out NetworkManager networkManager,
        out SteamNetworkingSocketsTransport steamTransport)
    {
        if (!LobbyNetworkRuntime.EnsureSetup(out networkManager, out steamTransport))
        {
            if (!s_loggedNetcodeMissing)
            {
                Debug.LogWarning("[Lobby] Failed to ensure lobby network runtime.");
                s_loggedNetcodeMissing = true;
            }

            return false;
        }

        if (networkManager == null)
        {
            if (!s_loggedNetworkManagerMissing)
            {
                Debug.LogWarning("[Lobby] NetworkManager is missing after runtime setup.");
                s_loggedNetworkManagerMissing = true;
            }

            return false;
        }

        if (steamTransport == null)
        {
            if (!s_loggedTransportMissing)
            {
                Debug.LogWarning("[Lobby] Steam transport is missing after runtime setup.");
                s_loggedTransportMissing = true;
            }

            return false;
        }

        return true;
    }

    public bool IsLobbyNetworkConnected
    {
        get
        {
            if (!TryResolveNetworkObjects(out NetworkManager networkManager, out _))
                return false;

            if (IsHosting)
                return networkManager.IsHost || networkManager.IsServer;

            return networkManager.IsClient && networkManager.IsConnectedClient;
        }
    }

    private void RegisterClientConnectionCallbacks(NetworkManager networkManager)
    {
        if (networkManager == null)
            return;

        networkManager.OnClientConnectedCallback -= HandleClientConnected;
        networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        networkManager.OnClientConnectedCallback += HandleClientConnected;
        networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
    }

    private void HandleClientConnected(ulong clientId)
    {
        HasLobbyNetworkConnectionFailed = false;
        LastLobbyNetworkError = string.Empty;
        Debug.Log($"[Lobby] Steam client connected. clientId={clientId}");
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (IsHosting) return;

        HasLobbyNetworkConnectionFailed = true;
        LastLobbyNetworkError =
            $"Disconnected from Steam lobby host. hostSteamId={_currentHostSteamId}";

        Debug.LogWarning($"[Lobby] {LastLobbyNetworkError}");
    }
}
