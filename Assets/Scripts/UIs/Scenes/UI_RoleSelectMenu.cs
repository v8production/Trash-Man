using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UI_RoleSelectMenu : UI_Scene
{
    private const int CanvasOrder = 10;

    private enum GameObjects
    {
        Background,
    }

    private enum Buttons
    {
        Body,
        LeftArm,
        RightArm,
        LeftLeg,
        RightLeg,
    }

    public event Action<Define.TitanRole> RoleSelected;
    public event Action Closed;

    public override void Init()
    {
        base.Init();
        Managers.UI.ShowCanvas(gameObject, CanvasOrder);
        Bind<GameObject>(typeof(GameObjects));
        Bind<Button>(typeof(Buttons));

        GetObject((int)GameObjects.Background).BindEvent(OnBackgroundClicked);
        GetButton((int)Buttons.Body).gameObject.BindEvent(_ => NotifyRoleSelected(Define.TitanRole.Body));
        GetButton((int)Buttons.LeftArm).gameObject.BindEvent(_ => NotifyRoleSelected(Define.TitanRole.LeftArm));
        GetButton((int)Buttons.RightArm).gameObject.BindEvent(_ => NotifyRoleSelected(Define.TitanRole.RightArm));
        GetButton((int)Buttons.LeftLeg).gameObject.BindEvent(_ => NotifyRoleSelected(Define.TitanRole.LeftLeg));
        GetButton((int)Buttons.RightLeg).gameObject.BindEvent(_ => NotifyRoleSelected(Define.TitanRole.RightLeg));

        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        RoleSelected = null;
        Closed = null;
    }

    private void OnBackgroundClicked(PointerEventData eventData)
    {
        Closed?.Invoke();
    }

    private void NotifyRoleSelected(Define.TitanRole role)
    {
        RoleSelected?.Invoke(role);
    }
}
