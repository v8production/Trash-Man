using System;

public sealed class UI_RightLegButton : UI_LobbyRoleButtonBase
{
    private enum Buttons
    {
        RightLegButton,
    }

    protected override Type ButtonElementsType => typeof(Buttons);
    public override Define.TitanRole Role => Define.TitanRole.RightLeg;
}
