using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneManagerEx
{
    private bool _enterLobbyAsHost;

    public BaseScene CurrentScene
    {
        get { return Object.FindAnyObjectByType<BaseScene>(); }
    }

    public void LoadLobbyAsHost()
    {
        _enterLobbyAsHost = true;
        LoadScene(Define.Scene.Lobby);
    }

    public bool ConsumeLobbyHostRequest()
    {
        bool requested = _enterLobbyAsHost;
        _enterLobbyAsHost = false;
        return requested;
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
}
