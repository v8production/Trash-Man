using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class LobbyScene : BaseScene
{
    private UI_LobbyMenu _lobbyMenu;
    private UI_RoleSelectMenu _roleSelectMenu;
    private UI_Loading _loadingUi;
    private bool _pendingHostBootstrap;
    private bool _isLobbySetupPending;
    private string _pendingJoinCode = string.Empty;
    private UI_HostStartButton _screenHostStartButton;
    private UI_RoleSelectButton _screenRoleSelectButton;
    private LobbyCameraController _localLobbyCamera;
    private const string LobbyCameraPrefabName = "Lobby_Camera";
    private const string ScreenHostStartButtonName = "UI_HostStartButton";
    private const string ScreenRoleSelectButtonName = "UI_RoleSelectButton";
    private static readonly Vector3 s_hostStartButtonWorldPosition = new(1.49f, 1.8f, 1.5f);
    private static readonly Vector3 s_roleSelectButtonWorldPosition = new(1.49f, 1.8f, -1.5f);
    private static readonly Quaternion s_screenButtonWorldRotation = Quaternion.Euler(0f, -90f, 0f);

    private static readonly Dictionary<string, LobbyUserEntry> s_userEntriesByDiscordUserId = new();

    private const float LobbyJoinTimeoutSeconds = 15f;
    private float _lobbySetupStartedAt;

    private sealed class LobbyUserEntry
    {
        public RangerController Ranger;
        public UI_Nickname Nickname;
        public int SelectedRoleMask;
    }

    public static void RegisterUserObjects(string userId, RangerController ranger, UI_Nickname nickname)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        if (!s_userEntriesByDiscordUserId.TryGetValue(userId, out LobbyUserEntry entry) || entry == null)
        {
            entry = new LobbyUserEntry();
            s_userEntriesByDiscordUserId[userId] = entry;
        }

        if (ranger != null)
            entry.Ranger = ranger;

        if (nickname != null)
            entry.Nickname = nickname;
    }

    public static void RegisterUserPartSelection(string userId, int selectedRoleMask)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        if (!s_userEntriesByDiscordUserId.TryGetValue(userId, out LobbyUserEntry entry) || entry == null)
        {
            entry = new LobbyUserEntry();
            s_userEntriesByDiscordUserId[userId] = entry;
        }

        entry.SelectedRoleMask = NormalizeRoleMask(selectedRoleMask);
    }

    public static bool TryGetRegisteredUserSelectedRoleMask(string userId, out int roleMask)
    {
        roleMask = 0;

        if (string.IsNullOrWhiteSpace(userId))
            return false;

        if (!s_userEntriesByDiscordUserId.TryGetValue(userId, out LobbyUserEntry entry) || entry == null)
            return false;

        roleMask = NormalizeRoleMask(entry.SelectedRoleMask);
        return roleMask != 0;
    }

    public static void UnregisterUserObjects(string userId, RangerController ranger, UI_Nickname nickname)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        if (!s_userEntriesByDiscordUserId.TryGetValue(userId, out LobbyUserEntry entry) || entry == null)
            return;

        if (ranger != null && entry.Ranger == ranger)
            entry.Ranger = null;

        if (nickname != null && entry.Nickname == nickname)
            entry.Nickname = null;

        if (entry.Ranger == null && entry.Nickname == null)
            s_userEntriesByDiscordUserId.Remove(userId);
    }

    public static bool TrySetNicknameSpeakerActive(string userId, bool isVoiceChatActive)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        if (!s_userEntriesByDiscordUserId.TryGetValue(userId, out LobbyUserEntry entry) || entry == null || entry.Nickname == null)
            return false;

        entry.Nickname.SetActive(isVoiceChatActive);
        return true;
    }

    public static void ClearUserObjectRegistry()
    {
        s_userEntriesByDiscordUserId.Clear();
    }

    protected override void Init()
    {
        base.Init();
        SceneType = Define.Scene.Lobby;
        LoadManagers();
        LogLobbyVoice("LobbyScene initialized.");
        Managers.Input.SetMode(Define.InputMode.Player);
        EnsureLobbyMenu();
        EnsureLoadingUI();
        EnsureScreenHostStartButton();
        EnsureScreenRoleSelectButton();

        _pendingHostBootstrap = Managers.Scene.ConsumeLobbyHostRequest();
        _pendingJoinCode = Managers.Scene.ConsumeLobbyJoinCodeRequest(out string joinCode) ? joinCode : string.Empty;
        _isLobbySetupPending = _pendingHostBootstrap || !string.IsNullOrWhiteSpace(_pendingJoinCode);

        if (_isLobbySetupPending)
        {
            _lobbySetupStartedAt = Time.unscaledTime;
            SetLobbyLoading(true, "Preparing lobby...");
        }

        Managers.Discord.OnAuthStateChanged -= HandleDiscordAuthStateChanged;
        Managers.Discord.OnAuthStateChanged += HandleDiscordAuthStateChanged;
        ProcessPendingLobbyRequest();
        TryAutoConnectLobbyVoice();
    }

    private void Update()
    {
        EnsureLocalLobbyCameraReady();
        UpdateLobbyLoadingState();
        UpdateScreenButtonTransforms();
        if (!IsEscapePressedThisFrame())
            return;

        if (_isLobbySetupPending)
            return;

        if (CloseRoleSelectMenu())
            return;

        ToggleLobbyMenu();
    }

    private void OnDestroy()
    {
        Managers.Discord.OnAuthStateChanged -= HandleDiscordAuthStateChanged;

        if (_screenHostStartButton != null)
            _screenHostStartButton.StartButtonClicked -= HandleHostStartButtonClicked;

        if (_screenRoleSelectButton != null)
            _screenRoleSelectButton.RoleSelectButtonClicked -= HandleRoleSelectButtonClicked;

        if (_roleSelectMenu != null)
        {
            _roleSelectMenu.RoleSelected -= HandleRoleSelected;
            _roleSelectMenu.Closed -= HandleRoleSelectMenuClosed;
        }
    }

    private static void LoadManagers()
    {
        _ = Managers.Input;
        Managers.LobbySession.Init();
    }

    private void HandleDiscordAuthStateChanged()
    {
        LogLobbyVoice($"Discord auth state changed. linked={Managers.Discord.IsLinked}, connecting={Managers.Discord.IsConnecting}, lastError={Managers.Discord.LastAuthError}");
        ProcessPendingLobbyRequest();
        TryAutoConnectLobbyVoice();
    }

    private void ProcessPendingLobbyRequest()
    {
        if (!Managers.Discord.IsLinked)
        {
            if (_pendingHostBootstrap || !string.IsNullOrWhiteSpace(_pendingJoinCode))
            {
                LogLobbyVoice("Pending lobby request is waiting for Discord link readiness.");
                SetLobbyLoading(true, "Linking Discord...");
            }

            return;
        }

        if (_pendingHostBootstrap)
        {
            SetLobbyLoading(true, "Creating lobby...");
            _pendingHostBootstrap = false;
            Managers.LobbySession.BootstrapLocalHostLobby();
            return;
        }

        if (string.IsNullOrWhiteSpace(_pendingJoinCode))
        {
            if (_isLobbySetupPending)
            {
                if (!Managers.LobbySession.HasJoinedLobbySession)
                {
                    SetLobbyLoading(true, "Preparing lobby...");
                    return;
                }

                if (!IsLocalLobbyInteractionReady())
                {
                    SetLobbyLoading(true, "Spawning local player...");
                    return;
                }
            }

            SetLobbyLoading(false);
            return;
        }

        string joinCode = _pendingJoinCode;
        SetLobbyLoading(true, "Joining lobby...");
        _pendingJoinCode = string.Empty;
        if (!Managers.LobbySession.JoinLobbyByCode(joinCode))
        {
            Managers.Chat.EnqueueMessage("Failed to join lobby with that code.", 2.5f);
            Managers.Scene.LoadScene(Define.Scene.Intro);
        }
    }

    private void UpdateLobbyLoadingState()
    {
        if (!_isLobbySetupPending)
            return;

        if (Managers.LobbySession.HasLobbyNetworkConnectionFailed)
        {
            SetLobbyLoading(false);
            _isLobbySetupPending = false;
            Managers.Toast.EnqueueMessage("Failed to connect to lobby host.", 3f);
            Managers.Scene.LoadScene(Define.Scene.Intro);
            return;
        }

        bool hasLocalRanger = Managers.LobbySession.TryGetLocalRangerTransform(out _);
        bool hasCamera = _localLobbyCamera != null;

        if (Managers.LobbySession.HasJoinedLobbySession &&
            Managers.LobbySession.IsLobbyNetworkConnected &&
            hasLocalRanger &&
            hasCamera)
        {
            SetLobbyLoading(false);
            _isLobbySetupPending = false;
            return;
        }

        if (Time.unscaledTime - _lobbySetupStartedAt > LobbyJoinTimeoutSeconds)
        {
            SetLobbyLoading(false);
            _isLobbySetupPending = false;
            Managers.Toast.EnqueueMessage("Lobby connection timed out.", 3f);
            Managers.Scene.LoadScene(Define.Scene.Intro);
        }
    }

    private void SetLobbyLoading(bool active, string message = null)
    {
        EnsureLoadingUI();
        if (_loadingUi == null) return;

        _isLobbySetupPending = active;
        _loadingUi.gameObject.SetActive(active);

        if (!string.IsNullOrWhiteSpace(message))
            _loadingUi.SetMessage(message);

        if (active)
            Managers.Input.SetMode(Define.InputMode.UI);
        else
            RefreshInputMode();
    }

    private void EnsureLobbyMenu()
    {
        if (_lobbyMenu != null)
            return;

        _lobbyMenu = Managers.UI.ShowSceneUI<UI_LobbyMenu>(nameof(UI_LobbyMenu));
        if (_lobbyMenu != null)
            _lobbyMenu.gameObject.SetActive(false);
    }

    private void EnsureLoadingUI()
    {
        if (_loadingUi != null)
            return;

        _loadingUi = Managers.UI.ShowSceneUI<UI_Loading>(nameof(UI_Loading));
        if (_loadingUi != null)
            _loadingUi.gameObject.SetActive(false);
    }

    private void EnsureScreenHostStartButton()
    {
        if (_screenHostStartButton != null)
            return;

        _screenHostStartButton = Managers.UI.CreateWorldSpaceUI<UI_HostStartButton>(null, ScreenHostStartButtonName);
        if (_screenHostStartButton == null)
        {
            Debug.LogWarning("[Lobby] Failed to load UI_HostStartButton prefab from Resources/Prefabs/UIs/WorldSpace.");
            return;
        }

        ApplyFixedScreenButtonTransform(_screenHostStartButton.transform, s_hostStartButtonWorldPosition);
        _screenHostStartButton.transform.SetParent(null, true);

        _screenHostStartButton.StartButtonClicked -= HandleHostStartButtonClicked;
        _screenHostStartButton.StartButtonClicked += HandleHostStartButtonClicked;
    }

    private void EnsureScreenRoleSelectButton()
    {
        if (_screenRoleSelectButton != null)
            return;

        _screenRoleSelectButton = Managers.UI.CreateWorldSpaceUI<UI_RoleSelectButton>(null, ScreenRoleSelectButtonName);
        if (_screenRoleSelectButton == null)
        {
            Debug.LogWarning("[Lobby] Failed to load UI_RoleSelectButton prefab from Resources/Prefabs/UIs/WorldSpace.");
            return;
        }

        ApplyFixedScreenButtonTransform(_screenRoleSelectButton.transform, s_roleSelectButtonWorldPosition);
        _screenRoleSelectButton.transform.SetParent(null, true);

        _screenRoleSelectButton.RoleSelectButtonClicked -= HandleRoleSelectButtonClicked;
        _screenRoleSelectButton.RoleSelectButtonClicked += HandleRoleSelectButtonClicked;
    }

    private void UpdateScreenButtonTransforms()
    {
        if (_screenHostStartButton != null)
            ApplyFixedScreenButtonTransform(_screenHostStartButton.transform, s_hostStartButtonWorldPosition);

        if (_screenRoleSelectButton != null)
            ApplyFixedScreenButtonTransform(_screenRoleSelectButton.transform, s_roleSelectButtonWorldPosition);
    }

    private static void ApplyFixedScreenButtonTransform(Transform targetTransform, Vector3 worldPosition)
    {
        if (targetTransform == null)
            return;

        targetTransform.SetPositionAndRotation(worldPosition, s_screenButtonWorldRotation);
    }

    private void HandleRoleSelectButtonClicked()
    {
        if (_isLobbySetupPending || !Managers.LobbySession.HasJoinedLobbySession)
            return;

        ShowRoleSelectMenu();
    }

    private void HandleRoleSelected(Define.TitanRole role)
    {
        if (_isLobbySetupPending || !Managers.LobbySession.HasJoinedLobbySession)
            return;

        if (TryToggleLocalRole(role) && _roleSelectMenu != null)
            _roleSelectMenu.RefreshRoleNicknames();
    }

    private void HandleRoleSelectMenuClosed()
    {
        CloseRoleSelectMenu();
    }

    private bool TryToggleLocalRole(Define.TitanRole selectedRole)
    {
        LobbyNetworkPlayer localPlayer = FindLocalOwnedNetworkPlayer();
        if (localPlayer == null)
            return false;

        int currentMask = localPlayer.SelectedTitanRoleMaskValue;
        int bit = 1 << (((int)selectedRole) - (int)Define.TitanRole.Torso);
        int nextMask = currentMask ^ bit;

        localPlayer.ToggleTitanRoleSelection(selectedRole);

        if (localPlayer.TryGetLobbyUserId(out string lobbyUserId))
            RegisterUserPartSelection(lobbyUserId, nextMask);

        bool isSelected = (nextMask & bit) != 0;
        Managers.Toast.EnqueueMessage($"{(isSelected ? "Selected" : "Unselected")} part: {GetRoleLabel(selectedRole)}", 1.4f);
        return true;
    }

    private void EnsureRoleSelectMenu()
    {
        if (_roleSelectMenu != null)
            return;

        _roleSelectMenu = Managers.UI.ShowSceneUI<UI_RoleSelectMenu>(nameof(UI_RoleSelectMenu));
        if (_roleSelectMenu == null)
            return;

        _roleSelectMenu.RoleSelected -= HandleRoleSelected;
        _roleSelectMenu.RoleSelected += HandleRoleSelected;
        _roleSelectMenu.Closed -= HandleRoleSelectMenuClosed;
        _roleSelectMenu.Closed += HandleRoleSelectMenuClosed;
        _roleSelectMenu.gameObject.SetActive(false);
    }

    private void ShowRoleSelectMenu()
    {
        EnsureRoleSelectMenu();
        if (_roleSelectMenu == null)
            return;

        _roleSelectMenu.gameObject.SetActive(true);
        _roleSelectMenu.RefreshRoleNicknames();
        Managers.Input.SetMode(Define.InputMode.UI);
    }

    private bool CloseRoleSelectMenu()
    {
        if (_roleSelectMenu == null || !_roleSelectMenu.gameObject.activeSelf)
            return false;

        _roleSelectMenu.gameObject.SetActive(false);
        RefreshInputMode();
        return true;
    }

    private void EnsureLocalLobbyCameraReady()
    {
        if (!Managers.LobbySession.HasJoinedLobbySession)
            return;

        if (_localLobbyCamera == null)
            _localLobbyCamera = FindAnyObjectByType<LobbyCameraController>();

        if (!Managers.LobbySession.TryGetLocalRangerTransform(out Transform localRanger) || localRanger == null)
            return;

        if (_localLobbyCamera == null)
        {
            GameObject cameraObject = Managers.Resource.Instantiate(LobbyCameraPrefabName);
            if (cameraObject == null)
                return;

            _localLobbyCamera = cameraObject.GetComponent<LobbyCameraController>();
            if (_localLobbyCamera == null)
                return;
        }

        _localLobbyCamera.SetTarget(localRanger);
    }

    private bool IsLocalLobbyInteractionReady()
    {
        if (_localLobbyCamera == null)
            return false;

        if (!Managers.LobbySession.TryGetLocalRangerTransform(out Transform localRanger) || localRanger == null)
            return false;

        return true;
    }

    private static LobbyNetworkPlayer FindLocalOwnedNetworkPlayer()
    {
        return LobbyNetworkPlayer.FindLocalOwnedPlayer();
    }

    private void HandleHostStartButtonClicked()
    {
        if (!Managers.LobbySession.IsHosting)
        {
            Managers.Toast.EnqueueMessage("Only the host can start this action.", 2.5f);
            return;
        }

        // Validate using network-synced role masks, not the lobby UI registry.
        // The UI registry can be stale while identities/objects are still syncing.
        if (!Managers.TitanRole.RefreshRoleMap(requireAllRoles: true, out string roleError))
        {
            string label = string.IsNullOrWhiteSpace(roleError) ? "role requirements" : roleError;
            Managers.Toast.EnqueueMessage($"Cannot start game: {label}", 2.8f);
            return;
        }

        if (!LobbyNetworkPlayer.RequestLoadGameForAll())
            Managers.Scene.LoadScene(Define.Scene.Game);
    }

    private static bool AreAllLobbyUsersReadyForGame(out string missingUserId)
    {
        missingUserId = string.Empty;
        if (s_userEntriesByDiscordUserId.Count == 0)
        {
            missingUserId = "No lobby users";
            return false;
        }

        Dictionary<int, string> ownerByRoleValue = new();
        int combinedRoleMask = 0;

        foreach (KeyValuePair<string, LobbyUserEntry> pair in s_userEntriesByDiscordUserId)
        {
            string userId = pair.Key;
            LobbyUserEntry entry = pair.Value;
            if (entry == null || entry.Ranger == null)
            {
                missingUserId = userId;
                return false;
            }

            int roleMask = NormalizeRoleMask(entry.SelectedRoleMask);
            combinedRoleMask |= roleMask;

            for (int roleValue = (int)Define.TitanRole.Torso; roleValue <= (int)Define.TitanRole.RightLeg; roleValue++)
            {
                int bit = 1 << (roleValue - 1);
                if ((roleMask & bit) == 0)
                    continue;

                if (ownerByRoleValue.TryGetValue(roleValue, out string existingOwner) && !string.Equals(existingOwner, userId))
                {
                    missingUserId = $"Duplicate role: {GetRoleLabel((Define.TitanRole)roleValue)}";
                    return false;
                }

                ownerByRoleValue[roleValue] = userId;
            }
        }

        for (int roleValue = (int)Define.TitanRole.Torso; roleValue <= (int)Define.TitanRole.RightLeg; roleValue++)
        {
            int bit = 1 << (roleValue - 1);
            if ((combinedRoleMask & bit) == 0)
            {
                missingUserId = $"Missing role: {GetRoleLabel((Define.TitanRole)roleValue)}";
                return false;
            }
        }

        return true;
    }

    private static int NormalizeRoleMask(int roleMask)
    {
        return roleMask & GetAllRoleMask();
    }

    private static int GetAllRoleMask()
    {
        int count = ((int)Define.TitanRole.RightLeg - (int)Define.TitanRole.Torso) + 1;
        return (1 << count) - 1;
    }

    private static string GetRoleLabel(Define.TitanRole role)
    {
        return role switch
        {
            Define.TitanRole.Torso => "Center",
            Define.TitanRole.LeftArm => "Left Arm",
            Define.TitanRole.RightArm => "Right Arm",
            Define.TitanRole.LeftLeg => "Left Leg",
            Define.TitanRole.RightLeg => "Right Leg",
            _ => "Unknown",
        };
    }

    private void ToggleLobbyMenu()
    {
        EnsureLobbyMenu();
        if (_lobbyMenu == null)
            return;

        bool shouldShow = !_lobbyMenu.gameObject.activeSelf;
        _lobbyMenu.gameObject.SetActive(shouldShow);
        RefreshInputMode();
    }

    private void RefreshInputMode()
    {
        bool hasBlockingUi = _isLobbySetupPending
            || (_lobbyMenu != null && _lobbyMenu.gameObject.activeSelf)
            || (_roleSelectMenu != null && _roleSelectMenu.gameObject.activeSelf);

        Managers.Input.SetMode(hasBlockingUi ? Define.InputMode.UI : Define.InputMode.Player);
    }

    private static bool IsEscapePressedThisFrame()
    {
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
    }

    private void TryAutoConnectLobbyVoice()
    {
        LogLobbyVoice($"TryAutoConnectLobbyVoice called. linked={Managers.Discord.IsLinked}, connecting={Managers.Discord.IsConnecting}");

        if (!Managers.Discord.IsLinked)
        {
            if (Managers.Discord.IsConnecting)
            {
                LogLobbyVoice("Auto-connect skipped because Discord is currently connecting.");
                return;
            }

            bool hasAppId = Util.TryGetDiscordApplicationId(out ulong appId);
            LogLobbyVoice($"Resolved Discord application id. hasValue={hasAppId}");
            if (!hasAppId)
            {
                Debug.LogWarning("LobbyScene: Discord auto-connect skipped - Discord application id is not configured.");
                return;
            }

            LogLobbyVoice($"Requesting Discord connect with appId={appId}.");
            Managers.Discord.Connect(appId, string.Empty);
            return;
        }

        string lobbyVoiceSecret = GetLobbyVoiceSecret();
        LogLobbyVoice($"Discord already linked. Ensuring lobby voice connection. secretLen={lobbyVoiceSecret.Length}");
        Managers.Discord.EnsureLobbyVoiceConnected(lobbyVoiceSecret);
    }

    private static string GetLobbyVoiceSecret()
    {
        string activeVoiceSecret = Managers.LobbySession.CurrentVoiceSecret;
        if (!string.IsNullOrWhiteSpace(activeVoiceSecret))
            return activeVoiceSecret;

        string joinCode = Managers.LobbySession.CurrentJoinCode;
        if (!string.IsNullOrWhiteSpace(joinCode))
            return $"trash-man-lobby-{joinCode.Trim().ToLowerInvariant()}";

        string localUserId = Managers.Discord.LocalUserId;
        if (!string.IsNullOrWhiteSpace(localUserId))
            return $"trash-man-lobby-{localUserId.Trim().ToLowerInvariant()}";

        return "trash-man-lobby";
    }

    private static void LogLobbyVoice(string message)
    {
        Debug.Log($"[LobbyVoice] {message}");
    }

    public override void Clear()
    {
        ClearUserObjectRegistry();
        Managers.Discord.OnAuthStateChanged -= HandleDiscordAuthStateChanged;

        // Keep voice alive when transitioning into GameScene.
        if (Managers.Scene.PendingScene == Define.Scene.Intro)
            Managers.Discord.EndActiveLobbyVoice();

        if (_lobbyMenu != null)
        {
            Managers.Resource.Destory(_lobbyMenu.gameObject);
            _lobbyMenu = null;
        }

        if (_loadingUi != null)
        {
            Managers.Resource.Destory(_loadingUi.gameObject);
            _loadingUi = null;
        }

        if (_roleSelectMenu != null)
        {
            _roleSelectMenu.RoleSelected -= HandleRoleSelected;
            _roleSelectMenu.Closed -= HandleRoleSelectMenuClosed;
            Managers.Resource.Destory(_roleSelectMenu.gameObject);
            _roleSelectMenu = null;
        }

        if (_screenHostStartButton != null)
        {
            _screenHostStartButton.StartButtonClicked -= HandleHostStartButtonClicked;
            Managers.Resource.Destory(_screenHostStartButton.gameObject);
        }

        if (_screenRoleSelectButton != null)
        {
            _screenRoleSelectButton.RoleSelectButtonClicked -= HandleRoleSelectButtonClicked;
            Managers.Resource.Destory(_screenRoleSelectButton.gameObject);
            _screenRoleSelectButton = null;
        }

        _localLobbyCamera = null;
        _screenHostStartButton = null;
    }
}
