using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

public class LobbySessionManager
{
    private const string RangerPrefabName = "Ranger(TEMP)";
    private const string LobbyCameraPrefabName = "Lobby_Camera";
    private const int JoinCodeLength = 6;
    private const ushort BaseLobbyPort = 18000;
    private const ushort LobbyPortRange = 2000;
    private const string DefaultHostAddress = "127.0.0.1";
    private const string LobbyHostAddressKey = "LOBBY_HOST_ADDRESS";
    private const string VoiceSecretPrefix = "trash-man-lobby";

    private static readonly char[] JoinCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789".ToCharArray();

    private readonly Dictionary<string, RangerController> _rangersByUserId = new();
    private readonly Dictionary<string, UI_Nickname> _nicknamesByUserId = new();
    private static readonly Dictionary<string, LobbyRoomData> s_roomByJoinCode = new();

    private string _currentVoiceSecret = string.Empty;
    private ushort _currentPort;
    private string _currentHostAddress = DefaultHostAddress;
    private string _lastHostPromotionAttemptCode = string.Empty;
    private bool _hostPromotionAttemptedForCurrentCode;

    private static bool s_loggedNetcodeMissing;
    private static bool s_loggedNetworkManagerMissing;
    private static bool s_loggedTransportMissing;

    private sealed class LobbyRoomData
    {
        public string JoinCode;
        public string HostUserId;
        public string VoiceSecret;
        public string HostAddress;
        public ushort Port;
        public readonly List<string> JoinOrder = new();
    }

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
        Debug.Log("[LobbyVoice] LobbySessionManager subscribed to lobby voice state events.");
    }

    public void OnUpdate()
    {
        if (string.IsNullOrWhiteSpace(CurrentJoinCode))
            return;

        if (!string.Equals(_lastHostPromotionAttemptCode, CurrentJoinCode, StringComparison.Ordinal))
        {
            _lastHostPromotionAttemptCode = CurrentJoinCode;
            _hostPromotionAttemptedForCurrentCode = false;
        }

        if (!s_roomByJoinCode.TryGetValue(CurrentJoinCode, out LobbyRoomData room) || room == null)
            return;

        if (string.Equals(room.HostUserId, Managers.Discord.LocalUserId, StringComparison.Ordinal) && !IsHosting)
        {
            if (_hostPromotionAttemptedForCurrentCode)
                return;

            _hostPromotionAttemptedForCurrentCode = true;
            bool startedHost = TryStartUtpHost(room.Port, out _);
            if (startedHost)
            {
                IsHosting = true;
                HostUserId = room.HostUserId;
                Debug.Log($"[Lobby] Host promoted to local user. joinCode={CurrentJoinCode}, port={room.Port}");
            }
            else
            {
                Debug.LogWarning($"[Lobby] Host promotion deferred. joinCode={CurrentJoinCode}");
            }
        }
    }

    public void Clear()
    {
        Managers.Discord.OnLocalDisplayNameChanged -= HandleLocalDisplayNameChanged;
        Managers.Discord.OnLobbyUserVoiceChatStateChanged -= HandleLobbyUserVoiceChatStateChanged;
        _rangersByUserId.Clear();
        _nicknamesByUserId.Clear();
        IsHosting = false;
        HostUserId = string.Empty;
        CurrentJoinCode = string.Empty;
        _currentVoiceSecret = string.Empty;
        _currentPort = 0;
        _currentHostAddress = DefaultHostAddress;
        _lastHostPromotionAttemptCode = string.Empty;
        _hostPromotionAttemptedForCurrentCode = false;
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
        if (string.IsNullOrWhiteSpace(joinCode))
            return false;

        return s_roomByJoinCode.ContainsKey(joinCode);
    }

    public bool JoinLobbyByCode(string rawJoinCode)
    {
        string joinCode = NormalizeJoinCode(rawJoinCode);
        if (string.IsNullOrWhiteSpace(joinCode))
        {
            Debug.LogWarning("[Lobby] Join failed: invalid join code format.");
            return false;
        }

        if (!s_roomByJoinCode.TryGetValue(joinCode, out LobbyRoomData room) || room == null)
        {
            room = new LobbyRoomData
            {
                JoinCode = joinCode,
                HostUserId = string.Empty,
                VoiceSecret = BuildVoiceSecret(joinCode),
                HostAddress = ResolveConfiguredHostAddress(),
                Port = CalculatePort(joinCode),
            };

            s_roomByJoinCode[joinCode] = room;
            Debug.Log($"[Lobby] Join fallback room created for code={joinCode}, host={room.HostAddress}, port={room.Port}");
        }

        CleanupExistingLobbyObjects();

        CurrentJoinCode = joinCode;
        HostUserId = room.HostUserId;
        IsHosting = string.Equals(HostUserId, Managers.Discord.LocalUserId, StringComparison.Ordinal);
        _currentVoiceSecret = room.VoiceSecret;
        _currentHostAddress = room.HostAddress;
        _currentPort = room.Port;

        if (!room.JoinOrder.Contains(Managers.Discord.LocalUserId))
            room.JoinOrder.Add(Managers.Discord.LocalUserId);

        if (!IsHosting)
            TryStartUtpClient(room.HostAddress, room.Port);

        RangerController ranger = SpawnRangerForLocalUser();
        SetupLobbyCamera(ranger);
        SetupNicknameForLocalUser(ranger);

        Managers.Discord.NotifyLobbyUserJoined(Managers.Discord.LocalUserId, Managers.Discord.LocalDisplayName, true);
        Managers.Discord.EnsureLobbyVoiceConnected(room.VoiceSecret);
        return true;
    }

    public void QuitCurrentRoom()
    {
        string localUserId = Managers.Discord.LocalUserId;
        if (!string.IsNullOrWhiteSpace(CurrentJoinCode) && s_roomByJoinCode.TryGetValue(CurrentJoinCode, out LobbyRoomData room) && room != null)
        {
            room.JoinOrder.Remove(localUserId);

            if (string.Equals(room.HostUserId, localUserId, StringComparison.Ordinal))
            {
                if (room.JoinOrder.Count > 0)
                {
                    room.HostUserId = room.JoinOrder[0];
                    Debug.Log($"[Lobby] Host migrated to {room.HostUserId} for joinCode={room.JoinCode}");
                }
                else
                {
                    s_roomByJoinCode.Remove(room.JoinCode);
                    Debug.Log($"[Lobby] Room closed. joinCode={room.JoinCode}");
                }
            }
        }

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
        _lastHostPromotionAttemptCode = string.Empty;
        _hostPromotionAttemptedForCurrentCode = false;
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

        CurrentJoinCode = joinCode;
        _currentPort = roomPort;
        _currentHostAddress = string.IsNullOrWhiteSpace(hostAddress) ? ResolveConfiguredHostAddress() : hostAddress;
        _currentVoiceSecret = voiceSecret;
        GUIUtility.systemCopyBuffer = CurrentJoinCode;
        Managers.Toast.EnqueueMessage("Enter code is copied on clipboard.", 2.5f);

        LobbyRoomData room = new()
        {
            JoinCode = CurrentJoinCode,
            HostUserId = HostUserId,
            VoiceSecret = _currentVoiceSecret,
            HostAddress = _currentHostAddress,
            Port = _currentPort,
        };
        room.JoinOrder.Add(HostUserId);
        s_roomByJoinCode[CurrentJoinCode] = room;

        RangerController ranger = SpawnRangerForLocalUser();
        SetupLobbyCamera(ranger);
        SetupNicknameForLocalUser(ranger);

        Managers.Discord.NotifyLobbyUserJoined(Managers.Discord.LocalUserId, Managers.Discord.LocalDisplayName, true);

        Debug.Log($"Lobby host ready: user={Managers.Discord.LocalDisplayName}, joinCode={CurrentJoinCode}, port={_currentPort}, utpHostStarted={IsHosting}");
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
        for (int i = 0; i < 100; i++)
        {
            string code = GenerateJoinCode();
            if (!s_roomByJoinCode.ContainsKey(code))
                return code;
        }

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
        string configured = Util.GetEnv(LobbyHostAddressKey);
        return string.IsNullOrWhiteSpace(configured) ? DefaultHostAddress : configured.Trim();
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
        networkManagerType = Type.GetType("Unity.Netcode.NetworkManager, Unity.Netcode.Runtime");
        networkManagerObject = null;
        utpTransport = null;

        if (networkManagerType == null)
        {
            if (!s_loggedNetcodeMissing)
            {
                Debug.LogWarning("UTP operation skipped: Netcode for GameObjects package is not installed.");
                s_loggedNetcodeMissing = true;
            }
            return false;
        }

        MonoBehaviour[] behaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] != null && networkManagerType.IsAssignableFrom(behaviours[i].GetType()))
            {
                networkManagerObject = behaviours[i];
                break;
            }
        }

        if (networkManagerObject == null)
        {
            if (!s_loggedNetworkManagerMissing)
            {
                Debug.LogWarning("UTP operation skipped: no NetworkManager found in Lobby scene.");
                s_loggedNetworkManagerMissing = true;
            }
            return false;
        }

        utpTransport = networkManagerObject.GetComponent("Unity.Netcode.Transports.UTP.UnityTransport");
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
