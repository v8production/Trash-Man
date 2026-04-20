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
    private Transform _screenTransform;
    private LobbyCameraController _localLobbyCamera;
    private const string LobbyCameraPrefabName = "Lobby_Camera";
    private const string ScreenObjectName = "Screen";
    private const string ScreenHostStartButtonName = "UI_HostStartButton";
    private const string ScreenRoleSelectButtonName = "UI_RoleSelectButton";
    private static readonly ScreenButtonPlacement s_hostStartButtonPlacement = new(new Vector3(-0.5f, 1.5f, -1f), new Vector3(0f, -90f, 0f));
    private static readonly ScreenButtonPlacement s_roleSelectButtonPlacement = new(new Vector3(-0.5f, 1.5f, 1f), new Vector3(0f, -90f, 0f));

    private static readonly Dictionary<string, LobbyUserEntry> s_userEntriesByDiscordUserId = new();

    private sealed class LobbyUserEntry
    {
        public RangerController Ranger;
        public UI_Nickname Nickname;
        public int SelectedRole;
    }

    private readonly struct ScreenButtonPlacement
    {
        public ScreenButtonPlacement(Vector3 worldPosition, Vector3 worldEulerAngles)
        {
            WorldPosition = worldPosition;
            WorldEulerAngles = worldEulerAngles;
        }

        public Vector3 WorldPosition { get; }
        public Vector3 WorldEulerAngles { get; }
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

    public static void RegisterUserPartSelection(string userId, int selectedRoleValue)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        if (!s_userEntriesByDiscordUserId.TryGetValue(userId, out LobbyUserEntry entry) || entry == null)
        {
            entry = new LobbyUserEntry();
            s_userEntriesByDiscordUserId[userId] = entry;
        }

        entry.SelectedRole = NormalizeRoleValue(selectedRoleValue);
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
            SetLobbyLoading(true, "Preparing lobby...");

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

        if (!Managers.LobbySession.HasJoinedLobbySession)
            return;

        if (!IsLocalLobbyInteractionReady())
            return;

        SetLobbyLoading(false);
        Managers.Input.SetMode(Define.InputMode.Player);
    }

    private void SetLobbyLoading(bool active, string message = null)
    {
        EnsureLoadingUI();
        if (_loadingUi == null)
            return;

        _isLobbySetupPending = active;
        _loadingUi.gameObject.SetActive(active);

        if (!string.IsNullOrWhiteSpace(message))
            _loadingUi.SetMessage(message);

        if (active)
            Managers.Input.SetMode(Define.InputMode.UI);
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

        if (!TryGetScreenTransform(out Transform screenTransform))
        {
            Debug.LogWarning("[Lobby] Screen object was not found. Host start button setup skipped.");
            return;
        }

        Transform legacyRoot = screenTransform.Find(ScreenHostStartButtonName);
        _screenHostStartButton = Managers.UI.CreateWorldSpaceUI<UI_HostStartButton>(null, ScreenHostStartButtonName);
        if (_screenHostStartButton == null)
        {
            Debug.LogWarning("[Lobby] Failed to load UI_HostStartButton prefab from Resources/Prefabs/UIs/WorldSpace.");
            return;
        }

        SetLegacyButtonHidden(legacyRoot);
        ApplyScreenButtonTransform(_screenHostStartButton.transform, s_hostStartButtonPlacement, screenTransform);
        _screenHostStartButton.transform.SetParent(null, true);

        _screenHostStartButton.StartButtonClicked -= HandleHostStartButtonClicked;
        _screenHostStartButton.StartButtonClicked += HandleHostStartButtonClicked;
    }

    private void EnsureScreenRoleSelectButton()
    {
        if (_screenRoleSelectButton != null)
            return;

        if (!TryGetScreenTransform(out Transform screenTransform))
        {
            Debug.LogWarning("[Lobby] Screen object was not found. Role select button setup skipped.");
            return;
        }

        Transform legacyRoot = screenTransform.Find(ScreenRoleSelectButtonName);
        _screenRoleSelectButton = Managers.UI.CreateWorldSpaceUI<UI_RoleSelectButton>(null, ScreenRoleSelectButtonName);
        if (_screenRoleSelectButton == null)
        {
            Debug.LogWarning("[Lobby] Failed to load UI_RoleSelectButton prefab from Resources/Prefabs/UIs/WorldSpace.");
            return;
        }

        SetLegacyButtonHidden(legacyRoot);
        ApplyScreenButtonTransform(_screenRoleSelectButton.transform, s_roleSelectButtonPlacement, screenTransform);
        _screenRoleSelectButton.transform.SetParent(null, true);

        _screenRoleSelectButton.RoleSelectButtonClicked -= HandleRoleSelectButtonClicked;
        _screenRoleSelectButton.RoleSelectButtonClicked += HandleRoleSelectButtonClicked;
    }

    private void UpdateScreenButtonTransforms()
    {
        if (!TryGetScreenTransform(out Transform screenTransform))
            return;

        ApplyScreenButtonTransform(_screenHostStartButton != null ? _screenHostStartButton.transform : null, s_hostStartButtonPlacement, screenTransform);
        ApplyScreenButtonTransform(_screenRoleSelectButton != null ? _screenRoleSelectButton.transform : null, s_roleSelectButtonPlacement, screenTransform);
    }

    private bool TryGetScreenTransform(out Transform screenTransform)
    {
        if (_screenTransform != null)
        {
            screenTransform = _screenTransform;
            return true;
        }

        GameObject screen = GameObject.Find(ScreenObjectName);
        if (screen == null)
        {
            screenTransform = null;
            return false;
        }

        UI_HostStartButton legacyHostButtonController = screen.GetComponent<UI_HostStartButton>();
        if (legacyHostButtonController != null)
            legacyHostButtonController.enabled = false;

        _screenTransform = screen.transform;
        screenTransform = _screenTransform;
        return true;
    }

    private static void ApplyLegacyTransform(Transform targetTransform, Transform legacyTransform)
    {
        if (targetTransform == null || legacyTransform == null)
            return;

        targetTransform.SetPositionAndRotation(legacyTransform.position, legacyTransform.rotation);
        targetTransform.localScale = legacyTransform.lossyScale;

        if (targetTransform is RectTransform targetRect && legacyTransform is RectTransform legacyRect)
        {
            targetRect.sizeDelta = legacyRect.sizeDelta;
            targetRect.pivot = legacyRect.pivot;
        }
    }

    private static void ApplyScreenButtonTransform(Transform targetTransform, ScreenButtonPlacement placement, Transform screenTransform)
    {
        if (targetTransform == null || screenTransform == null)
            return;

        Vector3 worldPosition = placement.WorldPosition;
        Quaternion worldRotation = Quaternion.Euler(placement.WorldEulerAngles);
        targetTransform.SetPositionAndRotation(worldPosition, worldRotation);
    }


    private static void SetLegacyButtonHidden(Transform legacyTransform)
    {
        if (legacyTransform == null)
            return;

        if (legacyTransform.gameObject.activeSelf)
            legacyTransform.gameObject.SetActive(false);
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

        if (TrySelectLocalRole(role) && _roleSelectMenu != null)
            _roleSelectMenu.RefreshRoleNicknames();
    }

    private void HandleRoleSelectMenuClosed()
    {
        CloseRoleSelectMenu();
    }

    private bool TrySelectLocalRole(Define.TitanRole selectedRole)
    {
        LobbyNetworkPlayer localPlayer = FindLocalOwnedNetworkPlayer();
        if (localPlayer == null)
            return false;

        localPlayer.SelectTitanRole(selectedRole);

        if (localPlayer.TryGetLobbyUserId(out string lobbyUserId))
            RegisterUserPartSelection(lobbyUserId, (int)selectedRole);

        Managers.Toast.EnqueueMessage($"Selected part: {GetRoleLabel(selectedRole)}", 1.4f);
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
        LobbyNetworkPlayer[] players = FindObjectsByType<LobbyNetworkPlayer>();
        for (int i = 0; i < players.Length; i++)
        {
            LobbyNetworkPlayer player = players[i];
            if (player != null && player.IsOwner)
                return player;
        }

        return null;
    }

    private void HandleHostStartButtonClicked()
    {
        if (Managers.LobbySession == null || !Managers.LobbySession.IsHosting)
        {
            Managers.Toast.EnqueueMessage("Only the host can start this action.", 2.5f);
            return;
        }

        if (!AreAllLobbyUsersReadyForGame(out string missingUserId))
        {
            string missingLabel = string.IsNullOrWhiteSpace(missingUserId) ? "unknown requirement" : missingUserId;
            Managers.Toast.EnqueueMessage($"Cannot start game: {missingLabel}", 2.8f);
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

        HashSet<int> claimedRoles = new();

        foreach (KeyValuePair<string, LobbyUserEntry> pair in s_userEntriesByDiscordUserId)
        {
            string userId = pair.Key;
            LobbyUserEntry entry = pair.Value;
            if (entry == null || entry.Ranger == null)
            {
                missingUserId = userId;
                return false;
            }

            if (!IsValidRoleValue(entry.SelectedRole))
            {
                missingUserId = userId;
                return false;
            }

            if (!claimedRoles.Add(entry.SelectedRole))
            {
                missingUserId = $"Duplicate role: {GetRoleLabel((Define.TitanRole)entry.SelectedRole)}";
                return false;
            }
        }

        for (int roleValue = (int)Define.TitanRole.Body; roleValue <= (int)Define.TitanRole.RightLeg; roleValue++)
        {
            if (!claimedRoles.Contains(roleValue))
            {
                missingUserId = $"Missing role: {GetRoleLabel((Define.TitanRole)roleValue)}";
                return false;
            }
        }

        return true;
    }

    private static int NormalizeRoleValue(int roleValue)
    {
        return IsValidRoleValue(roleValue) ? roleValue : 0;
    }

    private static bool IsValidRoleValue(int roleValue)
    {
        return roleValue >= (int)Define.TitanRole.Body && roleValue <= (int)Define.TitanRole.RightLeg;
    }

    private static string GetRoleLabel(Define.TitanRole role)
    {
        return role switch
        {
            Define.TitanRole.Body => "Center",
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
        _screenTransform = null;
    }
}
