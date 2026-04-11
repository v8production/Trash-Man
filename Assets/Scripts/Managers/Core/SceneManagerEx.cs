using UnityEngine;
using UnityEngine.SceneManagement;
using System;

public class SceneManagerEx
{
    private bool _enterLobbyAsHost;
    private string _pendingLobbyJoinCode = string.Empty;

    public void Init()
    {
        Application.deepLinkActivated -= HandleDeepLinkActivated;
        Application.deepLinkActivated += HandleDeepLinkActivated;

        if (!string.IsNullOrWhiteSpace(Application.absoluteURL))
            HandleDeepLinkActivated(Application.absoluteURL);
    }

    public BaseScene CurrentScene
    {
        get { return UnityEngine.Object.FindAnyObjectByType<BaseScene>(); }
    }

    public void LoadLobbyAsHost()
    {
        _enterLobbyAsHost = true;
        _pendingLobbyJoinCode = string.Empty;
        LoadScene(Define.Scene.Lobby);
    }

    public void LoadLobbyByCode(string joinCode)
    {
        _enterLobbyAsHost = false;
        _pendingLobbyJoinCode = LobbySessionManager.NormalizeJoinCode(joinCode);
        LoadScene(Define.Scene.Lobby);
    }

    public bool ConsumeLobbyHostRequest()
    {
        bool requested = _enterLobbyAsHost;
        _enterLobbyAsHost = false;
        return requested;
    }

    public bool ConsumeLobbyJoinCodeRequest(out string joinCode)
    {
        joinCode = _pendingLobbyJoinCode;
        _pendingLobbyJoinCode = string.Empty;
        return !string.IsNullOrWhiteSpace(joinCode);
    }

    public void LoadScene(Define.Scene name)
    {
        Managers.Clear();
        SceneManager.LoadScene(Util.GetEnumName(name));
    }

    public void Clear()
    {
        CurrentScene.Clear();
    }

    private void HandleDeepLinkActivated(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        if (!TryExtractJoinCode(url, out string joinCode))
            return;

        LoadLobbyByCode(joinCode);
    }

    private static bool TryExtractJoinCode(string url, out string joinCode)
    {
        joinCode = string.Empty;

        int markerIndex = url.IndexOf("code=", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
            return false;

        int start = markerIndex + 5;
        int end = url.IndexOf('&', start);
        string raw = end >= 0 ? url.Substring(start, end - start) : url.Substring(start);
        string normalized = LobbySessionManager.NormalizeJoinCode(Uri.UnescapeDataString(raw));
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        joinCode = normalized;
        return true;
    }
}
