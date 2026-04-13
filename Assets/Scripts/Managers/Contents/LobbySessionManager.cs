using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class LobbySessionManager
{
    private const string RangerPrefabName = "Ranger(TEMP)";
    private const string LobbyCameraPrefabName = "Lobby_Camera";
    private const int JoinCodeLength = 6;
    private const ushort BaseLobbyPort = 18000;
    private const ushort LobbyPortRange = 2000;
    private const string DefaultHostAddress = "127.0.0.1";
    private const string VoiceSecretPrefix = "trash-man-lobby";
    private const string LobbyMetadataJoinCode = "join_code";
    private const string LobbyMetadataHostUserId = "host_user_id";
    private const string LobbyMetadataHostAddress = "host_address";
    private const string LobbyMetadataUtpPort = "utp_port";
    private const string LobbyMetadataVoiceSecret = "voice_secret";
    private const float LobbyStateSyncIntervalSeconds = 1f;

    private static readonly char[] JoinCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789".ToCharArray();

    private readonly Dictionary<string, RangerController> _rangersByUserId = new();
    private readonly Dictionary<string, UI_Nickname> _nicknamesByUserId = new();

    private string _currentVoiceSecret = string.Empty;
    private ushort _currentPort;
    private string _currentHostAddress = DefaultHostAddress;
    private ulong _currentDiscordLobbyId;
    private float _nextLobbyStateSyncTime;
    private bool _isUpdatingHostMetadata;

    private static bool s_loggedNetcodeMissing;
    private static bool s_loggedNetworkManagerMissing;
    private static bool s_loggedTransportMissing;

    public bool IsHosting { get; private set; }
    public string HostUserId { get; private set; } = string.Empty;
    public string CurrentJoinCode { get; private set; } = string.Empty;
    public string CurrentVoiceSecret => _currentVoiceSecret;

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
        _currentPort = 0;
        _currentHostAddress = DefaultHostAddress;
        _currentDiscordLobbyId = 0;
        _nextLobbyStateSyncTime = 0f;
        _isUpdatingHostMetadata = false;
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

        if (ranger != null)
            _rangersByUserId[userId] = ranger;

        if (nickname != null)
            _nicknamesByUserId[userId] = nickname;

        LobbyScene.RegisterUserObjects(userId, ranger, nickname);
    }

    public void UnregisterLobbyUserObjects(string userId, RangerController ranger, UI_Nickname nickname)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        if (_rangersByUserId.TryGetValue(userId, out RangerController storedRanger) && storedRanger == ranger)
            _rangersByUserId.Remove(userId);

        if (_nicknamesByUserId.TryGetValue(userId, out UI_Nickname storedNickname) && storedNickname == nickname)
            _nicknamesByUserId.Remove(userId);
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
        _currentPort = 0;
        _currentHostAddress = DefaultHostAddress;
        _currentDiscordLobbyId = 0;
        _nextLobbyStateSyncTime = 0f;
        _isUpdatingHostMetadata = false;
    }

    public void SetRangerNicknameVoiceActive(string userId, bool isVoiceChatActive)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        if (LobbyScene.TrySetNicknameSpeakerActive(userId, isVoiceChatActive))
            return;

        if (!_rangersByUserId.TryGetValue(userId, out RangerController ranger) || ranger == null)
        {
            Debug.Log($"[LobbyVoice] Ranger not found for userId={userId}");
            return;
        }

        UI_Nickname nicknameUI = ranger.GetComponentInChildren<UI_Nickname>(true);
        if (nicknameUI == null)
        {
            Debug.Log($"[LobbyVoice] UI_Nickname not found under Ranger. userId={userId}");
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

        CleanupExistingLobbyObjects();

        string joinCode = GenerateUniqueJoinCode();
        ushort roomPort = CalculatePort(joinCode);
        string voiceSecret = BuildVoiceSecret(joinCode);

        HostUserId = Managers.Discord.LocalUserId;
        IsHosting = TryStartUtpHost(roomPort, out string hostAddress);
        if (!IsHosting)
        {
            Debug.LogWarning("[Lobby] Host bootstrap failed: UTP host did not start.");
            Managers.Toast.EnqueueMessage("Failed to start lobby host. Check Netcode/Transport setup.", 3f);
            return;
        }

        CurrentJoinCode = joinCode;
        _currentPort = roomPort;
        _currentHostAddress = string.IsNullOrWhiteSpace(hostAddress) ? ResolveConfiguredHostAddress() : hostAddress;
        _currentVoiceSecret = voiceSecret;
        _nextLobbyStateSyncTime = 0f;
        GUIUtility.systemCopyBuffer = CurrentJoinCode;
        Managers.Toast.EnqueueMessage("Enter code is copied on clipboard.", 2.5f);

        Dictionary<string, string> lobbyMetadata = BuildHostLobbyMetadata(CurrentJoinCode, _currentHostAddress, _currentPort, _currentVoiceSecret, HostUserId);
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
        }
    }

    private void HandleDiscordLobbyJoined(bool success, ulong lobbyId, string lobbySecret, bool requestedAsHost, string error)
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
            if (metadata.TryGetValue(LobbyMetadataJoinCode, out string metadataJoinCode) && !string.IsNullOrWhiteSpace(metadataJoinCode))
                CurrentJoinCode = NormalizeJoinCode(metadataJoinCode);

            if (metadata.TryGetValue(LobbyMetadataHostUserId, out string metadataHostUserId) && !string.IsNullOrWhiteSpace(metadataHostUserId))
                HostUserId = metadataHostUserId;

            if (metadata.TryGetValue(LobbyMetadataHostAddress, out string metadataHostAddress) && !string.IsNullOrWhiteSpace(metadataHostAddress))
                _currentHostAddress = metadataHostAddress;

            if (metadata.TryGetValue(LobbyMetadataUtpPort, out string metadataPort) && ushort.TryParse(metadataPort, out ushort parsedPort))
                _currentPort = parsedPort;

            if (metadata.TryGetValue(LobbyMetadataVoiceSecret, out string metadataVoiceSecret) && !string.IsNullOrWhiteSpace(metadataVoiceSecret))
                _currentVoiceSecret = metadataVoiceSecret;
        }

        if (string.IsNullOrWhiteSpace(HostUserId))
            HostUserId = requestedAsHost ? Managers.Discord.LocalUserId : string.Empty;

        IsHosting = requestedAsHost || string.Equals(HostUserId, Managers.Discord.LocalUserId, StringComparison.Ordinal);

        if (!IsHosting && !TryStartUtpClient(_currentHostAddress, _currentPort))
        {
            Debug.LogWarning($"[Lobby] Failed to start UTP client. host={_currentHostAddress}, port={_currentPort}");
            Managers.Toast.EnqueueMessage("Failed to connect to lobby host.", 2.5f);
            Managers.Scene.LoadScene(Define.Scene.Intro);
            return;
        }

        Managers.Discord.NotifyLobbyUserJoined(Managers.Discord.LocalUserId, Managers.Discord.LocalDisplayName, true);
        Managers.Discord.EnsureLobbyVoiceConnected(_currentVoiceSecret);
        _nextLobbyStateSyncTime = 0f;

        Debug.Log($"[Lobby] Discord lobby ready. lobbyId={_currentDiscordLobbyId}, joinCode={CurrentJoinCode}, host={HostUserId}, localHosting={IsHosting}");
    }

    private static Dictionary<string, string> BuildHostLobbyMetadata(string joinCode, string hostAddress, ushort port, string voiceSecret, string hostUserId)
    {
        return new Dictionary<string, string>
        {
            [LobbyMetadataJoinCode] = joinCode,
            [LobbyMetadataHostUserId] = hostUserId,
            [LobbyMetadataHostAddress] = hostAddress,
            [LobbyMetadataUtpPort] = port.ToString(),
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
        RangerController[] rangers = UnityEngine.Object.FindObjectsByType<RangerController>(FindObjectsSortMode.None);
        for (int i = 0; i < rangers.Length; i++)
            UnityEngine.Object.Destroy(rangers[i].gameObject);

        LobbyCameraController[] cameras = UnityEngine.Object.FindObjectsByType<LobbyCameraController>(FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
            UnityEngine.Object.Destroy(cameras[i].gameObject);
    }

    private RangerController SpawnRangerForLocalUser()
    {
        GameObject rangerObject = Managers.Resource.Instantiate(RangerPrefabName);
        if (rangerObject == null)
        {
            Debug.LogError($"Lobby host bootstrap failed: Prefabs/{RangerPrefabName} not found.");
            return null;
        }

        RangerController ranger = rangerObject.GetComponent<RangerController>();
        if (ranger == null)
        {
            Debug.LogError("Lobby host bootstrap failed: Ranger prefab is missing RangerController.");
            return null;
        }

        _rangersByUserId[Managers.Discord.LocalUserId] = ranger;
        LobbyScene.RegisterUserObjects(Managers.Discord.LocalUserId, ranger, null);
        return ranger;
    }

    private static void SetupLobbyCamera(RangerController ranger)
    {
        GameObject cameraObject = Managers.Resource.Instantiate(LobbyCameraPrefabName);
        if (cameraObject == null)
        {
            Debug.LogError($"Lobby host bootstrap failed: Prefabs/{LobbyCameraPrefabName} not found.");
            return;
        }

        LobbyCameraController lobbyCamera = cameraObject.GetComponent<LobbyCameraController>();
        if (lobbyCamera == null)
        {
            Debug.LogError("Lobby host bootstrap failed: Lobby_Camera prefab is missing LobbyCameraController.");
            return;
        }

        if (ranger != null)
            lobbyCamera.SetTarget(ranger.transform);
    }

    private void SetupNicknameForLocalUser(RangerController ranger)
    {
        if (ranger == null)
            return;

        UI_Nickname nicknameUI = Managers.UI.CreateWorldSpaceUI<UI_Nickname>(ranger.transform, nameof(UI_Nickname));
        if (nicknameUI == null)
        {
            Debug.LogError("Lobby host bootstrap failed: UI_Nickname creation returned null.");
            return;
        }

        nicknameUI.SetText(Managers.Discord.LocalDisplayName);
        nicknameUI.SetVoiceChatActive(Managers.Discord.IsLobbyUserVoiceChatActive(Managers.Discord.LocalUserId));
        _nicknamesByUserId[Managers.Discord.LocalUserId] = nicknameUI;
        LobbyScene.RegisterUserObjects(Managers.Discord.LocalUserId, ranger, nicknameUI);
    }

    private void HandleLocalDisplayNameChanged(string displayName)
    {
        if (_nicknamesByUserId.TryGetValue(Managers.Discord.LocalUserId, out UI_Nickname nicknameUI) && nicknameUI != null)
            nicknameUI.SetText(displayName);
    }

    private void HandleLobbyUserVoiceChatStateChanged(string userId, bool isActive)
    {
        Debug.Log($"[LobbyVoice] Lobby user speaking indicator event. userId={userId}, speaking={isActive}");
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

    private static ushort CalculatePort(string joinCode)
    {
        unchecked
        {
            int hash = 17;
            for (int i = 0; i < joinCode.Length; i++)
                hash = (hash * 31) + joinCode[i];

            int offset = Mathf.Abs(hash % LobbyPortRange);
            return (ushort)(BaseLobbyPort + offset);
        }
    }

    private static string BuildVoiceSecret(string joinCode)
    {
        return $"{VoiceSecretPrefix}-{joinCode.ToLowerInvariant()}";
    }

    private static string ResolveConfiguredHostAddress()
    {
        string detected = TryDetectLocalIPv4Address();
        return string.IsNullOrWhiteSpace(detected) ? DefaultHostAddress : detected;
    }

    private static string TryDetectLocalIPv4Address()
    {
        try
        {
            using Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint endpoint && endpoint.Address != null)
                return endpoint.Address.ToString();
        }
        catch
        {
        }

        try
        {
            IPHostEntry entry = Dns.GetHostEntry(Dns.GetHostName());
            for (int i = 0; i < entry.AddressList.Length; i++)
            {
                IPAddress address = entry.AddressList[i];
                if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
                    continue;

                return address.ToString();
            }
        }
        catch
        {
        }

        return string.Empty;
    }

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

        if (!Managers.Discord.TryGetSessionLobbyMetadata(_currentDiscordLobbyId, out Dictionary<string, string> metadata) || metadata == null)
            return;

        if (metadata.TryGetValue(LobbyMetadataJoinCode, out string metadataJoinCode) && !string.IsNullOrWhiteSpace(metadataJoinCode))
            CurrentJoinCode = NormalizeJoinCode(metadataJoinCode);

        if (metadata.TryGetValue(LobbyMetadataHostAddress, out string metadataHostAddress) && !string.IsNullOrWhiteSpace(metadataHostAddress))
            _currentHostAddress = metadataHostAddress;

        if (metadata.TryGetValue(LobbyMetadataUtpPort, out string metadataPort) && ushort.TryParse(metadataPort, out ushort parsedPort))
            _currentPort = parsedPort;

        if (metadata.TryGetValue(LobbyMetadataVoiceSecret, out string metadataVoiceSecret) && !string.IsNullOrWhiteSpace(metadataVoiceSecret))
            _currentVoiceSecret = metadataVoiceSecret;

        string metadataHostUserId = metadata.TryGetValue(LobbyMetadataHostUserId, out string storedHostUserId) ? storedHostUserId : string.Empty;

        if (!Managers.Discord.TryGetSessionLobbyMemberIds(_currentDiscordLobbyId, out ulong[] memberIds) || memberIds == null || memberIds.Length == 0)
            return;

        string electedHostUserId = SelectHostUserId(memberIds, metadataHostUserId);
        if (string.IsNullOrWhiteSpace(electedHostUserId))
            return;

        HostUserId = electedHostUserId;
        bool localShouldHost = string.Equals(HostUserId, Managers.Discord.LocalUserId, StringComparison.Ordinal);

        if (!string.Equals(metadataHostUserId, electedHostUserId, StringComparison.Ordinal) && localShouldHost)
        {
            PublishHostMetadataAsOwner();
            return;
        }

        if (localShouldHost)
        {
            if (!IsHosting)
            {
                bool startedHost = TryStartUtpHost(_currentPort, out string hostAddress);
                if (startedHost)
                {
                    IsHosting = true;
                    _currentHostAddress = hostAddress;
                    Debug.Log($"[Lobby] Host promoted via Discord metadata. joinCode={CurrentJoinCode}, host={HostUserId}");
                }
            }

            return;
        }

        if (IsHosting)
        {
            TryStopUtp();
            IsHosting = false;
        }

        TryStartUtpClient(_currentHostAddress, _currentPort);
    }

    private static string SelectHostUserId(ulong[] memberIds, string preferredHostUserId)
    {
        if (!string.IsNullOrWhiteSpace(preferredHostUserId) && ulong.TryParse(preferredHostUserId, out ulong preferredId))
        {
            for (int i = 0; i < memberIds.Length; i++)
            {
                if (memberIds[i] == preferredId)
                    return preferredHostUserId;
            }
        }

        ulong selected = memberIds[0];
        for (int i = 1; i < memberIds.Length; i++)
        {
            if (memberIds[i] < selected)
                selected = memberIds[i];
        }

        return selected.ToString();
    }

    private void PublishHostMetadataAsOwner()
    {
        if (_isUpdatingHostMetadata)
            return;

        _isUpdatingHostMetadata = true;

        if (_currentPort == 0)
            _currentPort = CalculatePort(CurrentJoinCode);

        string hostAddress = ResolveConfiguredHostAddress();
        bool startedHost = IsHosting || TryStartUtpHost(_currentPort, out hostAddress);
        if (!startedHost)
        {
            _isUpdatingHostMetadata = false;
            return;
        }

        IsHosting = true;
        _currentHostAddress = hostAddress;

        Dictionary<string, string> lobbyMetadata = BuildHostLobbyMetadata(CurrentJoinCode, _currentHostAddress, _currentPort, _currentVoiceSecret, Managers.Discord.LocalUserId);
        bool requested = Managers.Discord.CreateOrJoinSessionLobby(
            _currentVoiceSecret,
            lobbyMetadata,
            BuildLocalMemberMetadata(),
            true,
            HandleHostMetadataPublishCompleted);

        if (!requested)
            _isUpdatingHostMetadata = false;
    }

    private void HandleHostMetadataPublishCompleted(bool success, ulong lobbyId, string error)
    {
        _isUpdatingHostMetadata = false;

        if (!success)
        {
            Debug.LogWarning($"[Lobby] Failed to publish promoted host metadata: {error}");
            return;
        }

        _currentDiscordLobbyId = lobbyId;
        HostUserId = Managers.Discord.LocalUserId;
        _nextLobbyStateSyncTime = 0f;
    }

    private bool TryStartUtpHost(ushort port, out string hostAddress)
    {
        hostAddress = DefaultHostAddress;
        try
        {
            if (!TryResolveNetworkObjects(out Type networkManagerType, out MonoBehaviour networkManagerObject, out Component utpTransport))
                return false;

            ConfigureTransportConnection(utpTransport, "0.0.0.0", port);
            hostAddress = ResolveConfiguredHostAddress();

            MethodInfo startHostMethod = networkManagerType.GetMethod("StartHost", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (startHostMethod == null)
            {
                Debug.LogWarning("UTP host start skipped: StartHost method not found on NetworkManager.");
                return false;
            }

            object startedObject = startHostMethod.Invoke(networkManagerObject, null);
            return startedObject is bool started && started;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"UTP host start failed: {e.Message}");
            return false;
        }
    }

    private bool TryStartUtpClient(string hostAddress, ushort port)
    {
        try
        {
            if (!TryResolveNetworkObjects(out Type networkManagerType, out MonoBehaviour networkManagerObject, out Component utpTransport))
                return false;

            ConfigureTransportConnection(utpTransport, hostAddress, port);

            MethodInfo startClientMethod = networkManagerType.GetMethod("StartClient", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (startClientMethod == null)
            {
                Debug.LogWarning("UTP client start skipped: StartClient method not found on NetworkManager.");
                return false;
            }

            object startedObject = startClientMethod.Invoke(networkManagerObject, null);
            bool started = startedObject is bool boolValue && boolValue;
            Debug.Log($"[Lobby] StartClient requested. host={hostAddress}, port={port}, started={started}");
            return started;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"UTP client start failed: {e.Message}");
            return false;
        }
    }

    private bool TryStopUtp()
    {
        try
        {
            if (!TryResolveNetworkObjects(out _, out MonoBehaviour networkManagerObject, out _))
                return false;

            MethodInfo shutdownMethod = networkManagerObject.GetType().GetMethod("Shutdown", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (shutdownMethod == null)
            {
                Debug.LogWarning("UTP stop skipped: Shutdown method not found on NetworkManager.");
                return false;
            }

            shutdownMethod.Invoke(networkManagerObject, null);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"UTP stop failed: {e.Message}");
            return false;
        }
    }

    private static bool TryResolveNetworkObjects(out Type networkManagerType, out MonoBehaviour networkManagerObject, out Component utpTransport)
    {
        if (!LobbyNetworkRuntime.EnsureSetup(out NetworkManager networkManager, out UnityTransport transport))
        {
            networkManagerType = null;
            networkManagerObject = null;
            utpTransport = null;
            Debug.LogWarning("UTP operation skipped: runtime NGO bootstrap failed.");
            return false;
        }

        networkManagerType = typeof(NetworkManager);
        networkManagerObject = networkManager;
        utpTransport = transport;

        if (networkManagerObject == null)
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

    private static void ConfigureTransportConnection(Component utpTransport, string hostAddress, ushort port)
    {
        MethodInfo[] methods = utpTransport.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
        MethodInfo setConnectionData = methods.FirstOrDefault(m =>
        {
            if (m.Name != "SetConnectionData")
                return false;

            ParameterInfo[] parameters = m.GetParameters();
            return parameters.Length >= 2
                && parameters[0].ParameterType == typeof(string)
                && parameters[1].ParameterType == typeof(ushort);
        });

        if (setConnectionData == null)
            return;

        ParameterInfo[] methodParameters = setConnectionData.GetParameters();
        if (methodParameters.Length >= 3)
            setConnectionData.Invoke(utpTransport, new object[] { hostAddress, port, "0.0.0.0" });
        else
            setConnectionData.Invoke(utpTransport, new object[] { hostAddress, port });
    }
}
