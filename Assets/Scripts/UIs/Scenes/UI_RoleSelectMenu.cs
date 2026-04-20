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
        LobbyNetworkPlayer[] players = FindObjectsByType<LobbyNetworkPlayer>();
        Dictionary<Define.TitanRole, List<string>> namesByRole = new();

        if (players != null && players.Length > 0)
        {
            for (int i = 0; i < players.Length; i++)
            {
                LobbyNetworkPlayer player = players[i];
                if (player == null)
                    continue;

                string displayName = player.DisplayName;
                if (string.IsNullOrWhiteSpace(displayName))
                    continue;

                int roleMask = 0;
                // Prefer the lobby registry because it is updated immediately for the local player
                // (LobbyScene.TryToggleLocalRole -> RegisterUserPartSelection), while the network variable
                // update can arrive slightly later.
                if (player.TryGetLobbyUserId(out string lobbyUserId)
                    && LobbyScene.TryGetRegisteredUserSelectedRoleMask(lobbyUserId, out int registeredMask))
                {
                    roleMask = registeredMask;
                }
                else
                {
                    roleMask = player.SelectedTitanRoleMaskValue;
                }

                if (roleMask == 0)
                    continue;

                int selectedRoleCount = CountBits(roleMask);
                string formattedName = selectedRoleCount > 1
                    ? $"{displayName} ({selectedRoleCount}개 역할)"
                    : displayName;

                AddRoleNameIfSelected(namesByRole, Define.TitanRole.Body, roleMask, formattedName);
                AddRoleNameIfSelected(namesByRole, Define.TitanRole.LeftArm, roleMask, formattedName);
                AddRoleNameIfSelected(namesByRole, Define.TitanRole.RightArm, roleMask, formattedName);
                AddRoleNameIfSelected(namesByRole, Define.TitanRole.LeftLeg, roleMask, formattedName);
                AddRoleNameIfSelected(namesByRole, Define.TitanRole.RightLeg, roleMask, formattedName);
            }
        }

        ApplyRoleNicknameText(namesByRole, Define.TitanRole.Body, Texts.BodyNickname);
        ApplyRoleNicknameText(namesByRole, Define.TitanRole.LeftArm, Texts.LeftArmNickname);
        ApplyRoleNicknameText(namesByRole, Define.TitanRole.RightArm, Texts.RightArmNickname);
        ApplyRoleNicknameText(namesByRole, Define.TitanRole.LeftLeg, Texts.LeftLegNickname);
        ApplyRoleNicknameText(namesByRole, Define.TitanRole.RightLeg, Texts.RightLegNickname);
    }

    private void ApplyRoleNicknameText(Dictionary<Define.TitanRole, List<string>> namesByRole, Define.TitanRole role, Texts targetText)
    {
        string roleLabel = GetRoleLabel(role);

        if (!namesByRole.TryGetValue(role, out List<string> names) || names == null || names.Count == 0)
        {
            SetNicknameText(targetText, $"{roleLabel} - ");
            return;
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        bool hasDuplicateOwners = names.Count > 1;
        string header = hasDuplicateOwners ? $"{roleLabel} [중복]" : roleLabel;
        SetNicknameText(targetText, $"{header} -\n{string.Join("\n", names)}");
    }

    private static void AddRoleNameIfSelected(Dictionary<Define.TitanRole, List<string>> namesByRole, Define.TitanRole role, int roleMask, string displayName)
    {
        int roleValue = (int)role;
        int bit = 1 << (roleValue - (int)Define.TitanRole.Body);
        if ((roleMask & bit) == 0)
            return;

        if (!namesByRole.TryGetValue(role, out List<string> list))
        {
            list = new List<string>();
            namesByRole[role] = list;
        }

        list.Add(displayName);
    }

    private static int CountBits(int value)
    {
        int count = 0;
        int v = value;
        while (v != 0)
        {
            v &= v - 1;
            count++;
        }

        return count;
    }

    private static string GetRoleLabel(Define.TitanRole role)
    {
        return role switch
        {
            Define.TitanRole.Body => "Center",
            Define.TitanRole.LeftArm => "Left Arm",
            Define.TitanRole.RightArm => "Right Arm",
            Define.TitanRole.LeftLeg => "Left Leg",
            Define.TitanRole.RightLeg => "Right Leg",
            _ => "Unknown",
        };
    }

    private void SetNicknameText(Texts textId, string value)
    {
        TextMeshProUGUI text = GetText((int)textId);
        if (text == null)
            return;

        text.text = value ?? string.Empty;
    }
}
