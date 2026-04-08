using UnityEngine;
using UnityEngine.UI;

public class UI_Nickname : UI_Base
{
    private Text _label;

    public override void Init()
    {
        _label = Util.FindChild<Text>(gameObject, "Label", true);
        if (_label == null)
            _label = CreateFallbackLabel();

        SetNickname("Player");
    }

    public void SetNickname(string nickname)
    {
        if (_label == null)
            return;

        _label.text = string.IsNullOrWhiteSpace(nickname) ? "Player" : nickname;
    }

    private Text CreateFallbackLabel()
    {
        GameObject textObject = new("Label");
        textObject.transform.SetParent(transform, false);

        RectTransform rectTransform = textObject.GetorAddComponent<RectTransform>();
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;

        Text text = textObject.GetorAddComponent<Text>();
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 20;
        return text;
    }
}
