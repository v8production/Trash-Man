using UnityEngine;
using UnityEngine.UI;

public class UI_DiscordLobby : UI_Scene
{
    private Text _localNicknameText;
    private InputField _friendInput;
    private Button _inviteButton;

    public override void Init()
    {
        base.Init();
        BuildFallbackUIIfNeeded();

        _localNicknameText.text = $"Discord: {Managers.Discord.LocalDisplayName}";
        _inviteButton.onClick.RemoveListener(OnClickInvite);
        _inviteButton.onClick.AddListener(OnClickInvite);

        Managers.Discord.OnLocalDisplayNameChanged -= HandleLocalNicknameChanged;
        Managers.Discord.OnLocalDisplayNameChanged += HandleLocalNicknameChanged;
    }

    private void OnDestroy()
    {
        Managers.Discord.OnLocalDisplayNameChanged -= HandleLocalNicknameChanged;
    }

    private void HandleLocalNicknameChanged(string nickname)
    {
        if (_localNicknameText != null)
            _localNicknameText.text = $"Discord: {nickname}";
    }

    private void OnClickInvite()
    {
        string friendName = _friendInput != null ? _friendInput.text : string.Empty;
        Managers.Discord.RequestFriendInvite(friendName);

        if (_friendInput != null)
            _friendInput.text = string.Empty;
    }

    private void BuildFallbackUIIfNeeded()
    {
        _localNicknameText = Util.FindChild<Text>(gameObject, "Text_LocalNickname", true);
        _friendInput = Util.FindChild<InputField>(gameObject, "InputField_Friend", true);
        _inviteButton = Util.FindChild<Button>(gameObject, "Button_Invite", true);

        if (_localNicknameText != null && _friendInput != null && _inviteButton != null)
            return;

        EnsureCanvasRoot();

        GameObject panel = CreateUIObject("Panel_DiscordLobby", transform);
        Image panelImage = panel.GetorAddComponent<Image>();
        panelImage.color = new Color(0f, 0f, 0f, 0.45f);

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = new Vector2(24f, -24f);
        panelRect.sizeDelta = new Vector2(360f, 170f);

        _localNicknameText = CreateText("Text_LocalNickname", panel.transform, new Vector2(16f, -16f), new Vector2(328f, 28f), "Discord: Player");
        CreateText("Text_InviteHeader", panel.transform, new Vector2(16f, -56f), new Vector2(328f, 24f), "Friend Invite");

        _friendInput = CreateInputField(panel.transform, new Vector2(16f, -86f), new Vector2(220f, 34f));
        _inviteButton = CreateButton(panel.transform, new Vector2(246f, -86f), new Vector2(98f, 34f), "Invite");
    }

    private void EnsureCanvasRoot()
    {
        RectTransform rootRect = gameObject.GetorAddComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;
    }

    private static GameObject CreateUIObject(string name, Transform parent)
    {
        GameObject go = new(name);
        go.transform.SetParent(parent, false);
        go.GetorAddComponent<RectTransform>();
        return go;
    }

    private static Text CreateText(string name, Transform parent, Vector2 anchoredPosition, Vector2 size, string content)
    {
        GameObject textObject = CreateUIObject(name, parent);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        Text text = textObject.GetorAddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 18;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = Color.white;
        text.text = content;
        return text;
    }

    private static InputField CreateInputField(Transform parent, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject inputObject = CreateUIObject("InputField_Friend", parent);
        Image image = inputObject.GetorAddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.95f);

        RectTransform rect = inputObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        InputField inputField = inputObject.GetorAddComponent<InputField>();

        Text placeholder = CreateText("Placeholder", inputObject.transform, new Vector2(8f, -5f), new Vector2(size.x - 16f, size.y - 10f), "Friend nickname");
        placeholder.color = new Color(0.35f, 0.35f, 0.35f, 0.85f);

        Text text = CreateText("Text", inputObject.transform, new Vector2(8f, -5f), new Vector2(size.x - 16f, size.y - 10f), string.Empty);
        text.color = Color.black;

        inputField.placeholder = placeholder;
        inputField.textComponent = text;
        inputField.text = string.Empty;

        return inputField;
    }

    private static Button CreateButton(Transform parent, Vector2 anchoredPosition, Vector2 size, string label)
    {
        GameObject buttonObject = CreateUIObject("Button_Invite", parent);
        Image image = buttonObject.GetorAddComponent<Image>();
        image.color = new Color(0.26f, 0.52f, 0.87f, 1f);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        Button button = buttonObject.GetorAddComponent<Button>();

        Text buttonText = CreateText("Text", buttonObject.transform, Vector2.zero, size, label);
        buttonText.alignment = TextAnchor.MiddleCenter;

        return button;
    }
}
