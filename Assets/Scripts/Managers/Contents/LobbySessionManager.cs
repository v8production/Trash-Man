using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public class LobbySessionManager
{
    private const string RangerPrefabName = "Ranger(TEMP)";
    private const string LobbyCameraPrefabName = "Lobby_Camera";
    private const int JoinCodeLength = 6;

    private static readonly char[] JoinCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789".ToCharArray();

    private readonly Dictionary<string, RangerController> _rangersByUserId = new();
    private readonly Dictionary<string, UI_Nickname> _nicknamesByUserId = new();

    public bool IsHosting { get; private set; }
    public string HostUserId { get; private set; } = string.Empty;
    public string CurrentJoinCode { get; private set; } = string.Empty;

    public void Init()
    {
    }

    public void Clear()
    {
        _rangersByUserId.Clear();
        _nicknamesByUserId.Clear();
        IsHosting = false;
        HostUserId = string.Empty;
        CurrentJoinCode = string.Empty;
    }

    public void BootstrapLocalHostLobby()
    {
        if (!Managers.Discord.IsLinked)
        {
            Debug.LogWarning("Lobby host bootstrap skipped: Discord account is not linked.");
            return;
        }

        CleanupExistingLobbyObjects();

        HostUserId = Managers.Discord.LocalUserId;
        IsHosting = TryStartUtpHost();

        CurrentJoinCode = GenerateJoinCode();
        GUIUtility.systemCopyBuffer = CurrentJoinCode;

        RangerController ranger = SpawnRangerForLocalUser();
        SetupLobbyCamera(ranger);
        SetupNicknameForLocalUser(ranger);

        Managers.Discord.NotifyLobbyUserJoined(Managers.Discord.LocalUserId, Managers.Discord.LocalDisplayName, true);

        Debug.Log($"Lobby host ready: user={Managers.Discord.LocalDisplayName}, joinCode={CurrentJoinCode}, utpHostStarted={IsHosting}");
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
        _nicknamesByUserId[Managers.Discord.LocalUserId] = nicknameUI;
    }

    private static string GenerateJoinCode()
    {
        char[] chars = new char[JoinCodeLength];
        for (int i = 0; i < chars.Length; i++)
            chars[i] = JoinCodeAlphabet[UnityEngine.Random.Range(0, JoinCodeAlphabet.Length)];

        return new string(chars);
    }

    private bool TryStartUtpHost()
    {
        try
        {
            Type networkManagerType = Type.GetType("Unity.Netcode.NetworkManager, Unity.Netcode.Runtime");
            if (networkManagerType == null)
            {
                Debug.LogWarning("UTP host start skipped: Netcode for GameObjects package is not installed.");
                return false;
            }

            MonoBehaviour[] behaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            MonoBehaviour networkManagerObject = null;
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
                Debug.LogWarning("UTP host start skipped: no NetworkManager found in Lobby scene.");
                return false;
            }

            Component utpTransport = networkManagerObject.GetComponent("Unity.Netcode.Transports.UTP.UnityTransport");
            if (utpTransport == null)
            {
                Debug.LogWarning("UTP host start skipped: NetworkManager exists but UnityTransport component is missing.");
                return false;
            }

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
}
