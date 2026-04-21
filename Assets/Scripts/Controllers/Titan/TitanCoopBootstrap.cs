using UnityEngine;

public static class TitanCoopBootstrap
{
    private static bool IsBootstrapEnabled
    {
        get
        {
#if UNITY_EDITOR
            return true;
#else
            return Debug.isDebugBuild;
#endif
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    public static void AttachControllerIfMissing()
    {
        if (!IsBootstrapEnabled)
        {
            return;
        }

        TitanRigRuntime existingRig = Object.FindAnyObjectByType<TitanRigRuntime>();
        GameObject target = existingRig != null ? existingRig.gameObject : null;

        if (target == null)
        {
            target = GameObject.Find("Titan");
        }

        if (target == null)
        {
            Animator[] animators = Object.FindObjectsByType<Animator>();
            for (int i = 0; i < animators.Length; i++)
            {
                Animator current = animators[i];
                if (current == null)
                {
                    continue;
                }

                string lowerName = current.gameObject.name.ToLowerInvariant();
                if (!lowerName.Contains("trash") && !lowerName.Contains("titan"))
                {
                    continue;
                }

                target = current.gameObject;
                break;
            }
        }

        if (target == null)
        {
            return;
        }

        EnsureRuntime(target);
    }

    public static void EnsureRuntime(GameObject target)
    {
        if (target == null)
            return;

        EnsurePhysicsComponents(target);

        TitanController controller = target.GetComponent<TitanController>();
        if (controller == null)
            controller = target.AddComponent<TitanController>();

        controller.EnsureInitialized();
    }

    private static void EnsurePhysicsComponents(GameObject target)
    {
        Rigidbody rigidbody = target.GetComponent<Rigidbody>();
        if (rigidbody == null)
        {
            rigidbody = target.AddComponent<Rigidbody>();
        }

        rigidbody.useGravity = true;
        rigidbody.isKinematic = false;
        rigidbody.mass = 900f;
        rigidbody.linearDamping = 0.35f;
        rigidbody.angularDamping = 1.2f;
        rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // Ensure we always have at least one reliable collider attached to the Rigidbody root.
        // (Convex MeshCollider generation can silently fail for complex meshes, which makes the Titan fall through the floor.)
        EnsureRootCollider(target);

        // Try to fit colliders closer to the visual mesh using compound convex MeshColliders.
        // This is more physically expressive than a single capsule and keeps the Titan dynamic.
        EnsureCompoundMeshColliders(target);

        // Put the Titan on Default layer so it collides with the default floor.
        SetLayerRecursively(target.transform, 0);

        InputDebug.Log($"Titan physics boot: target={target.name} rb={rigidbody != null} colliders={target.GetComponentsInChildren<Collider>(true).Length}");

        CapsuleCollider capsule = target.GetComponent<CapsuleCollider>();
        if (capsule != null)
            InputDebug.Log($"Titan root capsule: center={capsule.center} radius={capsule.radius} height={capsule.height} dir={capsule.direction}");

    }

    private static void EnsureRootCollider(GameObject target)
    {
        if (target == null)
            return;

        Collider existing = target.GetComponent<Collider>();
        if (existing != null)
        {
            existing.isTrigger = false;
            return;
        }

        CapsuleCollider capsule = target.AddComponent<CapsuleCollider>();
        capsule.direction = 1; // Y
        capsule.isTrigger = false;

        // Fit capsule to renderer bounds.
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            capsule.center = new Vector3(0f, 1f, 0f);
            capsule.height = 2f;
            capsule.radius = 0.5f;
            return;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
                continue;
            bounds.Encapsulate(renderers[i].bounds);
        }

        Vector3 localCenter = target.transform.InverseTransformPoint(bounds.center);
        float radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
        float height = Mathf.Max(bounds.size.y, radius * 2f);

        capsule.center = localCenter;
        capsule.radius = Mathf.Clamp(radius, 0.1f, 10f);
        capsule.height = Mathf.Clamp(height, capsule.radius * 2f, 50f);
    }

    private static void EnsureCompoundMeshColliders(GameObject target)
    {
        if (target == null)
            return;

        MeshFilter[] meshFilters = target.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter meshFilter = meshFilters[i];
            if (meshFilter == null || meshFilter.sharedMesh == null)
                continue;

            // Skip the root; it already has a stable primitive collider.
            if (meshFilter.gameObject == target)
                continue;

            // If there is already any collider, keep it.
            if (meshFilter.GetComponent<Collider>() != null)
                continue;

            MeshCollider meshCollider = meshFilter.gameObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = meshFilter.sharedMesh;
            meshCollider.convex = true;
            meshCollider.isTrigger = false;
        }

        // Skinned meshes can't use non-convex MeshCollider with a dynamic Rigidbody.
        // As a practical compromise, attach per-renderer box colliders to approximate volume.
        SkinnedMeshRenderer[] skinned = target.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinned.Length; i++)
        {
            SkinnedMeshRenderer renderer = skinned[i];
            if (renderer == null)
                continue;

            if (renderer.GetComponent<Collider>() != null)
                continue;

            BoxCollider box = renderer.gameObject.AddComponent<BoxCollider>();
            Bounds local = renderer.localBounds;
            box.center = local.center;
            box.size = local.size;
            box.isTrigger = false;
        }
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        if (root == null)
            return;

        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }
}

public sealed class SkinnedMeshColliderSync : MonoBehaviour
{
    private const float SyncIntervalSeconds = 0.05f;

    private SkinnedMeshRenderer sourceRenderer;
    private MeshCollider targetCollider;
    private Mesh bakedMesh;
    private float elapsed;

    public void Initialize(SkinnedMeshRenderer renderer, MeshCollider collider)
    {
        sourceRenderer = renderer;
        targetCollider = collider;
        EnsureBakedMesh();
        SyncNow();
        elapsed = 0f;
    }

    private void LateUpdate()
    {
        elapsed += Time.deltaTime;
        if (elapsed < SyncIntervalSeconds)
        {
            return;
        }

        elapsed = 0f;
        SyncNow();
    }

    private void OnDestroy()
    {
        if (bakedMesh != null)
        {
            Destroy(bakedMesh);
        }
    }

    private void EnsureBakedMesh()
    {
        if (bakedMesh == null)
        {
            bakedMesh = new Mesh
            {
                name = "SkinnedColliderMesh"
            };
        }
    }

    private void SyncNow()
    {
        if (sourceRenderer == null || targetCollider == null)
        {
            return;
        }

        EnsureBakedMesh();
        sourceRenderer.BakeMesh(bakedMesh, true);
        targetCollider.sharedMesh = null;
        targetCollider.sharedMesh = bakedMesh;
    }
}
