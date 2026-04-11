using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class UI_EnterCode : MonoBehaviour
{
    private const int JoinCodeLength = 6;

    private Action<string> _onSubmit;
    private TMP_InputField _inputField;

    public static UI_EnterCode Show(Action<string> onSubmit)
    {
        GameObject rootObject = new("UI_EnterCode");
        rootObject.transform.SetParent(Managers.UI._root.transform, false);

        UI_EnterCode popup = rootObject.AddComponent<UI_EnterCode>();
        popup.Build(onSubmit);
        return popup;
    }

    private void Build(Action<string> onSubmit)
    {
        _onSubmit = onSubmit;

        Managers.UI.ShowCanvas(gameObject, 2000);

        Image backdrop = gameObject.AddComponent<Image>();
        backdrop.color = new Color(0f, 0f, 0f, 0.65f);

        RectTransform rootRect = GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        GameObject panel = CreateChild("Panel", transform);
        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panel.AddComponent<Image>().color = new Color(0.14f, 0.16f, 0.2f, 0.98f);
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(460f, 220f);

        TMP_Text title = CreateLabel(panelRect, "Enter Lobby Code", 32f, new Vector2(0f, 70f), 34f);
        title.alignment = TextAlignmentOptions.Center;

        _inputField = CreateInput(panelRect);

        Button joinButton = CreateButton(panelRect, "Join", new Vector2(-80f, -68f), new Color(0.21f, 0.52f, 0.29f));
        joinButton.onClick.AddListener(Submit);

        Button cancelButton = CreateButton(panelRect, "Cancel", new Vector2(80f, -68f), new Color(0.45f, 0.22f, 0.22f));
        cancelButton.onClick.AddListener(Close);
    }

    private static TMP_InputField CreateInput(RectTransform parent)
    {
        GameObject inputObject = CreateChild("CodeInput", parent);
        RectTransform inputRect = inputObject.AddComponent<RectTransform>();
        inputRect.anchorMin = new Vector2(0.5f, 0.5f);
        inputRect.anchorMax = new Vector2(0.5f, 0.5f);
        inputRect.pivot = new Vector2(0.5f, 0.5f);
        inputRect.sizeDelta = new Vector2(320f, 56f);
        inputRect.anchoredPosition = new Vector2(0f, 8f);
        inputObject.AddComponent<Image>().color = new Color(0.09f, 0.11f, 0.14f, 1f);

        GameObject textObject = CreateChild("Text", inputRect);
        TMP_Text text = textObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = 30f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.text = string.Empty;
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0f, 0f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.offsetMin = new Vector2(12f, 6f);
        textRect.offsetMax = new Vector2(-12f, -6f);

        GameObject placeholderObject = CreateChild("Placeholder", inputRect);
        TMP_Text placeholder = placeholderObject.AddComponent<TextMeshProUGUI>();
        placeholder.fontSize = 24f;
        placeholder.alignment = TextAlignmentOptions.Center;
        placeholder.color = new Color(1f, 1f, 1f, 0.4f);
        placeholder.text = "6-digit code";
        RectTransform placeholderRect = placeholderObject.GetComponent<RectTransform>();
        placeholderRect.anchorMin = new Vector2(0f, 0f);
        placeholderRect.anchorMax = new Vector2(1f, 1f);
        placeholderRect.offsetMin = new Vector2(12f, 6f);
        placeholderRect.offsetMax = new Vector2(-12f, -6f);

        TMP_InputField field = inputObject.AddComponent<TMP_InputField>();
        field.textComponent = (TextMeshProUGUI)text;
        field.placeholder = placeholder;
        field.characterLimit = JoinCodeLength;
        field.contentType = TMP_InputField.ContentType.Alphanumeric;
        field.lineType = TMP_InputField.LineType.SingleLine;
        return field;
    }

    private static Button CreateButton(RectTransform parent, string label, Vector2 anchoredPosition, Color color)
    {
        GameObject buttonObject = CreateChild(label + "Button", parent);
        RectTransform buttonRect = buttonObject.AddComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.sizeDelta = new Vector2(140f, 44f);
        buttonRect.anchoredPosition = anchoredPosition;

        Image image = buttonObject.AddComponent<Image>();
        image.color = color;
        Button button = buttonObject.AddComponent<Button>();

        TMP_Text text = CreateLabel(buttonRect, label, 22f, Vector2.zero, 40f);
        text.alignment = TextAlignmentOptions.Center;
        return button;
    }

    private static TMP_Text CreateLabel(RectTransform parent, string content, float fontSize, Vector2 anchoredPosition, float height)
    {
        GameObject labelObject = CreateChild("Label_" + content.Replace(" ", string.Empty), parent);
        RectTransform labelRect = labelObject.AddComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.sizeDelta = new Vector2(420f, height);
        labelRect.anchoredPosition = anchoredPosition;

        TMP_Text label = labelObject.AddComponent<TextMeshProUGUI>();
        label.text = content;
        label.fontSize = fontSize;
        label.color = Color.white;
        return label;
    }

    private static GameObject CreateChild(string name, Transform parent)
    {
        GameObject go = new(name);
        go.transform.SetParent(parent, false);
        return go;
    }

    private void Submit()
    {
        string normalizedCode = LobbySessionManager.NormalizeJoinCode(_inputField != null ? _inputField.text : string.Empty);
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            Managers.Chat.EnqueueMessage("Enter a valid 6-digit lobby code.", 2f);
            return;
        }

        _onSubmit?.Invoke(normalizedCode);
        Close();
    }

    private void Close()
    {
        Destroy(gameObject);
    }
}
