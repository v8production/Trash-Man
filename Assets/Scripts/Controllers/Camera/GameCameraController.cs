using UnityEngine;

[DisallowMultipleComponent]
public class GameCameraController : MonoBehaviour
{
    [Header("Optional References")]
    [SerializeField] private Transform _titanTarget;
    [SerializeField] private BossController _bossTarget;

    [Header("Framing")]
    [SerializeField] private Vector3 _titanPivotOffset = new(0f, 1f, 0f);
    [SerializeField] private bool _useDynamicTitanHeight = true;
    [SerializeField] private float _dynamicHeightWeight = 0.75f;
    [SerializeField] private float _dynamicHeightLerpSpeed = 10f;
    [SerializeField] private float _minimumPivotHeightAboveRoot = 0.25f;
    [SerializeField] private Vector3 _bossLookOffset = new(0f, 0.5f, 0f);
    [SerializeField] private float _followDistance = 2f;
    [SerializeField] private float _heightOffset = 0f;
    [SerializeField] private float _lookAtBossWeight = 0f;

    [Header("Smoothing")]
    [SerializeField] private float _followLerpSpeed = 8f;
    [SerializeField] private float _rotationLerpSpeed = 10f;

    [Header("Fallback")]
    [SerializeField] private Vector3 _fallbackForward = Vector3.forward;
    [SerializeField] private float _minimumPlanarDistance = 0.25f;

    private float _smoothedDynamicPivotY;
    private bool _hasSmoothedDynamicPivotY;

    private void OnEnable()
    {
        ResolveReferences();
        if (TryBuildCameraPose(out Vector3 desiredPosition, out Quaternion desiredRotation))
        {
            transform.SetPositionAndRotation(desiredPosition, desiredRotation);
        }
    }

