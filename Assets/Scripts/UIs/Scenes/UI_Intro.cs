using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UI_Intro : UI_Scene
{
    [Header("Discord Social SDK")]
    [SerializeField] private string _discordScopes = "openid sdk.social_layer";

    private const string DiscordApplicationIdKey = "DISCORD_APPLICATION_ID";

    enum Images
    {
        Background,
        Logo,
    }

    enum Buttons
    {
        DiscordConnect,
        NewGame,
        Join,
        Quit,
    }

    enum Texts
    {
        DiscordConnect,
        NewGame,
        Join,
        Quit,
    }

    public override void Init()
    {
        base.Init();
        Bind<Image>(typeof(Images));
        Bind<Button>(typeof(Buttons));
        Bind<TextMeshProUGUI>(typeof(Texts));

        GetButton((int)Buttons.DiscordConnect).gameObject.BindEvent(OnDiscordConnectButtonClicked);
        GetButton((int)Buttons.NewGame).gameObject.BindEvent(OnNewGameButtonClicked);
        GetButton((int)Buttons.Join).gameObject.BindEvent(OnJoinButtonClicked);
        GetButton((int)Buttons.Quit).gameObject.BindEvent(OnQuitButtonClicked);

        Managers.Discord.OnAuthStateChanged -= HandleDiscordAuthStateChanged;
        Managers.Discord.OnAuthStateChanged += HandleDiscordAuthStateChanged;

        ApplyDiscordConnectState();
    }

    private void OnDestroy()
    {
        Managers.Discord.OnAuthStateChanged -= HandleDiscordAuthStateChanged;
    }

    private void OnDiscordConnectButtonClicked(PointerEventData eventData)
    {
        if (Managers.Discord.IsLinked || Managers.Discord.IsConnecting)
            return;

        string appIdText = Util.GetEnv(DiscordApplicationIdKey);
        if (!ulong.TryParse(appIdText, out ulong appId) || appId == 0)
        {
            Debug.LogError($"UI_Intro: Discord connect failed - set {DiscordApplicationIdKey} in process env or .env(.local/.dev/.prod) files.");
            return;
        }

        Managers.Discord.Connect(appId, _discordScopes);
        ApplyDiscordConnectState();
    }

    private void OnNewGameButtonClicked(PointerEventData eventData)
    {
        Managers.Scene.LoadLobbyAsHost();
    }

    private void OnJoinButtonClicked(PointerEventData eventData)
    {
        if (Object.FindAnyObjectByType<UI_EnterCode>() != null)
            return;

        Managers.UI.ShowSceneUI<UI_EnterCode>(nameof(UI_EnterCode));
    }

    private void OnQuitButtonClicked(PointerEventData eventData)
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    private void HandleDiscordAuthStateChanged()
    {
        ApplyDiscordConnectState();
    }

    private void ApplyDiscordConnectState()
    {
        Button discordButton = GetButton((int)Buttons.DiscordConnect);
        TextMeshProUGUI discordText = Get<TextMeshProUGUI>((int)Texts.DiscordConnect);

        if (discordText != null)
        {
            if (Managers.Discord.IsLinked)
                discordText.text = "Discord Linked";
            else if (Managers.Discord.IsConnecting)
                discordText.text = "Discord Connecting...";
            else if (!string.IsNullOrWhiteSpace(Managers.Discord.LastAuthError))
                discordText.text = "Discord Retry Connect";
            else
                discordText.text = "Discord Connect";
        }

        if (discordButton != null)
            discordButton.interactable = !Managers.Discord.IsLinked && !Managers.Discord.IsConnecting;
    }
}
