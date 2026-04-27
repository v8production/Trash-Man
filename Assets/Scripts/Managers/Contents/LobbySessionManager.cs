using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class LobbySessionManager
{
    private const string RangerPrefabName = "Ranger(TEMP)";
    private const string LobbyCameraPrefabName = "Lobby_Camera";
    private const int JoinCodeLength = 6;
    private const string VoiceSecretPrefix = "trash-man-lobby";
    private const string LobbyMetadataJoinCode = "join_code";
    private const string LobbyMetadataHostUserId = "host_user_id";

    private const int MaxRelayClientConnections = 4;

    private const string LobbyMetadataRelayJoinCode = "relay_join_code";
    private const string LobbyMetadataRelayConnectionType = "relay_connection_type";

    private const string LobbyMetadataVoiceSecret = "voice_secret";
    private const float LobbyStateSyncIntervalSeconds = 1f;

    private static readonly char[] JoinCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789".ToCharArray();

    private readonly Dictionary<string, RangerController> _rangersByUserId = new();
    private readonly Dictionary<string, UI_Nickname> _nicknamesByUserId = new();

    private string _currentVoiceSecret = string.Empty;
    private ulong _currentDiscordLobbyId;
    private float _nextLobbyStateSyncTime;

    private string _currentRelayJoinCode = string.Empty;

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
        if (_currentDiscordLobbyId == 0)
            return;

        if (Time.unscaledTime < _nextLobbyStateSyncTime)
            return;

        _nextLobbyStateSyncTime = Time.unscaledTime + LobbyStateSyncIntervalSeconds;
        RefreshHostStateFromDiscordMetadata();
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
        _currentDiscordLobbyId = 0;
        _nextLobbyStateSyncTime = 0f;
        ResetNetworkConnectionTracking();
    }

    public static string NormalizeJoinCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string trimmed = value.Trim().ToUpperInvariant();
        return trimmed.Length == JoinCodeLength ? trimmed : string.Empty;
    }

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
        string lobbySecret = BuildVoiceSecret(joinCode);
        _currentVoiceSecret = lobbySecret;
        _nextLobbyStateSyncTime = 0f;

        bool requested = Managers.Discord.CreateOrJoinSessionLobby(
            lobbySecret,
            null,
            BuildLocalMemberMetadata(),
            false,
            (success, lobbyId, error) => HandleDiscordLobbyJoined(success, lobbyId, lobbySecret, false, error));

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

        TryStopUtp();
        Managers.Discord.EndActiveLobbyVoice();
        _rangersByUserId.Clear();
        _nicknamesByUserId.Clear();
        IsHosting = false;
        HostUserId = string.Empty;
        CurrentJoinCode = string.Empty;
        _currentVoiceSecret = string.Empty;
        _currentDiscordLobbyId = 0;
        _nextLobbyStateSyncTime = 0f;
        ResetNetworkConnectionTracking();
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

    public async void BootstrapLocalHostLobby()
    {
        if (!Managers.Discord.IsLinked)
        {
            Debug.LogWarning("Lobby host bootstrap skipped: Discord account is not linked.");
            return;
        }

        CleanupExistingLobbyObjects();

        string joinCode = GenerateUniqueJoinCode();
        string voiceSecret = BuildVoiceSecret(joinCode);

        HostUserId = Managers.Discord.LocalUserId;
        CurrentJoinCode = joinCode;
        _currentVoiceSecret = voiceSecret;
        _nextLobbyStateSyncTime = 0f;
        HasLobbyNetworkConnectionFailed = false;
        LastLobbyNetworkError = string.Empty;

        try
        {
            if (!TryResolveNetworkObjects(out NetworkManager networkManager, out UnityTransport utpTransport))
            {
                Debug.LogWarning("[Lobby] Host bootstrap failed: NetworkManager/UnityTransport not found.");
                Managers.Toast.EnqueueMessage("Failed to start lobby host.\nCheck Netcode/Transport setup.", 3f);
                return;
            }

            _currentRelayJoinCode = await Managers.RelayConnection.StartHostAsync(
                networkManager,
                utpTransport,
                MaxRelayClientConnections);

            IsHosting = true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Lobby] Host bootstrap failed: Relay host did not start. {e}");
            Managers.Toast.EnqueueMessage("Failed to start Relay lobby host.", 3f);
            IsHosting = false;
            HostUserId = string.Empty;
            ResetNetworkConnectionTracking();
            return;
        }

        GUIUtility.systemCopyBuffer = CurrentJoinCode;
        Managers.Toast.EnqueueMessage("Enter code is copied on clipboard.", 2.5f);

        Dictionary<string, string> lobbyMetadata = BuildHostLobbyMetadata(
            CurrentJoinCode,
            _currentRelayJoinCode,
            _currentVoiceSecret,
            HostUserId);

        bool requested = Managers.Discord.CreateOrJoinSessionLobby(
            _currentVoiceSecret,
            lobbyMetadata,
            BuildLocalMemberMetadata(),
            true,
            (success, lobbyId, error) => HandleDiscordLobbyJoined(success, lobbyId, _currentVoiceSecret, true, error));

        if (!requested)
        {
            Debug.LogWarning("[Lobby] Host bootstrap failed: Discord session request not issued.");
            TryStopUtp();
            IsHosting = false;
            HostUserId = string.Empty;
            ResetNetworkConnectionTracking();
        }
    }

    private async void HandleDiscordLobbyJoined(bool success, ulong lobbyId, string lobbySecret, bool requestedAsHost, string error)
    {
        if (!success || lobbyId == 0)
        {
            Debug.LogWarning($"[Lobby] Discord lobby join failed. requestedAsHost={requestedAsHost}, error={error}");
            if (requestedAsHost)
            {
                TryStopUtp();
                IsHosting = false;
                HostUserId = string.Empty;
            }

            Managers.Toast.EnqueueMessage("Lobby join failed. Please try again.", 2.5f);
            Managers.Scene.LoadScene(Define.Scene.Intro);
            return;
        }

        _currentDiscordLobbyId = lobbyId;

        if (Managers.Discord.TryGetSessionLobbyMetadata(lobbyId, out Dictionary<string, string> metadata) && metadata != null)
        {
            if (metadata.TryGetValue(LobbyMetadataJoinCode, out string metadataJoinCode) &&
                !string.IsNullOrWhiteSpace(metadataJoinCode))
                CurrentJoinCode = NormalizeJoinCode(metadataJoinCode);

            if (metadata.TryGetValue(LobbyMetadataHostUserId, out string metadataHostUserId) &&
                !string.IsNullOrWhiteSpace(metadataHostUserId))
                HostUserId = metadataHostUserId;

            if (metadata.TryGetValue(LobbyMetadataRelayJoinCode, out string metadataRelayJoinCode) &&
                !string.IsNullOrWhiteSpace(metadataRelayJoinCode))
                _currentRelayJoinCode = metadataRelayJoinCode;

            if (metadata.TryGetValue(LobbyMetadataVoiceSecret, out string metadataVoiceSecret) &&
                !string.IsNullOrWhiteSpace(metadataVoiceSecret))
                _currentVoiceSecret = metadataVoiceSecret;
        }

        if (string.IsNullOrWhiteSpace(HostUserId))
            HostUserId = requestedAsHost ? Managers.Discord.LocalUserId : string.Empty;

        IsHosting = requestedAsHost || string.Equals(HostUserId, Managers.Discord.LocalUserId, StringComparison.Ordinal);

        if (!IsHosting)
        {
            if (string.IsNullOrWhiteSpace(_currentRelayJoinCode))
            {
                HasLobbyNetworkConnectionFailed = true;
                LastLobbyNetworkError = "Discord lobby is missing Relay join code.";
                Debug.LogWarning($"[Lobby] {LastLobbyNetworkError}");
                Managers.Toast.EnqueueMessage("Failed to connect to lobby host.", 2.5f);
                Managers.Scene.LoadScene(Define.Scene.Intro);
                return;
            }

            try
            {
                if (!TryResolveNetworkObjects(out NetworkManager networkManager, out UnityTransport utpTransport))
                {
                    HasLobbyNetworkConnectionFailed = true;
                    LastLobbyNetworkError = "NetworkManager/UnityTransport not found.";
                    Managers.Toast.EnqueueMessage("Failed to connect to lobby host.", 2.5f);
                    Managers.Scene.LoadScene(Define.Scene.Intro);
                    return;
                }

                await Managers.RelayConnection.StartClientAsync(
                    networkManager,
                    utpTransport,
                    _currentRelayJoinCode);

                RegisterClientConnectionCallbacks(networkManager);
            }
            catch (Exception e)
            {
                HasLobbyNetworkConnectionFailed = true;
                LastLobbyNetworkError = $"Relay client start failed: {e.Message}";
                Debug.LogWarning($"[Lobby] {LastLobbyNetworkError}");
                Managers.Toast.EnqueueMessage("Failed to connect to Relay lobby.", 2.5f);
                Managers.Scene.LoadScene(Define.Scene.Intro);
                return;
            }
        }

        Managers.Discord.NotifyLobbyUserJoined(Managers.Discord.LocalUserId, Managers.Discord.LocalDisplayName, true);
        Managers.Discord.EnsureLobbyVoiceConnected(_currentVoiceSecret);
        _nextLobbyStateSyncTime = 0f;

        Debug.Log($"[Lobby] Discord lobby ready. lobbyId={_currentDiscordLobbyId}, joinCode={CurrentJoinCode}, host={HostUserId}, localHosting={IsHosting}");
    }

    private static Dictionary<string, string> BuildHostLobbyMetadata(
        string joinCode,
        string relayJoinCode,
        string voiceSecret,
        string hostUserId)
    {
        return new Dictionary<string, string>
        {
            [LobbyMetadataJoinCode] = joinCode,
            [LobbyMetadataHostUserId] = hostUserId,
            [LobbyMetadataRelayJoinCode] = relayJoinCode,
            [LobbyMetadataRelayConnectionType] = RelayConnectionManager.DefaultConnectionType,
            [LobbyMetadataVoiceSecret] = voiceSecret,
        };
    }

    private static Dictionary<string, string> BuildLocalMemberMetadata()
    {
        return new Dictionary<string, string>
        {
            ["discord_user_id"] = Managers.Discord.LocalUserId,
            ["display_name"] = Managers.Discord.LocalDisplayName,
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

    // private RangerController SpawnRangerForLocalUser()
    // {
    //     GameObject rangerObject = Managers.Resource.Instantiate(RangerPrefabName);
    //     if (rangerObject == null)
    //     {
    //         Debug.LogError($"Lobby host bootstrap failed: Prefabs/{RangerPrefabName} not found.");
    //         return null;
    //     }

    //     RangerController ranger = rangerObject.GetComponent<RangerController>();
    //     if (ranger == null)
    //     {
    //         Debug.LogError("Lobby host bootstrap failed: Ranger prefab is missing RangerController.");
    //         return null;
    //     }

    //     _rangersByUserId[Managers.Discord.LocalUserId] = ranger;
    //     LobbyScene.RegisterUserObjects(Managers.Discord.LocalUserId, ranger, null);
    //     return ranger;
    // }

    // private static void SetupLobbyCamera(RangerController ranger)
    // {
    //     GameObject cameraObject = Managers.Resource.Instantiate(LobbyCameraPrefabName);
    //     if (cameraObject == null)
    //     {
    //         Debug.LogError($"Lobby host bootstrap failed: Prefabs/{LobbyCameraPrefabName} not found.");
    //         return;
    //     }

    //     LobbyCameraController lobbyCamera = cameraObject.GetComponent<LobbyCameraController>();
    //     if (lobbyCamera == null)
    //     {
    //         Debug.LogError("Lobby host bootstrap failed: Lobby_Camera prefab is missing LobbyCameraController.");
    //         return;
    //     }

    //     if (ranger != null)
    //         lobbyCamera.SetTarget(ranger.transform);
    // }

    // private void SetupNicknameForLocalUser(RangerController ranger)
    // {
    //     if (ranger == null)
    //         return;

    //     UI_Nickname nicknameUI = Managers.UI.CreateWorldSpaceUI<UI_Nickname>(ranger.transform, nameof(UI_Nickname));
    //     if (nicknameUI == null)
    //     {
    //         Debug.LogError("Lobby host bootstrap failed: UI_Nickname creation returned null.");
    //         return;
    //     }

    //     nicknameUI.SetText(Managers.Discord.LocalDisplayName);
    //     nicknameUI.SetVoiceChatActive(Managers.Discord.IsLobbyUserVoiceChatActive(Managers.Discord.LocalUserId));
    //     _nicknamesByUserId[Managers.Discord.LocalUserId] = nicknameUI;
    //     LobbyScene.RegisterUserObjects(Managers.Discord.LocalUserId, ranger, nicknameUI);
    // }

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

    // private static ushort CalculatePort(string joinCode)
    // {
    //     unchecked
    //     {
    //         int hash = 17;
    //         for (int i = 0; i < joinCode.Length; i++)
    //             hash = (hash * 31) + joinCode[i];

    //         int offset = Mathf.Abs(hash % LobbyPortRange);
    //         return (ushort)(BaseLobbyPort + offset);
    //     }
    // }

    private static string BuildVoiceSecret(string joinCode)
    {
        return $"{VoiceSecretPrefix}-{joinCode.ToLowerInvariant()}";
    }

    // private static string ResolveConfiguredHostAddress()
    // {
    //     string detected = TryDetectLocalIPv4Address();
    //     return string.IsNullOrWhiteSpace(detected) ? DefaultHostAddress : detected;
    // }

    // private static string TryDetectLocalIPv4Address()
    // {
    //     try
    //     {
    //         using Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    //         socket.Connect("8.8.8.8", 65530);
    //         if (socket.LocalEndPoint is IPEndPoint endpoint && endpoint.Address != null)
    //             return endpoint.Address.ToString();
    //     }
    //     catch
    //     {
    //     }

    //     try
    //     {
    //         IPHostEntry entry = Dns.GetHostEntry(Dns.GetHostName());
    //         for (int i = 0; i < entry.AddressList.Length; i++)
    //         {
    //             IPAddress address = entry.AddressList[i];
    //             if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
    //                 continue;

    //             return address.ToString();
    //         }
    //     }
    //     catch
    //     {
    //     }

    //     return string.Empty;
    // }

    private void HandleSessionLobbyUpdated(ulong lobbyId)
    {
        if (lobbyId != _currentDiscordLobbyId)
            return;

        _nextLobbyStateSyncTime = 0f;
    }

    private void HandleSessionLobbyMemberAdded(ulong lobbyId, ulong memberId)
    {
        if (lobbyId != _currentDiscordLobbyId)
            return;

        _nextLobbyStateSyncTime = 0f;
    }

    private void HandleSessionLobbyMemberRemoved(ulong lobbyId, ulong memberId)
    {
        if (lobbyId != _currentDiscordLobbyId)
            return;

        _nextLobbyStateSyncTime = 0f;
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

        if (metadata.TryGetValue(LobbyMetadataRelayJoinCode, out string relayJoinCode) &&
            !string.IsNullOrWhiteSpace(relayJoinCode))
            _currentRelayJoinCode = relayJoinCode;

        if (metadata.TryGetValue(LobbyMetadataVoiceSecret, out string metadataVoiceSecret) &&
            !string.IsNullOrWhiteSpace(metadataVoiceSecret))
            _currentVoiceSecret = metadataVoiceSecret;

        if (metadata.TryGetValue(LobbyMetadataHostUserId, out string metadataHostUserId) &&
            !string.IsNullOrWhiteSpace(metadataHostUserId))
            HostUserId = metadataHostUserId;
    }

    // private static string SelectHostUserId(ulong[] memberIds, string preferredHostUserId)
    // {
    //     if (!string.IsNullOrWhiteSpace(preferredHostUserId) && ulong.TryParse(preferredHostUserId, out ulong preferredId))
    //     {
    //         for (int i = 0; i < memberIds.Length; i++)
    //         {
    //             if (memberIds[i] == preferredId)
    //                 return preferredHostUserId;
    //         }
    //     }

    //     ulong selected = memberIds[0];
    //     for (int i = 1; i < memberIds.Length; i++)
    //     {
    //         if (memberIds[i] < selected)
    //             selected = memberIds[i];
    //     }

    //     return selected.ToString();
    // }

    // private void HandleHostMetadataPublishCompleted(bool success, ulong lobbyId, string error)
    // {
    //     _isUpdatingHostMetadata = false;

    //     if (!success)
    //     {
    //         Debug.LogWarning($"[Lobby] Failed to publish promoted host metadata: {error}");
    //         return;
    //     }

    //     _currentDiscordLobbyId = lobbyId;
    //     HostUserId = Managers.Discord.LocalUserId;
    //     _nextLobbyStateSyncTime = 0f;
    // }

    // private bool TryStartUtpHost(ushort port, out string hostAddress)
    // {
    //     hostAddress = DefaultHostAddress;
    //     try
    //     {
    //         if (!TryResolveNetworkObjects(out NetworkManager networkManager, out UnityTransport utpTransport))
    //             return false;

    //         if (networkManager.IsListening)
    //         {
    //             if (networkManager.IsServer)
    //             {
    //                 hostAddress = ResolveConfiguredHostAddress();
    //                 ResetNetworkConnectionTracking();
    //                 return true;
    //             }

    //             networkManager.Shutdown();
    //         }

    //         ConfigureTransportConnection(utpTransport, "0.0.0.0", port);
    //         hostAddress = ResolveConfiguredHostAddress();

    //         bool started = networkManager.StartHost();
    //         if (!started)
    //         {
    //             Debug.LogWarning($"UTP host start returned false. isListening={networkManager.IsListening}, isServer={networkManager.IsServer}, isClient={networkManager.IsClient}, port={port}");
    //             return false;
    //         }

    //         ResetNetworkConnectionTracking();
    //         return true;
    //     }
    //     catch (Exception e)
    //     {
    //         Debug.LogWarning($"UTP host start failed: {e}");
    //         return false;
    //     }
    // }

    // private bool TryStartUtpClient(string hostAddress, ushort port)
    // {
    //     HasLobbyNetworkConnectionFailed = false;
    //     LastLobbyNetworkError = string.Empty;

    //     string targetHost = NormalizeHostAddress(hostAddress);
    //     if (port == 0 || string.IsNullOrWhiteSpace(targetHost))
    //     {
    //         Debug.LogWarning($"[Lobby] StartClient skipped: invalid endpoint host={targetHost}, port={port}");
    //         return false;
    //     }

    //     try
    //     {
    //         if (!TryResolveNetworkObjects(out NetworkManager networkManager, out UnityTransport utpTransport))
    //             return false;

    //         bool sameEndpoint = string.Equals(_activeClientHostAddress, targetHost, StringComparison.OrdinalIgnoreCase)
    //             && _activeClientPort == port;

    //         if (networkManager.IsListening)
    //         {
    //             if (networkManager.IsClient && !networkManager.IsServer)
    //             {
    //                 if (sameEndpoint)
    //                 {
    //                     Debug.Log($"[Lobby] StartClient skipped: already using host={targetHost}, port={port}");
    //                     return true;
    //                 }

    //                 networkManager.Shutdown();
    //             }
    //             else
    //             {
    //                 networkManager.Shutdown();
    //             }
    //         }

    //         ConfigureTransportConnection(utpTransport, targetHost, port);

    //         bool started = networkManager.StartClient();
    //         Debug.Log($"[Lobby] StartClient requested. host={targetHost}, port={port}, started={started}");
    //         if (!started)
    //             return false;
    //         networkManager.OnClientConnectedCallback -= HandleClientConnected;
    //         networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
    //         networkManager.OnClientConnectedCallback += HandleClientConnected;
    //         networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
    //         _activeClientHostAddress = targetHost;
    //         _activeClientPort = port;
    //         return true;
    //     }
    //     catch (Exception e)
    //     {
    //         Debug.LogWarning($"UTP client start failed: {e.Message}");
    //         return false;
    //     }
    // }

    private bool TryStopUtp()
    {
        try
        {
            if (!TryResolveNetworkObjects(out NetworkManager networkManager, out _))
                return false;

            if (!networkManager.IsListening)
            {
                ResetNetworkConnectionTracking();
                return true;
            }

            networkManager.OnClientConnectedCallback -= HandleClientConnected;
            networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            networkManager.Shutdown();
            ResetNetworkConnectionTracking();
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"UTP stop failed: {e.Message}");
            return false;
        }
    }

    // private static string NormalizeHostAddress(string hostAddress)
    // {
    //     return string.IsNullOrWhiteSpace(hostAddress) ? DefaultHostAddress : hostAddress.Trim();
    // }

    private void ResetNetworkConnectionTracking()
    {
        _currentRelayJoinCode = string.Empty;
    }

    private static bool TryResolveNetworkObjects(out NetworkManager networkManager, out UnityTransport utpTransport)
    {
        if (!LobbyNetworkRuntime.EnsureSetup(out networkManager, out UnityTransport transport))
        {
            networkManager = null;
            utpTransport = null;
            Debug.LogWarning("UTP operation skipped: runtime NGO bootstrap failed.");
            return false;
        }

        utpTransport = transport;

        if (networkManager == null)
        {
            if (!s_loggedNetworkManagerMissing)
            {
                Debug.LogWarning("UTP operation skipped: no NetworkManager found in Lobby scene.");
                s_loggedNetworkManagerMissing = true;
            }
            return false;
        }

        if (utpTransport == null)
        {
            if (!s_loggedTransportMissing)
            {
                Debug.LogWarning("UTP operation skipped: UnityTransport component is missing.");
                s_loggedTransportMissing = true;
            }
            return false;
        }

        return true;
    }

    // private static void ConfigureTransportConnection(Component utpTransport, string hostAddress, ushort port)
    // {
    //     MethodInfo[] methods = utpTransport.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
    //     MethodInfo setConnectionData = methods.FirstOrDefault(m =>
    //     {
    //         if (m.Name != "SetConnectionData")
    //             return false;

    //         ParameterInfo[] parameters = m.GetParameters();
    //         return parameters.Length >= 2
    //             && parameters[0].ParameterType == typeof(string)
    //             && parameters[1].ParameterType == typeof(ushort);
    //     });

    //     if (setConnectionData == null)
    //         return;

    //     ParameterInfo[] methodParameters = setConnectionData.GetParameters();
    //     if (methodParameters.Length >= 3)
    //         setConnectionData.Invoke(utpTransport, new object[] { hostAddress, port, "0.0.0.0" });
    //     else
    //         setConnectionData.Invoke(utpTransport, new object[] { hostAddress, port });
    // }

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

    // private static bool IsRemoteUnsafeHostAddress(string hostAddress)
    // {
    //     if (string.IsNullOrWhiteSpace(hostAddress))
    //         return true;

    //     if (!IPAddress.TryParse(hostAddress, out IPAddress address))
    //         return false;

    //     return IPAddress.IsLoopback(address);
    // }

    private static void RegisterClientConnectionCallbacks(NetworkManager networkManager)
    {
        if (networkManager == null)
            return;

        LobbySessionManager session = Managers.LobbySession;

        networkManager.OnClientConnectedCallback -= session.HandleClientConnected;
        networkManager.OnClientDisconnectCallback -= session.HandleClientDisconnected;
        networkManager.OnClientConnectedCallback += session.HandleClientConnected;
        networkManager.OnClientDisconnectCallback += session.HandleClientDisconnected;
    }

    private void HandleClientConnected(ulong clientId)
    {
        HasLobbyNetworkConnectionFailed = false;
        LastLobbyNetworkError = string.Empty;
        Debug.Log($"[Lobby] UTP client connected. clientId={clientId}");
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (IsHosting) return;

        HasLobbyNetworkConnectionFailed = true;
        LastLobbyNetworkError =
            $"Disconnected from lobby host. joinCode={_currentRelayJoinCode}";

        Debug.LogWarning($"[Lobby] {LastLobbyNetworkError}");
    }
}
