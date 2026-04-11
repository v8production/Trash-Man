using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UI_LobbyMenu : UI_Scene
{
    private const string MaskedCodeText = "******";
    private const string HiddenCodeButtonText = "Show Code";
    private const string VisibleCodeButtonText = "Hide Code";

    enum Images
    {
        Background,
    }

    enum Buttons
    {
        SystemSettings,
        ShowCode,
        InviteRoom,
        QuitRoom,
    }

    enum Texts
    {
        SystemSettings,
        ShowCode,
        Code,
        InviteRoom,
        QuitRoom,
    }

    private bool _isCodeVisible;

    public override void Init()
    {
        base.Init();
        Bind<Image>(typeof(Images));
        Bind<Button>(typeof(Buttons));
        Bind<TextMeshProUGUI>(typeof(Texts));

        GetButton((int)Buttons.SystemSettings).gameObject.BindEvent(OnSystemSettingsButtonClicked);
        GetButton((int)Buttons.ShowCode).gameObject.BindEvent(OnShowCodeButtonClicked);
        GetButton((int)Buttons.InviteRoom).gameObject.BindEvent(OnInviteRoomButtonClicked);
        GetButton((int)Buttons.QuitRoom).gameObject.BindEvent(OnQuitRoomButtonClicked);

        ApplyJoinCodeState();
        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
    }

    private void OnEnable()
    {
        ApplyJoinCodeState();
    }

    private void OnSystemSettingsButtonClicked(PointerEventData eventData)
    {
        Managers.Chat.EnqueueMessage("System settings UI is not ready yet.", 2.5f);
    }

    private void OnShowCodeButtonClicked(PointerEventData eventData)
    {
        _isCodeVisible = !_isCodeVisible;
        ApplyJoinCodeState();
    }

    private void OnInviteRoomButtonClicked(PointerEventData eventData)
    {
        if (!Managers.Discord.IsLinked)
        {
            Managers.Chat.EnqueueMessage("Connect Discord before inviting a friend.", 2.5f);
            return;
        }

        string joinCode = Managers.Lobby.CurrentJoinCode;
        if (string.IsNullOrWhiteSpace(joinCode))
        {
            Managers.Chat.EnqueueMessage("Lobby code is not ready yet.", 2.5f);
            return;
        }

        Managers.Discord.RequestLobbyInvite(joinCode);
        Managers.Chat.EnqueueMessage($"Invite sent. Code: {joinCode}", 2.5f);
    }

    private void OnQuitRoomButtonClicked(PointerEventData eventData)
    {
        Managers.Lobby.QuitCurrentRoom();
        Managers.Scene.LoadScene(Define.Scene.Intro);
    }

    private void ApplyJoinCodeState()
    {
        TextMeshProUGUI showCodeText = Get<TextMeshProUGUI>((int)Texts.ShowCode);
        TextMeshProUGUI codeText = Get<TextMeshProUGUI>((int)Texts.Code);

        string joinCode = Managers.Lobby.CurrentJoinCode;
        bool canRevealCode = !string.IsNullOrWhiteSpace(joinCode);

        if (showCodeText != null)
            showCodeText.text = _isCodeVisible && canRevealCode ? VisibleCodeButtonText : HiddenCodeButtonText;

        if (codeText != null)
            codeText.text = _isCodeVisible && canRevealCode ? joinCode : MaskedCodeText;
    }
}
