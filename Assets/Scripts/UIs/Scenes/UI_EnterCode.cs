using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class UI_EnterCode : UI_Scene
{
    private const int RequiredCodeLength = 6;

    enum Images
    {
        Background,
    }

    enum Buttons
    {
        Enter,
    }

    enum InputFields
    {
        EnterCode,
    }

    private TMP_InputField _enterCodeInput;

    public override void Init()
    {
        base.Init();
        Managers.UI.ShowCanvas(gameObject, true);
        Bind<Image>(typeof(Images));
        Bind<Button>(typeof(Buttons));
        Bind<TMP_InputField>(typeof(InputFields));

        GetButton((int)Buttons.Enter).gameObject.BindEvent(OnEnterButtonClicked);
        _enterCodeInput = Get<TMP_InputField>((int)InputFields.EnterCode);

        if (_enterCodeInput != null)
            _enterCodeInput.characterLimit = RequiredCodeLength;
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            Close();
    }

    private void OnEnterButtonClicked(PointerEventData eventData)
    {
        string rawInput = _enterCodeInput != null ? _enterCodeInput.text : string.Empty;
        string normalizedCode = Util.NormalizeLobbyJoinCode(rawInput);
        if (string.IsNullOrWhiteSpace(normalizedCode) || normalizedCode.Length != RequiredCodeLength)
        {
            Managers.Toast.EnqueueMessage("Please enter a valid 6-digit code.", 2f);
            return;
        }

        Close();

        UI_Intro intro = Object.FindAnyObjectByType<UI_Intro>();
        if (intro != null)
        {
            intro.StartJoinTransition(normalizedCode);
            return;
        }

        Managers.Scene.LoadLobbyByCode(normalizedCode);
    }

    private void Close()
    {
        Destroy(gameObject);
    }
}
