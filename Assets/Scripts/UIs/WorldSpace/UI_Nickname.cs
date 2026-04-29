using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UI_Nickname : UI_Base
{

    enum Texts
    {
        Nickname,
    }

    enum Images
    {
        Speaker,
    }

    private const float DefaultSpeakerWidth = 28f;
    private const float SpeakerGap = 6f;
    private const float NicknameHorizontalPadding = 12f;

    private string _text;
    private RectTransform _textRect;
    private TextMeshProUGUI _textComponent;
    private RectTransform _speakerRect;
    private Image _speakerImage;
    private bool _isVoiceChatActive;

    public override void Init()
    {
        Bind<TextMeshProUGUI>(typeof(Texts));
        Bind<Image>(typeof(Images));
        _textComponent = GetText((int)Texts.Nickname);
        _textRect = _textComponent != null ? _textComponent.rectTransform : null;
        _speakerImage = GetImage((int)Images.Speaker);
        _speakerRect = _speakerImage != null ? _speakerImage.rectTransform : null;

        if (_textComponent != null)
        {
            _textComponent.textWrappingMode = TextWrappingModes.NoWrap;
            _textComponent.overflowMode = TextOverflowModes.Overflow;
            _textComponent.text = _text;
        }

        ApplySpeakerState();
        UpdateNicknameWidth();
        UpdateSpeakerPosition();
    }

    void Update()
    {
        Transform parent = transform.parent;
        transform.position = parent.position + Vector3.up * parent.GetComponent<CharacterController>().bounds.size.y;
        transform.rotation = Camera.main.transform.rotation;
        ApplySpeakerState();
    }

    public void SetText(string text)
    {
        _text = text;
        if (_textComponent != null)
        {
            _textComponent.text = _text;
            UpdateNicknameWidth();
            UpdateSpeakerPosition();
        }
    }

    public void SetActive(bool isVoiceChatActive)
    {
        _isVoiceChatActive = isVoiceChatActive;
        UpdateSpeakerPosition();
    }

    public void SetVoiceChatActive(bool isActive)
    {
        SetActive(isActive);
    }

    public void Hide() => gameObject.SetActive(false);
    public void Show() => gameObject.SetActive(true);

    private void ApplySpeakerState()
    {
        if (_speakerImage == null)
            return;

        _speakerImage.gameObject.SetActive(_isVoiceChatActive);
    }

    private void UpdateSpeakerPosition()
    {
        if (_speakerRect == null || _textRect == null)
            return;

        if (_textComponent == null)
            return;

        _textComponent.ForceMeshUpdate();
        float preferredWidth = Mathf.Max(_textComponent.preferredWidth, _textRect.rect.width);
        float speakerWidth = _speakerRect.rect.width > 0f ? _speakerRect.rect.width : _speakerRect.sizeDelta.x;
        if (speakerWidth <= 0f)
            speakerWidth = DefaultSpeakerWidth;

        float speakerX = _textRect.anchoredPosition.x - (preferredWidth * 0.5f) - SpeakerGap - (speakerWidth * 0.5f);
        _speakerRect.anchoredPosition = new Vector2(speakerX, _textRect.anchoredPosition.y);
    }

    private void UpdateNicknameWidth()
    {
        if (_textRect == null || _textComponent == null)
            return;

        _textComponent.ForceMeshUpdate();
        float preferredWidth = Mathf.Max(0f, _textComponent.preferredWidth);
        Vector2 size = _textRect.sizeDelta;
        size.x = preferredWidth + NicknameHorizontalPadding;
        _textRect.sizeDelta = size;
    }
}
