namespace Titan
{
    using TitanRole = global::Define.TitanRole;

    public interface ITitanRoleController
    {
        TitanRole Role { get; }
        void TickRoleInput(float deltaTime);
    }
}
