using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(RangerController))]
[RequireComponent(typeof(CharacterController))]
public class LobbyNetworkPlayer : NetworkBehaviour
{
    private const string LobbyCameraPrefabName = "Lobby_Camera";

    private readonly NetworkVariable<FixedString64Bytes> _discordUserId = new(default);
    private readonly NetworkVariable<FixedString64Bytes> _displayName = new(new FixedString64Bytes("Player"));

    private RangerController _rangerController;
    private CharacterController _characterController;
    private UI_Nickname _nicknameUI;
    private LobbyCameraController _localCamera;

    private void Awake()
    {
        _rangerController = GetComponent<RangerController>();
        _characterController = GetComponent<CharacterController>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        gameObject.hideFlags = HideFlags.None;
        _discordUserId.OnValueChanged += HandleIdentityChanged;
        _displayName.OnValueChanged += HandleIdentityChanged;

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

        string lobbyUserId = GetLobbyUserId();
        if (!string.IsNullOrWhiteSpace(lobbyUserId))
            Managers.LobbySession.UnregisterLobbyUserObjects(lobbyUserId, _rangerController, _nicknameUI);

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

    private void HandleIdentityChanged(FixedString64Bytes previousValue, FixedString64Bytes newValue)
    {
        RefreshIdentityPresentation();
    }

    private void ApplyOwnershipState()
    {
        if (_rangerController != null)
            _rangerController.enabled = IsOwner;

        if (_characterController != null)
            _characterController.enabled = IsOwner;
    }

    private void EnsureLocalCamera()
    {
        if (_localCamera != null)
        {
            _localCamera.SetTarget(transform);
            return;
        }

        LobbyCameraController[] existingCameras = Object.FindObjectsByType<LobbyCameraController>(FindObjectsSortMode.None);
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
}
