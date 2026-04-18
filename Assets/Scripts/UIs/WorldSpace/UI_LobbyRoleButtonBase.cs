using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public abstract class UI_LobbyRoleButtonBase : UI_Base
{
    private enum Texts
    {
        Nickname,
    }

    private GameObject _buttonRoot;
    private Button _button;
    private TextMeshProUGUI _label;
    private bool _isBound;
    private bool _isInitialized;

    protected abstract Type ButtonElementsType { get; }
    public abstract Define.TitanRole Role { get; }

    public event Action<Define.TitanRole> RoleButtonClicked;

    public override void Init()
    {
        if (_isInitialized)
            return;

        Type buttonElementsType = ButtonElementsType;
        if (buttonElementsType != null)
        {
            Bind<GameObject>(buttonElementsType);
            Bind<Button>(buttonElementsType);
            _buttonRoot = GetObject(0);
            _button = GetButton(0);
        }

        Bind<TextMeshProUGUI>(typeof(Texts));
        _label = GetText((int)Texts.Nickname);

        if (_buttonRoot == null)
            _buttonRoot = gameObject;

        if (_button == null)
            _button = _buttonRoot.GetComponentInChildren<Button>(true);

        if (_label == null)
            _label = GetComponentInChildren<TextMeshProUGUI>(true);

        BindButtonIfNeeded();
        _isInitialized = true;
    }

    private void OnEnable()
    {
        if (!_isInitialized)
            Init();

        BindButtonIfNeeded();
    }

    private void OnDisable()
    {
        UnbindButton();
    }

    private void OnDestroy()
    {
        UnbindButton();
        RoleButtonClicked = null;
    }

    public void SetLabel(string label)
    {
        if (!_isInitialized)
            Init();

        if (_label != null && !string.IsNullOrWhiteSpace(label))
            _label.text = label;
    }

    private void BindButtonIfNeeded()
    {
        if (_isBound || _button == null)
            return;

        _button.onClick.AddListener(HandleButtonClicked);
        _isBound = true;
    }

    private void UnbindButton()
    {
        if (!_isBound || _button == null)
            return;

        _button.onClick.RemoveListener(HandleButtonClicked);
        _isBound = false;
    }

    private void HandleButtonClicked()
    {
        RoleButtonClicked?.Invoke(Role);
    }
}
