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
    private const int FirstTitanRoleValue = (int)Define.TitanRole.Torso;
    private const int LastTitanRoleValue = (int)Define.TitanRole.RightLeg;

    private readonly NetworkVariable<FixedString64Bytes> _discordUserId = new(default);
    private readonly NetworkVariable<FixedString64Bytes> _displayName = new(new FixedString64Bytes("Player"));
    private readonly NetworkVariable<int> _selectedTitanRoleMask = new(0);
    private readonly NetworkVariable<int> _activeTitanRole = new(0);
    private readonly NetworkVariable<TitanRoleInputPayload> _roleInput = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<TitanRigPosePayload> _titanPose = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private RangerController _lobbyRanger;
    private CharacterController _lobbyRangerCharacterController;
    private UI_Nickname _nicknameUI;
    private LobbyCameraController _localCamera;

    private Animator _remoteAnimator;
    private Vector3 _remoteLastPosition;
    private bool _remoteHasLastPosition;
    private bool _remoteWasWalking;

    private bool _submittedIdentity;

    public int SelectedTitanRoleMaskValue => NormalizeTitanRoleMask(_selectedTitanRoleMask.Value);
    public bool HasSelectedTitanRole => NormalizeTitanRoleMask(_selectedTitanRoleMask.Value) != 0;
    public int ActiveTitanRoleValue => NormalizeTitanRoleValue(_activeTitanRole.Value);
    public TitanRoleInputPayload CurrentRoleInput => _roleInput.Value;
    public TitanRigPosePayload CurrentTitanPose => _titanPose.Value;
    public string DisplayName => GetDisplayName();

    private float _nextPublishLogTime;
    private const float PublishLogIntervalSeconds = 0.50f;
    private const float AttachInputBufferSeconds = 0.20f;
    private float _attachInputBufferRemaining;

    private void Awake()
    {
        // This NetworkBehaviour lives on the minimal Netcode player object.
    }

    private void Update()
    {
        // Handle local control switching on the render frame so we don't miss key down events.
        TryHandleLocalRoleSwitchInput();

        // Publish local titan input from the owning network player itself.
        // This keeps input flow alive even if the Titan runtime discovers the local player later.
        PublishLocalRoleInput();

        // Netcode player objects can spawn before the LobbyScene finishes initializing.
        // Ensure lobby-local objects (ranger/camera/nickname) are created once the lobby scene is actually active.
        TryEnsureLobbyLocalObjects();
    }

    private void LateUpdate()
    {
        if (!IsSpawned)
            return;

        BaseScene scene = Managers.Scene.CurrentScene;
        if (scene == null || scene.SceneType != Define.Scene.Lobby)
            return;

        SyncLobbyRangerTransform();
    }

    private void TryEnsureLobbyLocalObjects()
    {
        if (!IsSpawned)
            return;

        BaseScene scene = Managers.Scene.CurrentScene;
        if (scene == null || scene.SceneType != Define.Scene.Lobby)
            return;

        EnsureLobbyRanger();
        ApplyOwnershipState();
        EnsureNicknameUI();

        if (IsOwner)
        {
            EnsureLocalCamera();

            if (!_submittedIdentity)
            {
                _submittedIdentity = true;
                SubmitIdentityServerRpc(Managers.Discord.LocalUserId, Managers.Discord.LocalDisplayName);
            }
        }

        // Register lobby objects once we have them; remote identity updates will refresh via OnValueChanged.
        RefreshIdentityPresentation();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // We use Unity scene loads (NGO scene management disabled), so this NetworkObject must survive
        // the Lobby -> Game transition for role/input routing.
        DontDestroyOnLoad(gameObject);

        _submittedIdentity = false;

        // Unity appends "(Clone)" to instantiated prefab names; use stable, readable names in Hierarchy.
        UpdateRuntimeObjectName();

        bool isGameScene = Managers.Scene.CurrentScene != null && Managers.Scene.CurrentScene.SceneType == Define.Scene.Game;
        bool isLobbyScene = Managers.Scene.CurrentScene != null && Managers.Scene.CurrentScene.SceneType == Define.Scene.Lobby;

        gameObject.hideFlags = HideFlags.None;
        _discordUserId.OnValueChanged += HandleIdentityChanged;
        _displayName.OnValueChanged += HandleIdentityChanged;
        _selectedTitanRoleMask.OnValueChanged += HandleSelectedRoleChanged;
        _activeTitanRole.OnValueChanged += HandleActiveRoleChanged;

        if (isLobbyScene)
        {
            EnsureLobbyRanger();
            ApplyOwnershipState();
            EnsureNicknameUI();
            RefreshIdentityPresentation();

            // Ensure an initial, deterministic spawn position is applied on the server.
            // This avoids a frame of (0,0,0) before the first NetworkTransform tick.
            if (IsServer)
            {
                Vector3 initial = GetInitialSpawnPosition();
                transform.SetPositionAndRotation(initial, Quaternion.identity);
                if (_lobbyRanger != null)
                    _lobbyRanger.transform.SetPositionAndRotation(initial, Quaternion.identity);
            }
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
                _submittedIdentity = true;
            }
            else if (isGameScene)
            {
                SubmitIdentityServerRpc(Managers.Discord.LocalUserId, Managers.Discord.LocalDisplayName);
                _submittedIdentity = true;
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        _discordUserId.OnValueChanged -= HandleIdentityChanged;
        _displayName.OnValueChanged -= HandleIdentityChanged;
        _selectedTitanRoleMask.OnValueChanged -= HandleSelectedRoleChanged;
        _activeTitanRole.OnValueChanged -= HandleActiveRoleChanged;

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
        role = Define.TitanRole.Torso;
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
        role = Define.TitanRole.Torso;
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

        TrySwitchActiveRoleFromDigit(1, Define.TitanRole.Torso);
        TrySwitchActiveRoleFromDigit(2, Define.TitanRole.LeftArm);
        TrySwitchActiveRoleFromDigit(3, Define.TitanRole.RightArm);
        TrySwitchActiveRoleFromDigit(4, Define.TitanRole.LeftLeg);
        TrySwitchActiveRoleFromDigit(5, Define.TitanRole.RightLeg);
    }

    private void TrySwitchActiveRoleFromDigit(int digit, Define.TitanRole role)
    {
        if (!Managers.Input.WasDigitPressedThisFrame(digit))
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
            // InputDebug.Log($"PublishLocalRoleInput (client={OwnerClientId}, isOwner={IsOwner}) selectedMask=0x{selectedMask:X}, activeRole={activeRole}");
        }

        TitanAggregatedInput currentInput = Managers.Input.CaptureTitanInput();
        if (currentInput.RightMousePressedThisFrame)
        {
            string activeRoleLabel = activeRole != 0 ? ((Define.TitanRole)activeRole).ToString() : "<none>";
            InputDebug.Log($"[TitanAttachClick] owner={OwnerClientId} selectedMask=0x{selectedMask:X} activeRole={activeRoleLabel} rmbHeld={currentInput.RightMouseHeld} mouse={currentInput.MousePosition} delta={currentInput.MouseDelta}");
            _attachInputBufferRemaining = AttachInputBufferSeconds;
        }
        else
        {
            _attachInputBufferRemaining = Mathf.Max(0f, _attachInputBufferRemaining - Time.unscaledDeltaTime);
        }

        currentInput.RightMouseAttachBuffered = currentInput.RightMouseHeld || _attachInputBufferRemaining > 0f;
        TitanRoleInputPayload payload = new(currentInput);
        if (_roleInput.Value.Equals(payload))
            return;

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

                if (!obj.TryGetComponent(out LobbyNetworkPlayer player))
                    continue;

                if (!player.IsSpawned)
                    continue;

                result.Add(player);
            }

            // If SpawnManager is present but doesn't yet report player objects (timing / scene load edge),
            // fall back to a scene search.
            if (result.Count > 0)
                return result.ToArray();
        }

        return Object.FindObjectsByType<LobbyNetworkPlayer>();
    }

    public static bool TryPublishServerTitanPose(TitanRigPosePayload posePayload)
    {
        LobbyNetworkPlayer publisher = FindServerPosePublisher();
        if (publisher == null || !publisher.IsServer || !publisher.IsSpawned)
            return false;

        if (publisher._titanPose.Value.Equals(posePayload))
            return true;

        publisher._titanPose.Value = posePayload;
        return true;
    }

    public static bool TryGetLatestTitanPose(out TitanRigPosePayload posePayload)
    {
        posePayload = default;

        LobbyNetworkPlayer publisher = FindServerPosePublisher();
        if (publisher == null || !publisher.IsSpawned)
            return false;

        posePayload = publisher._titanPose.Value;
        return posePayload.IsValid;
    }

    private static LobbyNetworkPlayer FindServerPosePublisher()
    {
        LobbyNetworkPlayer[] players = FindAllSpawnedPlayers();
        LobbyNetworkPlayer fallback = null;

        for (int i = 0; i < players.Length; i++)
        {
            LobbyNetworkPlayer player = players[i];
            if (player == null || !player.IsSpawned)
                continue;

            if (fallback == null || player.OwnerClientId < fallback.OwnerClientId)
                fallback = player;

            if (player.OwnerClientId == NetworkManager.ServerClientId)
                return player;
        }

        return fallback;
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

    private void HandleActiveRoleChanged(int previousValue, int newValue)
    {
        RefreshRoleSelectionPresentation();

        // Owner-side: when active role switches, reset virtual mouse baseline and detach buffer.
        // This prevents immediate pose snaps for roles that map absolute mouse position to joints (legs/arms).
        if (!IsOwner)
        {
            return;
        }

        BaseScene scene = Managers.Scene.CurrentScene;
        if (scene == null || scene.SceneType != Define.Scene.Game)
        {
            return;
        }

        _attachInputBufferRemaining = 0f;
        Managers.Input.ResetTitanMouseBaseline();
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
        Vector3 initial = GetInitialSpawnPosition();
        rangerObject.transform.SetPositionAndRotation(initial, Quaternion.identity);

        _lobbyRanger = rangerObject.GetComponent<RangerController>();
        _lobbyRangerCharacterController = rangerObject.GetComponent<CharacterController>();
        ApplyOwnershipState();
        UpdateLobbyRangerName();

        // On the owner, drive the network player object's transform from the visible lobby ranger.
        // This is what remote clients will replicate and follow.
        if (IsOwner)
            transform.SetPositionAndRotation(initial, Quaternion.identity);
    }

    private void SyncLobbyRangerTransform()
    {
        if (_lobbyRanger == null)
            return;

        if (IsOwner)
        {
            // Owner drives network transform (replicated to server/others via OwnerNetworkTransform).
            Transform rangerTransform = _lobbyRanger.transform;
            transform.SetPositionAndRotation(rangerTransform.position, rangerTransform.rotation);
            return;
        }

        // Non-owners follow the replicated network player transform.
        Transform networkTransform = transform;
        Transform ranger = _lobbyRanger.transform;
        ranger.SetPositionAndRotation(networkTransform.position, networkTransform.rotation);
        UpdateRemoteRangerAnimation();
    }

    private void UpdateRemoteRangerAnimation()
    {
        if (_lobbyRanger == null)
            return;

        if (_remoteAnimator == null)
            _remoteAnimator = _lobbyRanger.GetComponentInChildren<Animator>(true);

        if (_remoteAnimator == null)
            return;

        Vector3 currentPos = _lobbyRanger.transform.position;
        if (!_remoteHasLastPosition)
        {
            _remoteHasLastPosition = true;
            _remoteLastPosition = currentPos;
            _remoteAnimator.CrossFade("idle", 0.05f);
            _remoteWasWalking = false;
            return;
        }

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        float speed = (currentPos - _remoteLastPosition).magnitude / dt;
        _remoteLastPosition = currentPos;

        bool walking = speed > 0.15f;
        if (walking == _remoteWasWalking)
            return;

        _remoteWasWalking = walking;
        _remoteAnimator.CrossFade(walking ? "walk" : "idle", 0.10f);
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

        // Prefer reusing an authored/existing lobby camera when present.
        LobbyCameraController existingCamera = Object.FindAnyObjectByType<LobbyCameraController>();
        if (existingCamera != null)
        {
            _localCamera = existingCamera;
            _localCamera.SetTarget(_lobbyRanger != null ? _lobbyRanger.transform : transform);
            return;
        }

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

        _nicknameUI = Managers.UI.CreateWorldSpaceUI<UI_Nickname>(_lobbyRanger.transform);
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
        // Network player object is already in DontDestroyOnLoad.

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

        // Keep discoverable so role/input routing can find it in GameScene.
        // (HideInHierarchy can cause FindObjectsByType fallbacks to miss it.)
        gameObject.hideFlags = HideFlags.None;
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

        return Define.TitanRole.Torso;
    }

    private static bool IsValidTitanRoleValue(int roleValue)
    {
        return roleValue >= FirstTitanRoleValue && roleValue <= LastTitanRoleValue;
    }
}