    private void LateUpdate()
    {
        ResolveReferences();

        if (!TryBuildCameraPose(out Vector3 desiredPosition, out Quaternion desiredRotation))
            return;

        float followT = 1f - Mathf.Exp(-_followLerpSpeed * Time.deltaTime);
        float rotationT = 1f - Mathf.Exp(-_rotationLerpSpeed * Time.deltaTime);

        transform.position = Vector3.Lerp(transform.position, desiredPosition, followT);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationT);
    }

    public void SetTargets(Transform titanTarget, BossController bossTarget)
    {
        _titanTarget = titanTarget;
        _bossTarget = bossTarget;

        if (TryBuildCameraPose(out Vector3 desiredPosition, out Quaternion desiredRotation))
        {
            transform.SetPositionAndRotation(desiredPosition, desiredRotation);
        }
    }

    private void ResolveReferences()
    {
        if (_titanTarget == null)
        {
            Transform movementRoot = Managers.TitanRig.MovementRoot;
            if (movementRoot != null)
            {
                _titanTarget = movementRoot;
            }
            else
            {
                TitanController titanController = FindAnyObjectByType<TitanController>();
                if (titanController != null)
                    _titanTarget = titanController.transform;
            }
        }

        if (_bossTarget == null)
            _bossTarget = FindAnyObjectByType<BossController>();
    }

    private bool TryBuildCameraPose(out Vector3 desiredPosition, out Quaternion desiredRotation)
    {
        desiredPosition = transform.position;
        desiredRotation = transform.rotation;

        if (_titanTarget == null)
            return false;

        Vector3 titanPivot = ResolveTitanPivot();
        Vector3 titanForward = ResolvePlanarForward(titanPivot);
        Vector3 desiredLookPoint = ResolveLookPoint(titanPivot);

        desiredPosition = titanPivot - titanForward * _followDistance + Vector3.up * _heightOffset;

        Vector3 lookDirection = desiredLookPoint - desiredPosition;
        if (lookDirection.sqrMagnitude <= 0.0001f)
            lookDirection = titanForward;

        desiredRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
        return true;
    }

    private Vector3 ResolvePlanarForward(Vector3 titanPivot)
    {
        if (_bossTarget != null)
        {
            Vector3 bossPivot = _bossTarget.transform.position + _bossLookOffset;
            Vector3 planarToBoss = Vector3.ProjectOnPlane(bossPivot - titanPivot, Vector3.up);
            if (planarToBoss.sqrMagnitude >= _minimumPlanarDistance * _minimumPlanarDistance)
                return planarToBoss.normalized;
        }

        Vector3 titanForward = Vector3.ProjectOnPlane(_titanTarget.forward, Vector3.up);
        if (titanForward.sqrMagnitude > 0.0001f)
            return titanForward.normalized;

        Vector3 fallbackForward = Vector3.ProjectOnPlane(_fallbackForward, Vector3.up);
        return fallbackForward.sqrMagnitude > 0.0001f ? fallbackForward.normalized : Vector3.forward;
    }

    private Vector3 ResolveLookPoint(Vector3 titanPivot)
    {
        if (_bossTarget == null)
            return titanPivot;

        Vector3 bossPivot = _bossTarget.transform.position + _bossLookOffset;
        float bossWeight = Mathf.Clamp01(_lookAtBossWeight);
        return Vector3.Lerp(titanPivot, bossPivot, bossWeight);
    }

    private Vector3 ResolveTitanPivot()
    {
        Vector3 basePivot = _titanTarget.position + _titanPivotOffset;
        if (!_useDynamicTitanHeight || !TryGetDynamicTitanPivotY(basePivot.y, out float dynamicPivotY))
        {
            return basePivot;
        }

        float weight = Mathf.Clamp01(_dynamicHeightWeight);
        float targetY = Mathf.Lerp(basePivot.y, dynamicPivotY, weight);
        if (!_hasSmoothedDynamicPivotY)
        {
            _smoothedDynamicPivotY = targetY;
            _hasSmoothedDynamicPivotY = true;
        }
        else
        {
            float t = 1f - Mathf.Exp(-Mathf.Max(0.01f, _dynamicHeightLerpSpeed) * Time.deltaTime);
            _smoothedDynamicPivotY = Mathf.Lerp(_smoothedDynamicPivotY, targetY, t);
        }

        basePivot.y = _smoothedDynamicPivotY;
        return basePivot;
    }

    private bool TryGetDynamicTitanPivotY(float fallbackY, out float pivotY)
    {
        pivotY = fallbackY;

        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;
        int count = 0;

        IncludeTransformHeight(_titanTarget, ref minY, ref maxY, ref count);
        IncludeTransformHeight(Managers.TitanRig.Spine, ref minY, ref maxY, ref count);
        IncludeTransformHeight(Managers.TitanRig.LeftShoulder, ref minY, ref maxY, ref count);
        IncludeTransformHeight(Managers.TitanRig.LeftElbow, ref minY, ref maxY, ref count);
        IncludeTransformHeight(Managers.TitanRig.RightShoulder, ref minY, ref maxY, ref count);
        IncludeTransformHeight(Managers.TitanRig.RightElbow, ref minY, ref maxY, ref count);
        IncludeTransformHeight(Managers.TitanRig.LeftHip, ref minY, ref maxY, ref count);
        IncludeTransformHeight(Managers.TitanRig.LeftKnee, ref minY, ref maxY, ref count);
        IncludeTransformHeight(Managers.TitanRig.LeftFoot, ref minY, ref maxY, ref count);
        IncludeTransformHeight(Managers.TitanRig.RightHip, ref minY, ref maxY, ref count);
        IncludeTransformHeight(Managers.TitanRig.RightKnee, ref minY, ref maxY, ref count);
        IncludeTransformHeight(Managers.TitanRig.RightFoot, ref minY, ref maxY, ref count);

        if (count <= 0)
        {
            return false;
        }

        float boundsCenterY = (minY + maxY) * 0.5f;
        float rootMinY = _titanTarget.position.y + Mathf.Max(0f, _minimumPivotHeightAboveRoot);
        pivotY = Mathf.Max(boundsCenterY, rootMinY);
        return true;
    }

    private static void IncludeTransformHeight(Transform value, ref float minY, ref float maxY, ref int count)
    {
        if (value == null)
        {
            return;
        }

        float y = value.position.y;
        minY = Mathf.Min(minY, y);
        maxY = Mathf.Max(maxY, y);
        count++;
    }
}
