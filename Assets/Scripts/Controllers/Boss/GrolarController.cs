using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GrolarController : BossController
{
    private Animator _animator;

    protected Define.GrolarAnimState _animState;

    public virtual Define.GrolarAnimState AnimState
    {
        get { return _animState; }
        set
        {
            // Note: Enum default (0) can match the first desired state.
            // If we early-return here, the Animator may never receive the initial CrossFade.
            if (Equals(_animState, value))
            {
                if (_animator == null)
                    return;

                AnimatorStateInfo info = _animator.GetCurrentAnimatorStateInfo(0);
                if (info.IsName(value.ToString()))
                    return;
            }
            _animState = value;

            float fade = IsLocomotionState(_animState) ? _locomotionCrossFade : _actionCrossFade;
            if (_animator != null)
                _animator.CrossFade(_animState.ToString(), fade);
        }
    }
    private BossStat _stat;

    [Header("Animation Blend")]
    [SerializeField] private float _locomotionCrossFade = 0.20f;
    [SerializeField] private float _actionCrossFade = 0.10f;

    [Header("Rotation")]
    [SerializeField] private float _rotationLerpSpeed = 10f;

    [Header("Target")]
    [SerializeField] private TitanController _titan;

    [Header("Orbit")]
    [SerializeField] private float _orbitRadius = 4f;
    [SerializeField] private float _stateDurationMin = 1.0f;
    [SerializeField] private float _stateDurationMax = 3.0f;

    [Header("Attack")]
    [Range(0f, 1f)]
    [SerializeField] private float _attackTryChance = 0.25f;
    [SerializeField] private float _attackDistance = 1f;

    [Header("Move Speed")]
    [SerializeField] private float _walkSpeed = 1f;
    [SerializeField] private float _runSpeed = 2f;

    private Transform _titanRoot;

    private OrbitState _orbitState;
    private float _orbitAngleRad;
    private float _stateTimer;
    private bool _attackInProgress;
    private bool _swingHitHandled;
    private bool _queuedRebound;
    private Coroutine _attackRoutine;

    private Quaternion _desiredRotation;

    private GrolarAnimationEventRelay _eventRelay;

    private enum OrbitState
    {
        RunCW,
        RunCCW,
        WalkCW,
        WalkCCW,
    }

    private static bool IsLocomotionState(Define.GrolarAnimState state)
    {
        return state == Define.GrolarAnimState.Run00 || state == Define.GrolarAnimState.Walk00;
    }

    private void Awake()
    {
        _animator = GetComponentInChildren<Animator>(true);
        _stat = gameObject.GetComponent<BossStat>();

        if (_animator != null)
        {
            _eventRelay = _animator.gameObject.GetComponent<GrolarAnimationEventRelay>();
            if (_eventRelay == null)
                _eventRelay = _animator.gameObject.AddComponent<GrolarAnimationEventRelay>();
            _eventRelay.Bind(this);
        }
    }

    private void Start()
    {
        BindTitanIfNeeded();
        InitializeOrbit();

        _desiredRotation = transform.rotation;
    }

    private void Update()
    {
        if (_attackInProgress)
        {
            ApplyRotationSmoothing(Time.deltaTime);
            return;
        }

        if (!BindTitanIfNeeded())
            return;

        TickOrbit(Time.deltaTime);
        ApplyRotationSmoothing(Time.deltaTime);
    }

    private bool BindTitanIfNeeded()
    {
        if (_titan != null && _titanRoot != null)
            return true;

        Managers.TitanRig.EnsureBoundFromScene();
        TitanRigRuntime runtime = Managers.TitanRig.Runtime;
        if (runtime == null)
        {
            // Fallback: allow binding even if TitanRigRuntime isn't available yet.
            if (_titan == null)
                _titan = Object.FindAnyObjectByType<TitanController>();

            if (_titan != null)
                _titanRoot = _titan.transform;

            return _titanRoot != null;
        }

        _titanRoot = runtime.MovementRoot != null ? runtime.MovementRoot : runtime.transform;
        if (_titan == null)
            _titan = runtime.GetComponent<TitanController>();

        return _titanRoot != null;
    }

    private void InitializeOrbit()
    {
        if (!BindTitanIfNeeded())
            return;

        Vector3 titanPos = _titanRoot.position;
        Vector3 planar = Vector3.ProjectOnPlane(transform.position - titanPos, Vector3.up);
        if (planar.sqrMagnitude <= 0.0001f)
            planar = Vector3.right;

        _orbitAngleRad = Mathf.Atan2(planar.z, planar.x);
        transform.position = GetOrbitWorldPosition(_orbitAngleRad);

        _orbitState = Random.value < 0.5f
            ? (Random.value < 0.5f ? OrbitState.RunCW : OrbitState.RunCCW)
            : (Random.value < 0.5f ? OrbitState.WalkCW : OrbitState.WalkCCW);

        EnterOrbitState(_orbitState, false);
    }

    private void TickOrbit(float deltaTime)
    {
        _stateTimer -= deltaTime;
        if (_stateTimer <= 0f)
        {
            OrbitState next = PickNextOrbitState(_orbitState);
            EnterOrbitState(next, true);
        }

        float speed = IsRun(_orbitState) ? _runSpeed : _walkSpeed;
        int dir = IsCW(_orbitState) ? -1 : 1;
        float omega = (_orbitRadius > 0.0001f) ? (speed / _orbitRadius) : 0f;
        _orbitAngleRad += dir * omega * deltaTime;

        Vector3 nextPos = GetOrbitWorldPosition(_orbitAngleRad);
        transform.position = nextPos;

        Vector3 titanPos = _titanRoot.position;
        Vector3 tangent = Vector3.Cross((nextPos - titanPos), Vector3.up).normalized;
        if (dir < 0)
            tangent = -tangent;
        SetDesiredRotationFromDirection(tangent);
    }

    private Vector3 GetOrbitWorldPosition(float angleRad)
    {
        Vector3 titanPos = _titanRoot.position;
        Vector3 offset = new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad)) * _orbitRadius;
        Vector3 pos = titanPos + offset;
        pos.y = transform.position.y;
        return pos;
    }

    private void EnterOrbitState(OrbitState state, bool allowAttackTry)
    {
        _orbitState = state;
        float min = Mathf.Max(0.05f, _stateDurationMin);
        float max = Mathf.Max(min, _stateDurationMax);
        _stateTimer = Random.Range(min, max);

        AnimState = IsRun(state) ? Define.GrolarAnimState.Run00 : Define.GrolarAnimState.Walk00;

        if (allowAttackTry)
            TryStartAttackOnStateTransition();
    }

    private void TryStartAttackOnStateTransition()
    {
        if (_attackInProgress)
            return;
        if (_titanRoot == null)
            return;
        if (Random.value > _attackTryChance)
            return;

        if (_attackRoutine != null)
            StopCoroutine(_attackRoutine);

        _attackRoutine = StartCoroutine(CoAttackRoutine());
    }

    private OrbitState PickNextOrbitState(OrbitState current)
    {
        // Transitions (spec):
        // A RunCW  : A, C
        // B RunCCW : B, D
        // C WalkCW : A, C, D
        // D WalkCCW: B, C, D
        List<OrbitState> candidates = new(3);
        switch (current)
        {
            case OrbitState.RunCW:
                candidates.Add(OrbitState.RunCW);
                candidates.Add(OrbitState.WalkCW);
                break;
            case OrbitState.RunCCW:
                candidates.Add(OrbitState.RunCCW);
                candidates.Add(OrbitState.WalkCCW);
                break;
            case OrbitState.WalkCW:
                candidates.Add(OrbitState.RunCW);
                candidates.Add(OrbitState.WalkCW);
                candidates.Add(OrbitState.WalkCCW);
                break;
            case OrbitState.WalkCCW:
                candidates.Add(OrbitState.RunCCW);
                candidates.Add(OrbitState.WalkCW);
                candidates.Add(OrbitState.WalkCCW);
                break;
        }

        return candidates[Random.Range(0, candidates.Count)];
    }

    private static bool IsRun(OrbitState state)
    {
        return state == OrbitState.RunCW || state == OrbitState.RunCCW;
    }

    private static bool IsCW(OrbitState state)
    {
        return state == OrbitState.RunCW || state == OrbitState.WalkCW;
    }

    private IEnumerator CoAttackRoutine()
    {
        _attackInProgress = true;
        _queuedRebound = false;
        _swingHitHandled = false;

        // MUST face Titan before assigning Alert00_Roar, and keep facing during the roar.
        ForceFaceTitanNow();
        yield return null; // ensure rotation is applied before crossfade this frame
        ForceFaceTitanNow();
        yield return CoPlayAnimAndWait(Define.GrolarAnimState.Alert00_Roar, 1f, false, true);

        AnimState = Define.GrolarAnimState.Run00;
        yield return CoMoveToDistance(_attackDistance, _runSpeed);

        yield return CoFaceTitan();
        yield return CoPlayAnimAndWait(Define.GrolarAnimState.Attack00_Alert, 0.75f);

        _swingHitHandled = false;
        _queuedRebound = false;
        yield return CoPlayAnimAndWait(Define.GrolarAnimState.Attack00_Swing, 1f, true);

        if (_queuedRebound)
            yield return CoPlayAnimAndWait(Define.GrolarAnimState.Attack00_Rebound, 1f);

        AnimState = Define.GrolarAnimState.Run00;
        yield return CoMoveToMinDistance(_orbitRadius, _runSpeed);

        _attackInProgress = false;
        _attackRoutine = null;

        EnterOrbitState(_orbitState, false);
    }

    private IEnumerator CoFaceTitan()
    {
        if (_titanRoot == null)
            yield break;

        Vector3 titanPos = _titanRoot.position;
        Vector3 toTitan = Vector3.ProjectOnPlane(titanPos - transform.position, Vector3.up);
        SetDesiredRotationFromDirection(toTitan);

        yield return null;
    }

    private void SetDesiredRotationToTitan()
    {
        if (_titanRoot == null)
            return;

        Vector3 titanPos = _titanRoot.position;
        Vector3 toTitan = Vector3.ProjectOnPlane(titanPos - transform.position, Vector3.up);
        SetDesiredRotationFromDirection(toTitan);
    }

    private void SetDesiredRotationFromDirection(Vector3 planarDirection)
    {
        if (planarDirection.sqrMagnitude <= 0.0001f)
            return;

        _desiredRotation = Quaternion.LookRotation(planarDirection.normalized, Vector3.up);
    }

    private void ApplyRotationSmoothing(float deltaTime)
    {
        if (_rotationLerpSpeed <= 0f)
        {
            transform.rotation = _desiredRotation;
            return;
        }

        transform.rotation = Quaternion.Slerp(transform.rotation, _desiredRotation, _rotationLerpSpeed * deltaTime);
    }

    private void ForceFaceTitanNow()
    {
        if (_titanRoot == null)
            return;

        Vector3 titanPos = _titanRoot.position;
        Vector3 toTitan = Vector3.ProjectOnPlane(titanPos - transform.position, Vector3.up);
        if (toTitan.sqrMagnitude <= 0.0001f)
            return;

        transform.rotation = Quaternion.LookRotation(toTitan.normalized, Vector3.up);
        _desiredRotation = transform.rotation;
    }

    private IEnumerator CoMoveToDistance(float targetDistance, float speed)
    {
        while (_titanRoot != null)
        {
            Vector3 titanPos = _titanRoot.position;
            Vector3 toTitan = Vector3.ProjectOnPlane(titanPos - transform.position, Vector3.up);
            float dist = toTitan.magnitude;
            if (dist <= targetDistance)
                yield break;

            Vector3 dir = toTitan.normalized;
            transform.position += dir * speed * Time.deltaTime;
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            yield return null;
        }
    }

    private IEnumerator CoMoveToMinDistance(float minDistance, float speed)
    {
        while (_titanRoot != null)
        {
            Vector3 titanPos = _titanRoot.position;
            Vector3 fromTitan = Vector3.ProjectOnPlane(transform.position - titanPos, Vector3.up);
            float dist = fromTitan.magnitude;
            if (dist >= minDistance)
                yield break;

            Vector3 dir = fromTitan.sqrMagnitude > 0.0001f ? fromTitan.normalized : Vector3.right;
            transform.position += dir * speed * Time.deltaTime;
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            yield return null;
        }
    }

    private IEnumerator CoPlayAnimAndWait(Define.GrolarAnimState state, float speed, bool allowInterruptByRebound = false, bool faceTitanDuring = false)
    {
        if (_animator == null)
            yield break;

        float prevSpeed = _animator.speed;
        _animator.speed = speed;
        AnimState = state;

        while (true)
        {
            if (faceTitanDuring)
                ForceFaceTitanNow();

            if (allowInterruptByRebound && _queuedRebound)
                break;

            AnimatorStateInfo info = _animator.GetCurrentAnimatorStateInfo(0);
            if (info.IsName(state.ToString()) && info.normalizedTime >= 1f)
                break;
            yield return null;
        }

        _animator.speed = prevSpeed;
    }

    internal void NotifySwingHit()
    {
        if (_attackInProgress == false)
            return;
        if (_swingHitHandled)
            return;
        if (AnimState != Define.GrolarAnimState.Attack00_Swing)
            return;
        if (_titanRoot == null)
            return;

        _swingHitHandled = true;

        float dist = Vector3.ProjectOnPlane(_titanRoot.position - transform.position, Vector3.up).magnitude;
        if (dist > _attackDistance)
            return;

        if (_titan != null && _titan.Guard)
        {
            _queuedRebound = true;
            AnimState = Define.GrolarAnimState.Attack00_Rebound;
            return;
        }

        if (_titan != null && _titan.Stat != null)
            _titan.Stat.OnAttacked(_stat);
    }

}

public sealed class GrolarAnimationEventRelay : MonoBehaviour
{
    private GrolarController _owner;

    public void Bind(GrolarController owner)
    {
        _owner = owner;
    }

    // AnimationEvent name (Attack00_Swing.fbx.meta): onHit
    public void onHit()
    {
        _owner?.NotifySwingHit();
    }
}
