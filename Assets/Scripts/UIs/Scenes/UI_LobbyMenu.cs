using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UI_LobbyMenu : UI_Scene
{

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
    }

    private void OnDestroy()
    {
    }

    private void OnSystemSettingsButtonClicked(PointerEventData eventData)
    {
    }

    private void OnShowCodeButtonClicked(PointerEventData eventData)
    {
    }

    private void OnInviteRoomButtonClicked(PointerEventData eventData)
    {
    }

    private void OnQuitRoomButtonClicked(PointerEventData eventData)
    {
    }
}
