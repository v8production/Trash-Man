using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class LobbyScene : BaseScene
{
    private UI_LobbyMenu _lobbyMenu;
    private UI_Loading _loadingUi;
    private bool _pendingHostBootstrap;
    private bool _isLobbySetupPending;
    private string _pendingJoinCode = string.Empty;
    private UI_HostStartButton _screenHostStartButton;
    private LobbyCameraController _localLobbyCamera;
    private const string LobbyCameraPrefabName = "Lobby_Camera";

    private readonly List<RoleButtonBinding> _roleButtonBindings = new();
    private static readonly RoleButtonSetup[] s_roleButtonSetups =
    {
        new("UI_BodyButton", Define.TitanRole.Body),
        new("UI_LeftArmButton", Define.TitanRole.LeftArm),
        new("UI_RightArmButton", Define.TitanRole.RightArm),
        new("UI_LeftLegButton", Define.TitanRole.LeftLeg),
        new("UI_RightLegButton", Define.TitanRole.RightLeg),
    };

    private static readonly Dictionary<string, LobbyUserEntry> s_userEntriesByDiscordUserId = new();

    private sealed class LobbyUserEntry
    {
        public RangerController Ranger;
        public UI_Nickname Nickname;
        public int SelectedRole;
    }

    private readonly struct RoleButtonSetup
    {
        public RoleButtonSetup(string objectName, Define.TitanRole role)
        {
            ObjectName = objectName;
            Role = role;
        }

        public string ObjectName { get; }
        public Define.TitanRole Role { get; }
    }

    private sealed class RoleButtonBinding
    {
        public Button Button;
        public UnityEngine.Events.UnityAction Listener;
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
        EnsureScreenRoleButtons();

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
        HandleLocalPartSelectionInput();

        if (!IsEscapePressedThisFrame())
            return;

        if (_isLobbySetupPending)
            return;

        ToggleLobbyMenu();
    }

    private void OnDestroy()
    {
        Managers.Discord.OnAuthStateChanged -= HandleDiscordAuthStateChanged;

        if (_screenHostStartButton != null)
            _screenHostStartButton.StartButtonClicked -= HandleHostStartButtonClicked;

        UnbindScreenRoleButtons();
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

        GameObject screen = GameObject.Find("Screen");
        if (screen == null)
        {
            Debug.LogWarning("[Lobby] Screen object was not found. Host start button setup skipped.");
            return;
        }

        _screenHostStartButton = screen.GetComponent<UI_HostStartButton>();
        if (_screenHostStartButton == null)
        {
            Debug.LogWarning("[Lobby] UI_HostStartButton is missing on Screen. Attach it in scene and keep button root hidden by default.");
            return;
        }

        _screenHostStartButton.StartButtonClicked -= HandleHostStartButtonClicked;
        _screenHostStartButton.StartButtonClicked += HandleHostStartButtonClicked;
    }

    private void EnsureScreenRoleButtons()
    {
        if (_roleButtonBindings.Count > 0)
            return;

        GameObject screen = GameObject.Find("Screen");
        if (screen == null)
        {
            Debug.LogWarning("[Lobby] Screen object was not found. Role button setup skipped.");
            return;
        }

        for (int i = 0; i < s_roleButtonSetups.Length; i++)
        {
            RoleButtonSetup setup = s_roleButtonSetups[i];
            Transform root = screen.transform.Find(setup.ObjectName);
            if (root == null)
            {
                Debug.LogWarning($"[Lobby] {setup.ObjectName} object was not found under Screen.");
                continue;
            }

            Button roleButton = root.GetComponentInChildren<Button>(true);
            if (roleButton == null)
            {
                Debug.LogWarning($"[Lobby] {setup.ObjectName} has no Button component in children.");
                continue;
            }

            Define.TitanRole role = setup.Role;
            UnityEngine.Events.UnityAction listener = () => HandleRoleButtonClicked(role);
            roleButton.onClick.AddListener(listener);

            _roleButtonBindings.Add(new RoleButtonBinding
            {
                Button = roleButton,
                Listener = listener,
            });
        }
    }

    private void UnbindScreenRoleButtons()
    {
        for (int i = 0; i < _roleButtonBindings.Count; i++)
        {
            RoleButtonBinding binding = _roleButtonBindings[i];
            if (binding.Button == null || binding.Listener == null)
                continue;

            binding.Button.onClick.RemoveListener(binding.Listener);
        }

        _roleButtonBindings.Clear();
    }

    private void HandleRoleButtonClicked(Define.TitanRole role)
    {
        if (_isLobbySetupPending || !Managers.LobbySession.HasJoinedLobbySession)
            return;

        TrySelectLocalRole(role);
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

    private void EnsureLocalLobbyCameraReady()
    {
        if (!Managers.LobbySession.HasJoinedLobbySession)
            return;

        if (_localLobbyCamera == null)
            _localLobbyCamera = Object.FindAnyObjectByType<LobbyCameraController>();

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

    private void HandleLocalPartSelectionInput()
    {
        if (_isLobbySetupPending || !Managers.LobbySession.HasJoinedLobbySession)
            return;

        if (Managers.Input.Mode != Define.InputMode.Player)
            return;

        if (!TryReadRoleSelectionInput(out Define.TitanRole selectedRole))
            return;

        TrySelectLocalRole(selectedRole);
    }

    private static bool TryReadRoleSelectionInput(out Define.TitanRole selectedRole)
    {
        selectedRole = Define.TitanRole.Body;
        if (Keyboard.current == null)
            return false;

        if (Keyboard.current.digit1Key.wasPressedThisFrame || Keyboard.current.numpad1Key.wasPressedThisFrame)
        {
            selectedRole = Define.TitanRole.Body;
            return true;
        }

        if (Keyboard.current.digit2Key.wasPressedThisFrame || Keyboard.current.numpad2Key.wasPressedThisFrame)
        {
            selectedRole = Define.TitanRole.LeftArm;
            return true;
        }

        if (Keyboard.current.digit3Key.wasPressedThisFrame || Keyboard.current.numpad3Key.wasPressedThisFrame)
        {
            selectedRole = Define.TitanRole.RightArm;
            return true;
        }

        if (Keyboard.current.digit4Key.wasPressedThisFrame || Keyboard.current.numpad4Key.wasPressedThisFrame)
        {
            selectedRole = Define.TitanRole.LeftLeg;
            return true;
        }

        if (Keyboard.current.digit5Key.wasPressedThisFrame || Keyboard.current.numpad5Key.wasPressedThisFrame)
        {
            selectedRole = Define.TitanRole.RightLeg;
            return true;
        }

        return false;
    }

    private static LobbyNetworkPlayer FindLocalOwnedNetworkPlayer()
    {
        LobbyNetworkPlayer[] players = Object.FindObjectsByType<LobbyNetworkPlayer>();
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
            string missingLabel = string.IsNullOrWhiteSpace(missingUserId) ? "unknown user" : missingUserId;
            Managers.Toast.EnqueueMessage($"{missingLabel} must select at least one part before starting.", 2.8f);
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
        Managers.Input.SetMode(shouldShow ? Define.InputMode.UI : Define.InputMode.Player);
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

        if (_screenHostStartButton != null)
            _screenHostStartButton.StartButtonClicked -= HandleHostStartButtonClicked;

        UnbindScreenRoleButtons();

        _localLobbyCamera = null;
        _screenHostStartButton = null;
    }
}
