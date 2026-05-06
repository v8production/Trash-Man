using System.Collections;
using Netcode.Transports;
using Unity.Netcode;
using UnityEngine;

public class GameSceneQuickStartRunner : MonoBehaviour
{
    [SerializeField] private bool _runOnStart = true;
    [SerializeField] private bool _editorOnly = true;
    [SerializeField] private float _initialDelaySeconds = 0.1f;
    [SerializeField] private float _retryIntervalSeconds = 0.1f;
    [SerializeField] private int _maxRetryCount = 40;
    [SerializeField] private bool _verboseLog = true;

    private bool _started;

    private void Start()
    {
        if (!_runOnStart || _started)
            return;

        _started = true;
        StartCoroutine(RunQuickStartCoroutine());
    }

    private IEnumerator RunQuickStartCoroutine()
    {
        if (_editorOnly && !Application.isEditor)
            yield break;

        if (_initialDelaySeconds > 0f)
            yield return new WaitForSecondsRealtime(_initialDelaySeconds);

        int retryCount = Mathf.Max(1, _maxRetryCount);
        for (int attempt = 1; attempt <= retryCount; attempt++)
        {
            if (!LobbyNetworkRuntime.EnsureSetup(out NetworkManager networkManager, out SteamNetworkingSocketsTransport transport)
                || networkManager == null
                || transport == null)
            {
                yield return WaitRetry();
                continue;
            }

            if (!EnsureHostStarted(networkManager, attempt))
            {
                yield return WaitRetry();
                continue;
            }

            LobbyNetworkPlayer localPlayer = LobbyNetworkPlayer.FindLocalOwnedPlayer();
            if (localPlayer == null || !localPlayer.IsSpawned)
            {
                yield return WaitRetry();
                continue;
            }

            EnsureAllRolesSelected(localPlayer);

            if (HasAllRoles(localPlayer))
            {
                Log($"Quick start ready. clientId={localPlayer.OwnerClientId}, selectedMask=0x{localPlayer.SelectedTitanRoleMaskValue:X}");
                yield break;
            }

            yield return WaitRetry();
        }

        Debug.LogWarning("[GameQuickStart] Failed to prepare local host and five-role assignment within retry limit.");
    }

    private bool EnsureHostStarted(
        NetworkManager networkManager,
        int attempt)
    {
        if (networkManager == null)
            return false;

        if (networkManager.IsListening)
        {
            if (networkManager.IsHost)
                return true;

            networkManager.Shutdown();
            Log($"Restarting runtime as Steam host. attempt={attempt}");
            return false;
        }

        bool started = networkManager.StartHost();
        Log($"StartHost requested. attempt={attempt}, started={started}, steamId={Managers.Steam.LocalSteamId.m_SteamID}");
        return started;
    }

    private void EnsureAllRolesSelected(LobbyNetworkPlayer localPlayer)
    {
        SelectRoleIfMissing(localPlayer, Define.TitanRole.Torso);
        SelectRoleIfMissing(localPlayer, Define.TitanRole.LeftArm);
        SelectRoleIfMissing(localPlayer, Define.TitanRole.RightArm);
        SelectRoleIfMissing(localPlayer, Define.TitanRole.LeftLeg);
        SelectRoleIfMissing(localPlayer, Define.TitanRole.RightLeg);
    }

    private static void SelectRoleIfMissing(LobbyNetworkPlayer localPlayer, Define.TitanRole role)
    {
        if (localPlayer.HasSelectedTitanRoleValue(role))
            return;

        localPlayer.ToggleTitanRoleSelection(role);
    }

    private static bool HasAllRoles(LobbyNetworkPlayer localPlayer)
    {
        return localPlayer.HasSelectedTitanRoleValue(Define.TitanRole.Torso)
            && localPlayer.HasSelectedTitanRoleValue(Define.TitanRole.LeftArm)
            && localPlayer.HasSelectedTitanRoleValue(Define.TitanRole.RightArm)
            && localPlayer.HasSelectedTitanRoleValue(Define.TitanRole.LeftLeg)
            && localPlayer.HasSelectedTitanRoleValue(Define.TitanRole.RightLeg);
    }

    private CustomYieldInstruction WaitRetry()
    {
        return new WaitForSecondsRealtime(Mathf.Max(0.02f, _retryIntervalSeconds));
    }

    private void Log(string message)
    {
        if (_verboseLog)
            Debug.Log($"[GameQuickStart] {message}");
    }
}
