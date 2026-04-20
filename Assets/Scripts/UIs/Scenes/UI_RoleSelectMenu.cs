using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UI_RoleSelectMenu : UI_Scene
{
    private const int CanvasOrder = 10;
    private const float NicknameRefreshIntervalSeconds = 0.25f;

    private bool _isInitialized;
    private float _nextNicknameRefreshTime;

    private enum GameObjects
    {
        Background,
    }

    private enum Buttons
    {
        Cancel,
        Body,
        LeftArm,
        RightArm,
        LeftLeg,
        RightLeg,
    }

    enum Texts
    {
        BodyNickname,
        LeftArmNickname,
        RightArmNickname,
        LeftLegNickname,
        RightLegNickname,
    }

    public event Action<Define.TitanRole> RoleSelected;
    public event Action Closed;

    public override void Init()
    {
        if (_isInitialized)
            return;

        base.Init();
        Managers.UI.ShowCanvas(gameObject, CanvasOrder);
        Bind<GameObject>(typeof(GameObjects));
        Bind<Button>(typeof(Buttons));
        Bind<TextMeshProUGUI>(typeof(Texts));

        GetObject((int)GameObjects.Background).BindEvent(OnCancelClicked);
        GetButton((int)Buttons.Cancel).gameObject.BindEvent(OnCancelClicked);
        GetButton((int)Buttons.Body).gameObject.BindEvent(_ => NotifyRoleSelected(Define.TitanRole.Body));
        GetButton((int)Buttons.LeftArm).gameObject.BindEvent(_ => NotifyRoleSelected(Define.TitanRole.LeftArm));
        GetButton((int)Buttons.RightArm).gameObject.BindEvent(_ => NotifyRoleSelected(Define.TitanRole.RightArm));
        GetButton((int)Buttons.LeftLeg).gameObject.BindEvent(_ => NotifyRoleSelected(Define.TitanRole.LeftLeg));
        GetButton((int)Buttons.RightLeg).gameObject.BindEvent(_ => NotifyRoleSelected(Define.TitanRole.RightLeg));

        gameObject.SetActive(false);
        _isInitialized = true;
    }

    private void OnEnable()
    {
        _nextNicknameRefreshTime = 0f;
        RefreshRoleNicknames();
    }

    private void Update()
    {
        if (!_isInitialized || !gameObject.activeInHierarchy)
            return;

        if (Time.unscaledTime < _nextNicknameRefreshTime)
            return;

        _nextNicknameRefreshTime = Time.unscaledTime + NicknameRefreshIntervalSeconds;
        RefreshRoleNicknames();
    }

    private void OnDestroy()
    {
        RoleSelected = null;
        Closed = null;
    }

    private void OnCancelClicked(PointerEventData eventData)
    {
        Closed?.Invoke();
    }

    private void NotifyRoleSelected(Define.TitanRole role)
    {
        RoleSelected?.Invoke(role);
    }

    public void RefreshRoleNicknames()
    {
        SetNicknameText(Texts.BodyNickname, string.Empty);
        SetNicknameText(Texts.LeftArmNickname, string.Empty);
        SetNicknameText(Texts.RightArmNickname, string.Empty);
        SetNicknameText(Texts.LeftLegNickname, string.Empty);
        SetNicknameText(Texts.RightLegNickname, string.Empty);

        LobbyNetworkPlayer[] players = FindObjectsByType<LobbyNetworkPlayer>();
        if (players == null || players.Length == 0)
            return;

        Dictionary<Define.TitanRole, List<string>> namesByRole = new();

        for (int i = 0; i < players.Length; i++)
        {
            LobbyNetworkPlayer player = players[i];
            if (player == null || !player.HasSelectedTitanRole)
                continue;

            Define.TitanRole role = (Define.TitanRole)player.SelectedTitanRoleValue;
            string displayName = player.DisplayName;
            if (string.IsNullOrWhiteSpace(displayName))
                continue;

            if (!namesByRole.TryGetValue(role, out List<string> list))
            {
                list = new List<string>();
                namesByRole[role] = list;
            }

            list.Add(displayName);
        }

        ApplyRoleNicknameText(namesByRole, Define.TitanRole.Body, Texts.BodyNickname);
        ApplyRoleNicknameText(namesByRole, Define.TitanRole.LeftArm, Texts.LeftArmNickname);
        ApplyRoleNicknameText(namesByRole, Define.TitanRole.RightArm, Texts.RightArmNickname);
        ApplyRoleNicknameText(namesByRole, Define.TitanRole.LeftLeg, Texts.LeftLegNickname);
        ApplyRoleNicknameText(namesByRole, Define.TitanRole.RightLeg, Texts.RightLegNickname);
    }

    private void ApplyRoleNicknameText(Dictionary<Define.TitanRole, List<string>> namesByRole, Define.TitanRole role, Texts targetText)
    {
        if (!namesByRole.TryGetValue(role, out List<string> names) || names == null || names.Count == 0)
            return;

        names.Sort(StringComparer.OrdinalIgnoreCase);
        SetNicknameText(targetText, string.Join("\n", names));
    }

    private void SetNicknameText(Texts textId, string value)
    {
        TextMeshProUGUI text = GetText((int)textId);
        if (text == null)
            return;

        text.text = value ?? string.Empty;
    }
}
