using System;

public sealed class UI_LeftArmButton : UI_LobbyRoleButtonBase
{
    private enum Buttons
    {
        LeftArmButton,
    }

    protected override Type ButtonElementsType => typeof(Buttons);
    public override Define.TitanRole Role => Define.TitanRole.LeftArm;
}
