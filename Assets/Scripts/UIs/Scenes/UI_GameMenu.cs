using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UI_GameMenu : UI_Scene
{

    enum Images
    {
        Background,
    }

    enum Buttons
    {
        SystemSettings,
        TempButton,
        TempButton2,
        QuitGame,
    }

    enum Texts
    {
        SystemSettings,
        TempButton,
        TempButton2,
        QuitGame,
    }

    public override void Init()
    {
        base.Init();
        Bind<Image>(typeof(Images));
        Bind<Button>(typeof(Buttons));
        Bind<TextMeshProUGUI>(typeof(Texts));

        GetButton((int)Buttons.SystemSettings).gameObject.BindEvent(OnSystemSettingsButtonClicked);
        GetButton((int)Buttons.TempButton).gameObject.BindEvent(OnTempButtonClicked);
        GetButton((int)Buttons.TempButton2).gameObject.BindEvent(OnTempButtonClicked);
        GetButton((int)Buttons.QuitGame).gameObject.BindEvent(OnQuitGameButtonClicked);
    }

    private void OnDestroy()
    {
    }

    private void OnEnable()
    {
    }

    private void OnSystemSettingsButtonClicked(PointerEventData eventData)
    {
        Managers.Toast.EnqueueMessage("System settings UI is not ready yet.", 2.5f);
    }

    private void OnTempButtonClicked(PointerEventData eventData)
    {
    }

    private void OnQuitGameButtonClicked(PointerEventData eventData)
    {
        Managers.LobbySession.QuitCurrentRoom();
        Managers.Scene.LoadScene(Define.Scene.Intro);
    }
}
