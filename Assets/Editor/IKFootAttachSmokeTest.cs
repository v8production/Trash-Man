using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations.Rigging;

public static class IKFootAttachSmokeTest
{
    public static void Run()
    {
        try
        {
            RunInternal();
            Debug.Log("[IKFootAttachSmokeTest] OK");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[IKFootAttachSmokeTest] FAILED: {ex}");
            EditorApplication.Exit(1);
            return;
        }

        EditorApplication.Exit(0);
    }

    private static void RunInternal()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.position = Vector3.zero;

        GameObject legRoot = new GameObject("LegRoot");
        legRoot.transform.position = new Vector3(0f, 1f, 0f);

        GameObject foot = new GameObject("Foot");
        foot.transform.position = new Vector3(0f, 0.08f, 0f);
        foot.transform.rotation = Quaternion.Euler(0f, 30f, 0f);

        GameObject target = new GameObject("IKTarget");

        GameObject constraintGo = new GameObject("TwoBoneIK");
        TwoBoneIKConstraint constraint = constraintGo.AddComponent<TwoBoneIKConstraint>();
        TwoBoneIKConstraintData data = constraint.data;
        data.root = legRoot.transform;
        data.target = target.transform;
        constraint.data = data;
        constraint.weight = 0f;

        GameObject controllerGo = new GameObject("Controller");
        FootAttachController controller = controllerGo.AddComponent<FootAttachController>();

        SetPrivate(controller, "twoBoneIK", constraint);
        SetPrivate(controller, "foot", foot.transform);
        SetPrivate(controller, "ikTarget", target.transform);
        SetPrivate(controller, "bottomProbe", foot.transform);
        SetPrivate(controller, "groundLayers", (LayerMask)~0);
        SetPrivate(controller, "autoAttachOnGround", true);
        SetPrivate(controller, "detachWhileRightMouseHeld", false);
        SetPrivate(controller, "maxTargetDistanceFromRoot", 10f);

        // In batchmode (non-play mode), Time.deltaTime can be 0.
        // Drive the internal loop pieces with a synthetic deltaTime.
        MethodInfo refresh = GetInstanceMethod(controller, "RefreshAttachmentState");
        MethodInfo applyPose = GetInstanceMethod(controller, "ApplyIKTargetPose");
        MethodInfo blend = GetInstanceMethod(controller, "BlendIKWeight");

        const float syntheticDeltaTime = 1f / 60f;
        for (int i = 0; i < 3; i++)
        {
            refresh.Invoke(controller, null);
            applyPose.Invoke(controller, null);
            blend.Invoke(controller, new object[] { syntheticDeltaTime });
        }

        if (!controller.IsAttached)
        {
            throw new InvalidOperationException("Controller did not auto-attach on ground");
        }

        float distToGround = Vector3.Distance(target.transform.position, Vector3.zero);
        if (distToGround > 0.05f)
        {
            throw new InvalidOperationException($"IK target not near ground. dist={distToGround:F3}");
        }

        float angle = Quaternion.Angle(target.transform.rotation, foot.transform.rotation);
        if (angle > 0.1f)
        {
            throw new InvalidOperationException($"IK target rotation mismatch. angle={angle:F3}");
        }

        if (constraint.weight <= 0f)
        {
            throw new InvalidOperationException("Constraint weight did not blend up");
        }

        UnityEngine.Object.DestroyImmediate(controllerGo);
        UnityEngine.Object.DestroyImmediate(constraintGo);
        UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(foot);
        UnityEngine.Object.DestroyImmediate(legRoot);
        UnityEngine.Object.DestroyImmediate(ground);
    }

    private static void SetPrivate<T>(object instance, string fieldName, T value)
    {
        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
        {
            throw new MissingFieldException(instance.GetType().Name, fieldName);
        }

        field.SetValue(instance, value);
    }

    private static MethodInfo GetInstanceMethod(object instance, string methodName)
    {
        Type type = instance.GetType();
        MethodInfo method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (method == null)
        {
            throw new MissingMethodException(type.Name, methodName);
        }

        return method;
    }
}
