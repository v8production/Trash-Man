using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

[RequireComponent(typeof(NetworkObject))]
public class LobbyNetworkPlayer : NetworkBehaviour
{
    private const string LobbyCameraPrefabName = "Lobby_Camera";
    private const string LobbyRangerPrefabName = "Ranger(TEMP)";
    private const int FirstTitanRoleValue = (int)Define.TitanRole.Body;
    private const int LastTitanRoleValue = (int)Define.TitanRole.RightLeg;

    private readonly NetworkVariable<FixedString64Bytes> _discordUserId = new(default);
    private readonly NetworkVariable<FixedString64Bytes> _displayName = new(new FixedString64Bytes("Player"));
    private readonly NetworkVariable<int> _selectedTitanRoleMask = new(0);
    private readonly NetworkVariable<int> _activeTitanRole = new(0);
    private readonly NetworkVariable<TitanRoleInputPayload> _roleInput = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private RangerController _lobbyRanger;
    private CharacterController _lobbyRangerCharacterController;
    private UI_Nickname _nicknameUI;
    private LobbyCameraController _localCamera;

    public int SelectedTitanRoleMaskValue => NormalizeTitanRoleMask(_selectedTitanRoleMask.Value);
    public bool HasSelectedTitanRole => NormalizeTitanRoleMask(_selectedTitanRoleMask.Value) != 0;
    public int ActiveTitanRoleValue => NormalizeTitanRoleValue(_activeTitanRole.Value);
    public TitanRoleInputPayload CurrentRoleInput => _roleInput.Value;
    public string DisplayName => GetDisplayName();

    private float _nextPublishLogTime;
    private const float PublishLogIntervalSeconds = 0.50f;

    private void Awake()
    {
        // This NetworkBehaviour lives on the minimal Netcode player object.
    }

