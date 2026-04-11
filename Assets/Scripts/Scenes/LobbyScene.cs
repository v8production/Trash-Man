using UnityEngine;

public class LobbyScene : BaseScene
{
    private const string DiscordApplicationIdKey = "DISCORD_APPLICATION_ID";
    private const string DiscordLobbyVoiceSecretKey = "DISCORD_LOBBY_VOICE_SECRET";
    private const string DefaultLobbyVoiceSecret = "trash-man-lobby-scene";

    private UI_LobbyMenu _lobbyMenu;

    protected override void Init()
    {
        base.Init();
        SceneType = Define.Scene.Lobby;
        LoadManagers();
        Managers.Input.SetMode(Define.InputMode.Player);
        EnsureLobbyMenu();

        if (Managers.Scene.ConsumeLobbyHostRequest())
            Managers.Lobby.BootstrapLocalHostLobby();

        Managers.Discord.OnAuthStateChanged -= HandleDiscordAuthStateChanged;
        Managers.Discord.OnAuthStateChanged += HandleDiscordAuthStateChanged;
        TryAutoConnectLobbyVoice();
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape))
            return;

        ToggleLobbyMenu();
    }

    private void OnDestroy()
    {
        Managers.Discord.OnAuthStateChanged -= HandleDiscordAuthStateChanged;
    }

    private static void LoadManagers()
    {
        _ = Managers.Input;
    }

    private void HandleDiscordAuthStateChanged()
    {
        TryAutoConnectLobbyVoice();
    }

    private void EnsureLobbyMenu()
    {
        if (_lobbyMenu != null)
            return;

        _lobbyMenu = Managers.UI.ShowSceneUI<UI_LobbyMenu>(nameof(UI_LobbyMenu));
        if (_lobbyMenu != null)
            _lobbyMenu.gameObject.SetActive(false);
    }

    private void ToggleLobbyMenu()
    {
        EnsureLobbyMenu();
        if (_lobbyMenu == null)
            return;

        bool shouldShow = !_lobbyMenu.gameObject.activeSelf;
        _lobbyMenu.gameObject.SetActive(shouldShow);
    }

    private void TryAutoConnectLobbyVoice()
    {
        if (!Managers.Discord.IsLinked)
        {
            if (Managers.Discord.IsConnecting)
                return;

            string appIdText = Util.GetEnv(DiscordApplicationIdKey);
            if (!ulong.TryParse(appIdText, out ulong appId) || appId == 0)
            {
                Debug.LogWarning($"LobbyScene: Discord auto-connect skipped - set {DiscordApplicationIdKey} in process env or .env files.");
                return;
            }

            Managers.Discord.Connect(appId, string.Empty);
            return;
        }

        Managers.Discord.EnsureLobbyVoiceConnected(GetLobbyVoiceSecret());
    }

    private static string GetLobbyVoiceSecret()
    {
        string configuredSecret = Util.GetEnv(DiscordLobbyVoiceSecretKey);
        return string.IsNullOrWhiteSpace(configuredSecret) ? DefaultLobbyVoiceSecret : configuredSecret.Trim();
    }


    public override void Clear()
    {
        Managers.Discord.OnAuthStateChanged -= HandleDiscordAuthStateChanged;
        Managers.Discord.EndActiveLobbyVoice();

        if (_lobbyMenu != null)
        {
            Managers.Resource.Destory(_lobbyMenu.gameObject);
            _lobbyMenu = null;
        }
    }
}
