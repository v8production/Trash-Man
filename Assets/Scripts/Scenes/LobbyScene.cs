using UnityEngine;
using UnityEngine.InputSystem;

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
        LogLobbyVoice("LobbyScene initialized.");
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
        if (!IsEscapePressedThisFrame())
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
        LogLobbyVoice($"Discord auth state changed. linked={Managers.Discord.IsLinked}, connecting={Managers.Discord.IsConnecting}, lastError={Managers.Discord.LastAuthError}");
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
        Managers.Input.SetMode(shouldShow ? Define.InputMode.UI : Define.InputMode.Player);
    }

    private static bool IsEscapePressedThisFrame()
    {
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
    }

    private void TryAutoConnectLobbyVoice()
    {
        LogLobbyVoice($"TryAutoConnectLobbyVoice called. linked={Managers.Discord.IsLinked}, connecting={Managers.Discord.IsConnecting}");

        if (!Managers.Discord.IsLinked)
        {
            if (Managers.Discord.IsConnecting)
            {
                LogLobbyVoice("Auto-connect skipped because Discord is currently connecting.");
                return;
            }

            string appIdText = Util.GetEnv(DiscordApplicationIdKey);
            LogLobbyVoice($"Resolved {DiscordApplicationIdKey} value. hasValue={!string.IsNullOrWhiteSpace(appIdText)}");
            if (!ulong.TryParse(appIdText, out ulong appId) || appId == 0)
            {
                Debug.LogWarning($"LobbyScene: Discord auto-connect skipped - set {DiscordApplicationIdKey} in process env or .env files.");
                return;
            }

            LogLobbyVoice($"Requesting Discord connect with appId={appId}.");
            Managers.Discord.Connect(appId, string.Empty);
            return;
        }

        string lobbyVoiceSecret = GetLobbyVoiceSecret();
        LogLobbyVoice($"Discord already linked. Ensuring lobby voice connection. secretLen={lobbyVoiceSecret.Length}");
        Managers.Discord.EnsureLobbyVoiceConnected(lobbyVoiceSecret);
    }

    private static string GetLobbyVoiceSecret()
    {
        string configuredSecret = Util.GetEnv(DiscordLobbyVoiceSecretKey);
        return string.IsNullOrWhiteSpace(configuredSecret) ? DefaultLobbyVoiceSecret : configuredSecret.Trim();
    }

    private static void LogLobbyVoice(string message)
    {
        Debug.Log($"[LobbyVoice] {message}");
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