    private void Update()
    {
        // Handle local control switching on the render frame so we don't miss key down events.
        TryHandleLocalRoleSwitchInput();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Unity appends "(Clone)" to instantiated prefab names; use stable, readable names in Hierarchy.
        UpdateRuntimeObjectName();

        bool isGameScene = Managers.Scene.CurrentScene != null && Managers.Scene.CurrentScene.SceneType == Define.Scene.Game;
        bool isLobbyScene = Managers.Scene.CurrentScene != null && Managers.Scene.CurrentScene.SceneType == Define.Scene.Lobby;

        gameObject.hideFlags = HideFlags.None;
        _discordUserId.OnValueChanged += HandleIdentityChanged;
        _displayName.OnValueChanged += HandleIdentityChanged;
        _selectedTitanRoleMask.OnValueChanged += HandleSelectedRoleChanged;
        _activeTitanRole.OnValueChanged += HandleSelectedRoleChanged;

        if (isLobbyScene)
        {
            EnsureLobbyRanger();
            ApplyOwnershipState();
            EnsureNicknameUI();
            RefreshIdentityPresentation();
        }
        else if (isGameScene)
        {
            Transform runtimeRoot = NetworkManager != null ? NetworkManager.transform : GameObject.Find("@NetworkManager")?.transform;
            PrepareForGameScene(runtimeRoot);

            // Always print once per spawn so we can verify this object exists in GameScene builds.
            Debug.Log($"{InputDebug.Prefix} OnNetworkSpawn(Game) ownerClientId={OwnerClientId} isOwner={IsOwner} selectedMask=0x{SelectedTitanRoleMaskValue:X} activeRole={ActiveTitanRoleValue}");
        }

        if (IsOwner)
        {
            if (isLobbyScene)
            {
                EnsureLocalCamera();
                SubmitIdentityServerRpc(Managers.Discord.LocalUserId, Managers.Discord.LocalDisplayName);
            }
            else if (isGameScene)
            {
                SubmitIdentityServerRpc(Managers.Discord.LocalUserId, Managers.Discord.LocalDisplayName);
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        _discordUserId.OnValueChanged -= HandleIdentityChanged;
        _displayName.OnValueChanged -= HandleIdentityChanged;
        _selectedTitanRoleMask.OnValueChanged -= HandleSelectedRoleChanged;
        _activeTitanRole.OnValueChanged -= HandleSelectedRoleChanged;

        string lobbyUserId = GetLobbyUserId();
        if (!string.IsNullOrWhiteSpace(lobbyUserId))
        {
            Managers.LobbySession.UnregisterLobbyUserObjects(lobbyUserId, _lobbyRanger, _nicknameUI);
            LobbyScene.RegisterUserPartSelection(lobbyUserId, 0);
        }

        if (_nicknameUI != null)
            Destroy(_nicknameUI.gameObject);

        if (_localCamera != null)
            Destroy(_localCamera.gameObject);

        if (_lobbyRanger != null)
            Destroy(_lobbyRanger.gameObject);

        base.OnNetworkDespawn();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void SubmitIdentityServerRpc(FixedString64Bytes discordUserId, FixedString64Bytes displayName)
    {
        _discordUserId.Value = discordUserId;
        _displayName.Value = displayName;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void SubmitSelectedTitanRoleMaskServerRpc(int titanRoleMask)
    {
        InputDebug.Log($"[ServerRpc] SubmitSelectedTitanRoleMaskServerRpc from client={OwnerClientId} raw=0x{titanRoleMask:X}");
        _selectedTitanRoleMask.Value = NormalizeTitanRoleMask(titanRoleMask);

        int normalizedMask = NormalizeTitanRoleMask(_selectedTitanRoleMask.Value);
        int activeRoleValue = NormalizeTitanRoleValue(_activeTitanRole.Value);
        if (normalizedMask == 0)
        {
            _activeTitanRole.Value = 0;
            return;
        }

        int activeBit = activeRoleValue != 0 ? (1 << (activeRoleValue - FirstTitanRoleValue)) : 0;
        if (activeBit == 0 || (normalizedMask & activeBit) == 0)
            _activeTitanRole.Value = (int)GetFirstRoleFromMask(normalizedMask);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void SubmitActiveTitanRoleServerRpc(int titanRoleValue)
    {
        InputDebug.Log($"[ServerRpc] SubmitActiveTitanRoleServerRpc from client={OwnerClientId} value={titanRoleValue}");
        int normalizedRoleValue = NormalizeTitanRoleValue(titanRoleValue);
        if (normalizedRoleValue == 0)
            return;

        int mask = NormalizeTitanRoleMask(_selectedTitanRoleMask.Value);
        int bit = 1 << (normalizedRoleValue - FirstTitanRoleValue);
        if ((mask & bit) == 0)
            return;

        _activeTitanRole.Value = normalizedRoleValue;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void SubmitRoleInputServerRpc(TitanRoleInputPayload inputPayload)
    {
        TitanAggregatedInput snapshot = inputPayload.ToAggregatedInput();
        bool hasInput = Mathf.Abs(snapshot.BodyWaist) > 0.001f
            || Mathf.Abs(snapshot.LeftArmElbow) > 0.001f
            || Mathf.Abs(snapshot.BodyForward) > 0.001f
            || Mathf.Abs(snapshot.BodyStrafe) > 0.001f
            || Mathf.Abs(snapshot.BodyTurn) > 0.001f;
        if (hasInput)
            InputDebug.Log($"[ServerRpc] SubmitRoleInputServerRpc from client={OwnerClientId} waist={snapshot.BodyWaist} ws={snapshot.LeftArmElbow} fwd={snapshot.BodyForward} strafe={snapshot.BodyStrafe}");
        _roleInput.Value = inputPayload;
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void LoadGameSceneClientRpc()
    {
        Managers.Scene.LoadScene(Define.Scene.Game);
    }

    public bool TryGetLobbyUserId(out string userId)
    {
        userId = GetLobbyUserId();
        return !string.IsNullOrWhiteSpace(userId);
    }

    public bool TryGetSelectedRole(out Define.TitanRole role)
    {
        role = Define.TitanRole.Body;
        int mask = NormalizeTitanRoleMask(_selectedTitanRoleMask.Value);
        if (mask == 0)
            return false;

        role = GetFirstRoleFromMask(mask);
        return true;
    }

    public bool TryGetSelectedRoleMask(out int roleMask)
    {
        roleMask = NormalizeTitanRoleMask(_selectedTitanRoleMask.Value);
        return roleMask != 0;
    }

    public bool HasSelectedTitanRoleValue(Define.TitanRole titanRole)
    {
        int bit = RoleToMaskBit(titanRole);
        if (bit == 0)
            return false;

        return (NormalizeTitanRoleMask(_selectedTitanRoleMask.Value) & bit) != 0;
    }

    public void ToggleTitanRoleSelection(Define.TitanRole titanRole)
    {
        if (!IsOwner)
            return;

        int bit = RoleToMaskBit(titanRole);
        if (bit == 0)
            return;

        int currentMask = NormalizeTitanRoleMask(_selectedTitanRoleMask.Value);
        int nextMask = NormalizeTitanRoleMask(currentMask ^ bit);
        if (nextMask == currentMask)
            return;

        SubmitSelectedTitanRoleMaskServerRpc(nextMask);
    }

    public bool TryGetActiveTitanRole(out Define.TitanRole role)
    {
        role = Define.TitanRole.Body;
        int activeRoleValue = NormalizeTitanRoleValue(_activeTitanRole.Value);
        if (activeRoleValue == 0)
            return false;

        int mask = NormalizeTitanRoleMask(_selectedTitanRoleMask.Value);
        int bit = 1 << (activeRoleValue - FirstTitanRoleValue);
        if ((mask & bit) == 0)
            return false;

        role = (Define.TitanRole)activeRoleValue;
        return true;
    }

    public bool IsActivelyControllingRole(Define.TitanRole role)
    {
        if (!TryGetActiveTitanRole(out Define.TitanRole active))
            return false;

        return active == role;
    }

    public void TryHandleLocalRoleSwitchInput()
    {
        if (!IsOwner)
            return;

        if (Managers.Scene.CurrentScene == null || Managers.Scene.CurrentScene.SceneType != Define.Scene.Game)
            return;

        TrySwitchActiveRoleFromDigit(1, Define.TitanRole.Body);
        TrySwitchActiveRoleFromDigit(2, Define.TitanRole.LeftArm);
        TrySwitchActiveRoleFromDigit(3, Define.TitanRole.RightArm);
        TrySwitchActiveRoleFromDigit(4, Define.TitanRole.LeftLeg);
        TrySwitchActiveRoleFromDigit(5, Define.TitanRole.RightLeg);
    }

    private void TrySwitchActiveRoleFromDigit(int digit, Define.TitanRole role)
    {
        if (!TitanInputUtility.WasDigitPressedThisFrame(digit))
            return;

        InputDebug.Log($"Digit{digit} pressed (client={OwnerClientId}, isOwner={IsOwner}). role={role}, selectedMask=0x{SelectedTitanRoleMaskValue:X}, activeRole={ActiveTitanRoleValue}");

        if (!HasSelectedTitanRoleValue(role))
        {
            InputDebug.LogWarning($"Digit{digit} ignored: role {role} not in selectedMask (mask=0x{SelectedTitanRoleMaskValue:X}).");
            return;
        }

        InputDebug.Log($"Switching active role -> {role} (rpc)");
        SubmitActiveTitanRoleServerRpc((int)role);
    }

    public void PublishLocalRoleInput()
    {
        if (!IsOwner)
            return;

        if (Managers.Scene.CurrentScene == null || Managers.Scene.CurrentScene.SceneType != Define.Scene.Game)
            return;

        // If the user owns roles but hasn't established an active one yet, pick the first role.
        if (!TryGetActiveTitanRole(out _))
        {
            int mask = NormalizeTitanRoleMask(_selectedTitanRoleMask.Value);
            if (mask != 0)
                SubmitActiveTitanRoleServerRpc((int)GetFirstRoleFromMask(mask));
        }

        int selectedMask = NormalizeTitanRoleMask(_selectedTitanRoleMask.Value);
        int activeRole = NormalizeTitanRoleValue(_activeTitanRole.Value);

        if (Time.unscaledTime >= _nextPublishLogTime)
        {
            _nextPublishLogTime = Time.unscaledTime + PublishLogIntervalSeconds;
            InputDebug.Log($"PublishLocalRoleInput (client={OwnerClientId}, isOwner={IsOwner}) selectedMask=0x{selectedMask:X}, activeRole={activeRole}");
        }

        TitanAggregatedInput currentInput = TitanBaseController.CaptureCurrentInputSnapshot(updateShared: false);
        TitanRoleInputPayload payload = new(currentInput);
        if (_roleInput.Value.Equals(payload))
            return;

        InputDebug.Log($"SubmitRoleInputServerRpc (mask=0x{selectedMask:X}, activeRole={activeRole}) waist={currentInput.BodyWaist} ws={currentInput.LeftArmElbow}");

        SubmitRoleInputServerRpc(payload);
    }

    public static LobbyNetworkPlayer FindLocalOwnedPlayer()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.SpawnManager != null)
        {
            ulong localClientId = networkManager.LocalClientId;
            var spawned = networkManager.SpawnManager.SpawnedObjectsList;
            foreach (NetworkObject obj in spawned)
            {
                if (obj == null || !obj.IsPlayerObject || obj.OwnerClientId != localClientId)
                    continue;

                if (obj.TryGetComponent(out LobbyNetworkPlayer player))
                    return player;
            }
        }

        LobbyNetworkPlayer[] players = Object.FindObjectsByType<LobbyNetworkPlayer>();
        for (int i = 0; i < players.Length; i++)
        {
            LobbyNetworkPlayer player = players[i];
            if (player != null && player.IsOwner)
                return player;
        }

        return null;
    }

    public static LobbyNetworkPlayer[] FindAllSpawnedPlayers()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.SpawnManager != null)
        {
            var spawned = networkManager.SpawnManager.SpawnedObjectsList;
            List<LobbyNetworkPlayer> result = new();
            foreach (NetworkObject obj in spawned)
            {
                if (obj == null)
                    continue;

                if (!obj.TryGetComponent(out LobbyNetworkPlayer player) || player == null)
                    continue;

                if (!player.IsSpawned)
                    continue;

                result.Add(player);
            }

            return result.ToArray();
        }

        return Object.FindObjectsByType<LobbyNetworkPlayer>();
    }

    public static bool RequestLoadGameForAll()
    {
        LobbyNetworkPlayer[] players = Object.FindObjectsByType<LobbyNetworkPlayer>();
        for (int i = 0; i < players.Length; i++)
        {
            LobbyNetworkPlayer player = players[i];
            if (player == null || !player.IsServer || !player.IsSpawned)
                continue;

            if (player.TryLoadGameSceneForSession())
                return true;

            player.LoadGameSceneClientRpc();
            return true;
        }

        return false;
    }

    private bool TryLoadGameSceneForSession()
    {
        if (!IsServer || NetworkManager == null)
            return false;

        if (!NetworkManager.NetworkConfig.EnableSceneManagement || NetworkManager.SceneManager == null)
            return false;

        SceneEventProgressStatus status = NetworkManager.SceneManager.LoadScene(Util.GetEnumName(Define.Scene.Game), LoadSceneMode.Single);
        return status == SceneEventProgressStatus.Started || status == SceneEventProgressStatus.SceneEventInProgress;
    }

    private void HandleIdentityChanged(FixedString64Bytes previousValue, FixedString64Bytes newValue)
    {
        UpdateRuntimeObjectName();
        UpdateLobbyRangerName();
        RefreshIdentityPresentation();
    }

    private void HandleSelectedRoleChanged(int previousValue, int newValue)
    {
        RefreshRoleSelectionPresentation();
    }

    private void UpdateRuntimeObjectName()
    {
        string userId = GetLobbyUserId();
        string suffix = string.IsNullOrWhiteSpace(userId) ? OwnerClientId.ToString() : userId;
        gameObject.name = $"@NetworkObject({suffix})";
    }

    private void UpdateLobbyRangerName()
    {
        if (_lobbyRanger == null)
            return;

        string userId = GetLobbyUserId();
        string suffix = string.IsNullOrWhiteSpace(userId) ? OwnerClientId.ToString() : userId;
        _lobbyRanger.gameObject.name = $"Ranger({suffix})";
    }

    private void ApplyOwnershipState()
    {
        bool isLobbyScene = Managers.Scene.CurrentScene != null && Managers.Scene.CurrentScene.SceneType == Define.Scene.Lobby;

        if (_lobbyRanger != null)
            _lobbyRanger.enabled = isLobbyScene && IsOwner;

        if (_lobbyRangerCharacterController != null)
            _lobbyRangerCharacterController.enabled = isLobbyScene && IsOwner;
    }

    private void EnsureLobbyRanger()
    {
        if (_lobbyRanger != null)
            return;

        GameObject rangerObject = Managers.Resource.Instantiate(LobbyRangerPrefabName);
        if (rangerObject == null)
            return;

        rangerObject.name = $"Ranger({OwnerClientId})";
        rangerObject.transform.position = GetInitialSpawnPosition();

        _lobbyRanger = rangerObject.GetComponent<RangerController>();
        _lobbyRangerCharacterController = rangerObject.GetComponent<CharacterController>();
        ApplyOwnershipState();
        UpdateLobbyRangerName();
    }

    private void EnsureLocalCamera()
    {
        if (_lobbyRanger == null)
            EnsureLobbyRanger();

        if (_localCamera != null)
        {
            _localCamera.SetTarget(_lobbyRanger != null ? _lobbyRanger.transform : transform);
            return;
        }

        LobbyCameraController[] existingCameras = Object.FindObjectsByType<LobbyCameraController>();
        for (int i = 0; i < existingCameras.Length; i++)
            Object.Destroy(existingCameras[i].gameObject);

        GameObject cameraObject = Managers.Resource.Instantiate(LobbyCameraPrefabName);
        if (cameraObject == null)
            return;

        _localCamera = cameraObject.GetComponent<LobbyCameraController>();
        if (_localCamera != null)
            _localCamera.SetTarget(_lobbyRanger != null ? _lobbyRanger.transform : transform);
    }

    private void EnsureNicknameUI()
    {
        if (_nicknameUI != null)
            return;

        if (_lobbyRanger == null)
            EnsureLobbyRanger();

        if (_lobbyRanger == null)
            return;

        _nicknameUI = Managers.UI.CreateWorldSpaceUI<UI_Nickname>(_lobbyRanger.transform, nameof(UI_Nickname));
        if (_nicknameUI == null)
            return;

        _nicknameUI.SetVoiceChatActive(Managers.Discord.IsLobbyUserVoiceChatActive(GetLobbyUserId()));
    }

    private void RefreshIdentityPresentation()
    {
        if (_nicknameUI != null)
        {
            _nicknameUI.SetText(GetDisplayName());
            _nicknameUI.SetVoiceChatActive(Managers.Discord.IsLobbyUserVoiceChatActive(GetLobbyUserId()));
        }

        string lobbyUserId = GetLobbyUserId();
        if (!string.IsNullOrWhiteSpace(lobbyUserId))
            Managers.LobbySession.RegisterLobbyUserObjects(lobbyUserId, _lobbyRanger, _nicknameUI);

        RefreshRoleSelectionPresentation();
    }

    private void RefreshRoleSelectionPresentation()
    {
        string lobbyUserId = GetLobbyUserId();
        if (string.IsNullOrWhiteSpace(lobbyUserId))
            return;

        LobbyScene.RegisterUserPartSelection(lobbyUserId, NormalizeTitanRoleMask(_selectedTitanRoleMask.Value));
    }

    public void PrepareForGameScene(Transform runtimeRoot)
    {
        // Keep the network player object alive for role input syncing.
        DontDestroyOnLoad(gameObject);

        if (_nicknameUI != null)
            Destroy(_nicknameUI.gameObject);

        if (_localCamera != null)
            Destroy(_localCamera.gameObject);

        if (_lobbyRanger != null)
            Destroy(_lobbyRanger.gameObject);

        // Netcode forbids parenting a NetworkObject under a non-NetworkObject parent.
        // This object is kept alive via DontDestroyOnLoad, so we don't need to reparent it.
        // if (runtimeRoot != null)
        //     transform.SetParent(runtimeRoot, true);

        gameObject.hideFlags = HideFlags.HideInHierarchy;
    }

    public bool TryGetLobbyRangerTransform(out Transform rangerTransform)
    {
        rangerTransform = null;

        if (_lobbyRanger == null)
            return false;

        rangerTransform = _lobbyRanger.transform;
        return rangerTransform != null;
    }

    private string GetLobbyUserId()
    {
        string syncedUserId = _discordUserId.Value.ToString();
        if (!string.IsNullOrWhiteSpace(syncedUserId))
            return syncedUserId;

        return IsOwner ? Managers.Discord.LocalUserId : string.Empty;
    }

    private string GetDisplayName()
    {
        string syncedDisplayName = _displayName.Value.ToString();
        if (!string.IsNullOrWhiteSpace(syncedDisplayName))
            return syncedDisplayName;

        return IsOwner ? Managers.Discord.LocalDisplayName : $"Player {OwnerClientId}";
    }

    private Vector3 GetInitialSpawnPosition()
    {
        int slot = (int)(OwnerClientId % 4);
        return new Vector3(slot * 2.5f, 0f, 0f);
    }

    private static int NormalizeTitanRoleValue(int roleValue)
    {
        return IsValidTitanRoleValue(roleValue) ? roleValue : 0;
    }

    private static int NormalizeTitanRoleMask(int roleMask)
    {
        return roleMask & GetAllTitanRoleMask();
    }

    private static int GetAllTitanRoleMask()
    {
        int count = (LastTitanRoleValue - FirstTitanRoleValue) + 1;
        return (1 << count) - 1;
    }

    private static int RoleToMaskBit(Define.TitanRole role)
    {
        int roleValue = (int)role;
        if (!IsValidTitanRoleValue(roleValue))
            return 0;

        return 1 << (roleValue - FirstTitanRoleValue);
    }

    private static Define.TitanRole GetFirstRoleFromMask(int roleMask)
    {
        int normalized = NormalizeTitanRoleMask(roleMask);
        for (int roleValue = FirstTitanRoleValue; roleValue <= LastTitanRoleValue; roleValue++)
        {
            int bit = 1 << (roleValue - FirstTitanRoleValue);
            if ((normalized & bit) != 0)
                return (Define.TitanRole)roleValue;
        }

        return Define.TitanRole.Body;
    }

    private static bool IsValidTitanRoleValue(int roleValue)
    {
        return roleValue >= FirstTitanRoleValue && roleValue <= LastTitanRoleValue;
    }
}
