public class LobbyScene : BaseScene
{
    protected override void Init()
    {
        base.Init();
        SceneType = Define.Scene.Lobby;
        LoadManagers();
    }

    private static void LoadManagers()
    {
        _ = Managers.Input;
        _ = Managers.Discord;
    }


    public override void Clear()
    {
    }
}
