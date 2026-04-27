using Steamworks;
using UnityEngine;

public class SteamManager
{
    private const uint AppId = 480;
    private bool _initialized;

    public bool IsInitialized => _initialized;
    public string LastInitError { get; private set; } = string.Empty;
    public CSteamID LocalSteamId => _initialized ? SteamUser.GetSteamID() : CSteamID.Nil;

    public void Init()
    {
        if (_initialized)
            return;

        LastInitError = string.Empty;

        try
        {
            if (SteamAPI.RestartAppIfNecessary((AppId_t)AppId))
            {
                LastInitError = $"Steam requested app restart. appId={AppId}";
                Debug.LogWarning($"[Steam] {LastInitError}");
                return;
            }

            string errMsg;
            ESteamAPIInitResult result = SteamAPI.InitEx(out errMsg);

            if (result != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
            {
                LastInitError = $"SteamAPI.InitEx failed. result={result}, message={errMsg}";
                Debug.LogWarning($"[Steam] {LastInitError}");
                return;
            }

            SteamNetworkingUtils.InitRelayNetworkAccess();

            _initialized = true;
            Debug.Log($"[Steam] Initialized. LocalSteamId={LocalSteamId.m_SteamID}");
        }
        catch (System.Exception e)
        {
            LastInitError = e.ToString();
            Debug.LogError($"[Steam] Init failed: {e}");
            _initialized = false;
        }
    }

    public void OnUpdate()
    {
        if (!_initialized)
            return;

        SteamAPI.RunCallbacks();
    }

    public void Clear()
    {
        if (!_initialized)
            return;

        SteamAPI.Shutdown();
        _initialized = false;
    }
}