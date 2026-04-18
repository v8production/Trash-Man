using System.Collections.Generic;

public interface ILobbyWorldButtonInteractionTarget
{
    bool IsProximityInteractable { get; }
    float ProximitySqrDistance { get; }
    int InteractionPriority { get; }
}

public static class LobbyWorldButtonInteractionRegistry
{
    private const float DistanceTieThreshold = 0.0001f;

    private static readonly List<ILobbyWorldButtonInteractionTarget> s_targets = new();

    public static void Register(ILobbyWorldButtonInteractionTarget target)
    {
        if (target == null || s_targets.Contains(target))
            return;

        s_targets.Add(target);
    }

    public static void Unregister(ILobbyWorldButtonInteractionTarget target)
    {
        if (target == null)
            return;

        s_targets.Remove(target);
    }

    public static bool IsClosestAvailable(ILobbyWorldButtonInteractionTarget requester)
    {
        if (requester == null || !requester.IsProximityInteractable)
            return false;

        for (int i = 0; i < s_targets.Count; i++)
        {
            ILobbyWorldButtonInteractionTarget other = s_targets[i];
            if (other == null || ReferenceEquals(other, requester) || !other.IsProximityInteractable)
                continue;

            if (other.ProximitySqrDistance + DistanceTieThreshold < requester.ProximitySqrDistance)
                return false;

            bool sameDistance = requester.ProximitySqrDistance + DistanceTieThreshold >= other.ProximitySqrDistance
                && other.ProximitySqrDistance + DistanceTieThreshold >= requester.ProximitySqrDistance;

            if (sameDistance && other.InteractionPriority < requester.InteractionPriority)
                return false;
        }

        return true;
    }
}
