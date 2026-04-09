public class LobbyScene : BaseScene
{
    protected override void Init()
    {
        base.Init();
        SceneType = Define.Scene.Lobby;
        LoadManagers();
        Managers.Input.SetMode(Define.InputMode.Player);

        if (Managers.Scene.ConsumeLobbyHostRequest())
            Managers.Lobby.BootstrapLocalHostLobby();
    }

    private static void LoadManagers()
    {
        _ = Managers.Input;
    }


    public override void Clear()
    {
    }
}
