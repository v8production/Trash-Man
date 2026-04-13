using System.Reflection;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public static class LobbyNetworkRuntime
{
    private const string RuntimeRootName = "@LobbyNetworkRuntime";
    private const string RangerPrefabPath = "Prefabs/Ranger(TEMP)";
    private const uint LobbyPlayerPrefabHash = 1804289383;

    private static GameObject s_runtimePlayerPrefab;

    public static bool EnsureSetup()
    {
        return EnsureSetup(out _, out _);
    }

    public static bool EnsureSetup(out NetworkManager networkManager, out UnityTransport transport)
    {
        networkManager = Object.FindFirstObjectByType<NetworkManager>();
        transport = networkManager != null ? networkManager.GetComponent<UnityTransport>() : null;

        if (networkManager == null)
        {
            GameObject runtimeRoot = GameObject.Find(RuntimeRootName);
            if (runtimeRoot == null)
                runtimeRoot = new GameObject { name = RuntimeRootName };

            networkManager = runtimeRoot.GetComponent<NetworkManager>();
            if (networkManager == null)
                networkManager = runtimeRoot.AddComponent<NetworkManager>();
        }

        if (transport == null)
        {
            transport = networkManager.GetComponent<UnityTransport>();
            if (transport == null)
                transport = networkManager.gameObject.AddComponent<UnityTransport>();
        }

        GameObject playerPrefab = EnsurePlayerPrefab();
        if (playerPrefab == null)
            return false;

        networkManager.NetworkConfig.NetworkTransport = transport;
        networkManager.NetworkConfig.EnableSceneManagement = false;
        networkManager.NetworkConfig.ConnectionApproval = false;
        networkManager.NetworkConfig.ForceSamePrefabs = false;
        networkManager.NetworkConfig.PlayerPrefab = playerPrefab;
        networkManager.AddNetworkPrefab(playerPrefab);
        return true;
    }

    private static GameObject EnsurePlayerPrefab()
    {
        if (s_runtimePlayerPrefab != null)
            return s_runtimePlayerPrefab;

        GameObject rangerPrefab = Managers.Resource.Load<GameObject>(RangerPrefabPath);
        if (rangerPrefab == null)
        {
            Debug.LogError($"[Lobby] Failed to load runtime player prefab at {RangerPrefabPath}.");
            return null;
        }

        s_runtimePlayerPrefab = Object.Instantiate(rangerPrefab);
        s_runtimePlayerPrefab.name = "@RangerNetworkPlayerPrefab";
        s_runtimePlayerPrefab.transform.position = new Vector3(10000f, -10000f, 10000f);
        s_runtimePlayerPrefab.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;

        RangerController rangerController = s_runtimePlayerPrefab.GetComponent<RangerController>();
        if (rangerController != null)
            rangerController.enabled = false;

        CharacterController characterController = s_runtimePlayerPrefab.GetComponent<CharacterController>();
        if (characterController != null)
            characterController.enabled = false;

        NetworkObject networkObject = s_runtimePlayerPrefab.GetorAddComponent<NetworkObject>();
        EnsureGlobalObjectIdHash(networkObject, LobbyPlayerPrefabHash);

        s_runtimePlayerPrefab.GetorAddComponent<OwnerNetworkTransform>();
        s_runtimePlayerPrefab.GetorAddComponent<OwnerNetworkAnimator>();
        s_runtimePlayerPrefab.GetorAddComponent<LobbyNetworkPlayer>();

        return s_runtimePlayerPrefab;
    }

    private static void EnsureGlobalObjectIdHash(NetworkObject networkObject, uint hash)
    {
        FieldInfo hashField = typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic);
        if (hashField == null)
            return;

        uint currentValue = (uint)hashField.GetValue(networkObject);
        if (currentValue == 0)
            hashField.SetValue(networkObject, hash);
    }
}
