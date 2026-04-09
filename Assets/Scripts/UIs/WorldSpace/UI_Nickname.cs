using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UI_Nickname : UI_Base
{
    [SerializeField] private Vector3 worldOffset = Vector3.zero;
    [SerializeField] private Vector2 canvasOffset = Vector2.zero;

    private string _text;
    private Transform _target;
    private Transform _anchorTarget;
    private RectTransform _selfRect;
    private RectTransform _textRect;
    private TextMeshProUGUI _textComponent;

    enum Texts
    {
        Text,
    }

    public override void Init()
    {
        _target = transform.parent;

        transform.SetParent(Managers.UI._root.transform, false);
        Managers.UI.ShowCanvas(gameObject, false);

        _selfRect = gameObject.GetComponent<RectTransform>();
        Bind<TextMeshProUGUI>(typeof(Texts));
        _textComponent = Get<TextMeshProUGUI>((int)Texts.Text);
        _textRect = _textComponent != null ? _textComponent.rectTransform : null;
        _textComponent.text = _text;
    }

    void LateUpdate()
    {
        if (_target == null || _textRect == null)
        {
            Hide();
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            Hide();
            return;
        }

        Transform anchorTarget = _anchorTarget != null ? _anchorTarget : _target;
        Vector3 worldAnchor = GetTargetCenter(anchorTarget) + worldOffset;
        Vector3 screenPoint = cam.WorldToScreenPoint(worldAnchor);

        if (screenPoint.z <= 0f)
        {
            Hide();
            return;
        }

        RectTransform parentRect = _textRect.parent as RectTransform;
        if (parentRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPoint, null, out Vector2 localPoint))
            _textRect.anchoredPosition = localPoint + canvasOffset;
        else
            _textRect.position = screenPoint + (Vector3)canvasOffset;

        if (_textComponent != null)
            _textComponent.text = _text;
    }

    public void SetText(string text)
    {
        _text = text;
        if (_textComponent != null)
            _textComponent.text = _text;
    }

    public void SetAnchorTarget(Transform anchorTarget)
    {
        _anchorTarget = anchorTarget;
    }

    public void Hide() => gameObject.SetActive(false);
    public void Show() => gameObject.SetActive(true);

    private static Vector3 GetTargetCenter(Transform target)
    {
        if (target == null)
            return Vector3.zero;

        if (target.TryGetComponent(out IInteractGuideAnchorProvider anchorProvider))
            return anchorProvider.GetInteractGuideAnchorWorldPosition();

        Collider collider = target.GetComponent<Collider>();
        if (collider != null)
            return collider.bounds.center;

        Collider colliderInChildren = target.GetComponentInChildren<Collider>();
        if (colliderInChildren != null)
            return colliderInChildren.bounds.center;

        Renderer renderer = target.GetComponentInChildren<Renderer>();
        if (renderer != null)
            return renderer.bounds.center;

        return target.position;
    }
}

public interface IInteractGuideAnchorProvider
{
    Vector3 GetInteractGuideAnchorWorldPosition();
}
