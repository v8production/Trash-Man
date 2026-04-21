using System.Reflection;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public static class LobbyNetworkRuntime
{
    private const string RuntimeRootName = "@NetworkManager";
    private const string NetworkObjectPrefabPath = "Prefabs/@NetworkObject";
    private const uint LobbyPlayerPrefabHash = 1804289383;

    private static GameObject s_runtimePlayerPrefab;
    private static bool s_inputDebugBootLogged;

    public static bool EnsureSetup()
    {
        return EnsureSetup(out _, out _);
    }

    public static bool EnsureSetup(out NetworkManager networkManager, out UnityTransport transport)
    {
        networkManager = Object.FindAnyObjectByType<NetworkManager>();
        transport = networkManager != null ? networkManager.GetComponent<UnityTransport>() : null;

        if (!s_inputDebugBootLogged)
        {
            s_inputDebugBootLogged = true;
            Debug.Log($"{InputDebug.Prefix} Boot Debug.isDebugBuild={Debug.isDebugBuild} Enabled={InputDebug.Enabled}");
        }

        // Keep the NetworkManager under a stable, cross-scene runtime root name.
        if (networkManager != null)
        {
            networkManager.gameObject.name = RuntimeRootName;
            Object.DontDestroyOnLoad(networkManager.gameObject);
        }

        if (networkManager == null)
        {
            GameObject runtimeRoot = GameObject.Find(RuntimeRootName);
            if (runtimeRoot == null)
                runtimeRoot = new GameObject { name = RuntimeRootName };

            Object.DontDestroyOnLoad(runtimeRoot);

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

        if (networkManager.NetworkConfig == null)
            networkManager.NetworkConfig = new NetworkConfig();

        if (networkManager.NetworkConfig.Prefabs == null)
            networkManager.NetworkConfig.Prefabs = new NetworkPrefabs();

        networkManager.NetworkConfig.NetworkTransport = transport;
        networkManager.NetworkConfig.EnableSceneManagement = true;
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

        GameObject networkObjectPrefab = Managers.Resource.Load<GameObject>(NetworkObjectPrefabPath);
        if (networkObjectPrefab == null)
        {
            Debug.LogError($"[Lobby] Failed to load runtime player prefab at {NetworkObjectPrefabPath}.");
            return null;
        }

        s_runtimePlayerPrefab = Object.Instantiate(networkObjectPrefab);
        s_runtimePlayerPrefab.name = "@NetworkObject";
        s_runtimePlayerPrefab.transform.position = Vector3.zero;
        s_runtimePlayerPrefab.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;

        NetworkObject networkObject = s_runtimePlayerPrefab.GetorAddComponent<NetworkObject>();
        EnsureGlobalObjectIdHash(networkObject, LobbyPlayerPrefabHash);

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
