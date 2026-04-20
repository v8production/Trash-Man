using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(RangerController))]
[RequireComponent(typeof(CharacterController))]
public class LobbyNetworkPlayer : NetworkBehaviour
{
    private const string LobbyCameraPrefabName = "Lobby_Camera";

    private readonly NetworkVariable<FixedString64Bytes> _discordUserId = new(default);
    private readonly NetworkVariable<FixedString64Bytes> _displayName = new(new FixedString64Bytes("Player"));
    private readonly NetworkVariable<int> _selectedTitanRole = new(0);
    private readonly NetworkVariable<TitanRoleInputPayload> _roleInput = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private RangerController _rangerController;
    private CharacterController _characterController;
    private UI_Nickname _nicknameUI;
    private LobbyCameraController _localCamera;

    public int SelectedTitanRoleValue => _selectedTitanRole.Value;
    public bool HasSelectedTitanRole => IsValidTitanRoleValue(_selectedTitanRole.Value);
    public TitanRoleInputPayload CurrentRoleInput => _roleInput.Value;
    public string DisplayName => GetDisplayName();

    private void Awake()
    {
        _rangerController = GetComponent<RangerController>();
        _characterController = GetComponent<CharacterController>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        gameObject.hideFlags = HideFlags.None;
        EnsureVisualComponentsEnabled();
        _discordUserId.OnValueChanged += HandleIdentityChanged;
        _displayName.OnValueChanged += HandleIdentityChanged;
        _selectedTitanRole.OnValueChanged += HandleSelectedRoleChanged;

        ApplyOwnershipState();
        EnsureNicknameUI();
        RefreshIdentityPresentation();

        if (IsOwner)
        {
            transform.position = GetInitialSpawnPosition();
            EnsureLocalCamera();
            SubmitIdentityServerRpc(Managers.Discord.LocalUserId, Managers.Discord.LocalDisplayName);
        }
    }

    public override void OnNetworkDespawn()
    {
        _discordUserId.OnValueChanged -= HandleIdentityChanged;
        _displayName.OnValueChanged -= HandleIdentityChanged;
        _selectedTitanRole.OnValueChanged -= HandleSelectedRoleChanged;

        string lobbyUserId = GetLobbyUserId();
        if (!string.IsNullOrWhiteSpace(lobbyUserId))
        {
            Managers.LobbySession.UnregisterLobbyUserObjects(lobbyUserId, _rangerController, _nicknameUI);
            LobbyScene.RegisterUserPartSelection(lobbyUserId, 0);
        }

        if (_nicknameUI != null)
            Destroy(_nicknameUI.gameObject);

        if (_localCamera != null)
            Destroy(_localCamera.gameObject);

        base.OnNetworkDespawn();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void SubmitIdentityServerRpc(FixedString64Bytes discordUserId, FixedString64Bytes displayName)
    {
        _discordUserId.Value = discordUserId;
        _displayName.Value = displayName;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void SubmitSelectedTitanRoleServerRpc(int titanRoleValue)
    {
        _selectedTitanRole.Value = NormalizeTitanRoleValue(titanRoleValue);
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
        role = Define.TitanRole.Body;
        if (!IsValidTitanRoleValue(_selectedTitanRole.Value))
            return false;

        role = (Define.TitanRole)_selectedTitanRole.Value;
        return true;
    }

    public void SelectTitanRole(Define.TitanRole titanRole)
    {
        if (!IsOwner)
            return;

        int roleValue = NormalizeTitanRoleValue((int)titanRole);
        if (_selectedTitanRole.Value == roleValue)
            return;

        SubmitSelectedTitanRoleServerRpc(roleValue);
    }

    public void PublishLocalRoleInput()
    {
        if (!IsOwner)
            return;

        if (Managers.Scene.CurrentScene == null || Managers.Scene.CurrentScene.SceneType != Define.Scene.Game)
            return;

        TitanAggregatedInput currentInput = TitanBaseController.CaptureCurrentInputSnapshot(updateShared: false);
        TitanRoleInputPayload payload = new(currentInput);
        if (_roleInput.Value.Equals(payload))
            return;

        SubmitRoleInputServerRpc(payload);
    }

    public static LobbyNetworkPlayer FindLocalOwnedPlayer()
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
        RefreshIdentityPresentation();
    }

    private void HandleSelectedRoleChanged(int previousValue, int newValue)
    {
        RefreshRoleSelectionPresentation();
    }

    private void ApplyOwnershipState()
    {
        if (_rangerController != null)
            _rangerController.enabled = IsOwner;

        if (_characterController != null)
            _characterController.enabled = IsOwner;
    }

    private void EnsureVisualComponentsEnabled()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].enabled = true;

        Animator[] animators = GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
            animators[i].enabled = true;
    }

    private void EnsureLocalCamera()
    {
        if (_localCamera != null)
        {
            _localCamera.SetTarget(transform);
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
            _localCamera.SetTarget(transform);
    }

    private void EnsureNicknameUI()
    {
        if (_nicknameUI != null)
            return;

        _nicknameUI = Managers.UI.CreateWorldSpaceUI<UI_Nickname>(transform, nameof(UI_Nickname));
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
            Managers.LobbySession.RegisterLobbyUserObjects(lobbyUserId, _rangerController, _nicknameUI);

        RefreshRoleSelectionPresentation();
    }

    private void RefreshRoleSelectionPresentation()
    {
        string lobbyUserId = GetLobbyUserId();
        if (string.IsNullOrWhiteSpace(lobbyUserId))
            return;

        LobbyScene.RegisterUserPartSelection(lobbyUserId, _selectedTitanRole.Value);
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

    private static bool IsValidTitanRoleValue(int roleValue)
    {
        return roleValue >= (int)Define.TitanRole.Body && roleValue <= (int)Define.TitanRole.RightLeg;
    }
}
