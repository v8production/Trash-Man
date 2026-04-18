using System;

public sealed class UI_RightArmButton : UI_LobbyRoleButtonBase
{
    private enum Buttons
    {
        RightArmButton,
    }

    protected override Type ButtonElementsType => typeof(Buttons);
    public override Define.TitanRole Role => Define.TitanRole.RightArm;
}
