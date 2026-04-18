using System;

public sealed class UI_BodyButton : UI_LobbyRoleButtonBase
{
    private enum Buttons
    {
        BodyButton,
    }

    protected override Type ButtonElementsType => typeof(Buttons);
    public override Define.TitanRole Role => Define.TitanRole.Body;
}
