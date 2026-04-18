using System;

public sealed class UI_LeftLegButton : UI_LobbyRoleButtonBase
{
    private enum Buttons
    {
        LeftLegButton,
    }

    protected override Type ButtonElementsType => typeof(Buttons);
    public override Define.TitanRole Role => Define.TitanRole.LeftLeg;
}
