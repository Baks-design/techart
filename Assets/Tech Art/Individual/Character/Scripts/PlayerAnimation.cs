using UnityEngine;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Collections;

[BurstCompile]
public class PlayerAnimator : MonoBehaviour
{
    [SerializeField] private CharacterController controller;
    [SerializeField] private Animator _animator;
    [SerializeField] private float _normalWalkSpeed = 1.7f;
    [SerializeField] private float _normalSprintSpeed = 5f;
    [SerializeField] private float _maxSprintScale = 1.4f;
    private Transform _transform;
    private Vector3 _previousPosition;
    private AnimationState _currentState;
    public struct AnimationState
    {
        public bool IsWalking;
        public bool IsRunning;
        public bool RollTriggered;
        public Vector3 Direction;
        public float MotionScale;
        public float SpeedZ;
    }
    private float _deltaTime;
    private float _inverseWalkSpeed;
    private float _inverseSprintSpeed;
    private float _inverseSprintScaleDivisor;
    private float _runThreshold;
    private float _idleThresholdSq;
    private float _walkThresholdWithHysteresisSq;
    private float _runThresholdWithHysteresisSq;
    private float _cachedSpeed;
    private float _cachedMotionScale;
    private bool _cachedWalking;
    private bool _cachedRunning;
    private const float IdleThreshold = 0.2f;
    private const float HysteresisRunThreshold = 0.15f;
    private const float HysteresisWalkThreshold = 0.05f;
    private const float SprintScaleDivisor = 3f;
    private const float FloatPrecision = 0.001f;
    private static readonly int Speed = Animator.StringToHash("Speed");
    private static readonly int MotionScale = Animator.StringToHash("MotionScale");
    private static readonly int Walking = Animator.StringToHash("Walking");
    private static readonly int Running = Animator.StringToHash("Running");
    private static readonly int Roll = Animator.StringToHash("Roll");

    private void Start() => Initialize();

    [BurstCompile]
    private void Initialize()
    {
        _transform = controller.transform;
        _previousPosition = _transform.position;

        _inverseWalkSpeed = 1f / _normalWalkSpeed;
        _inverseSprintSpeed = 1f / _normalSprintSpeed;
        _inverseSprintScaleDivisor = 1f / (SprintScaleDivisor * _normalSprintSpeed);

        _runThreshold = _normalWalkSpeed * 2f;
        _idleThresholdSq = IdleThreshold * IdleThreshold;

        if (!controller) controller = GetComponentInParent<CharacterController>();
        if (!_animator) _animator = GetComponentInParent<Animator>();

        _currentState = new AnimationState
        {
            MotionScale = 1f,
            Direction = Vector3.zero,
            SpeedZ = 0f
        };
    }

    private void LateUpdate()
    {
        _deltaTime = Time.deltaTime;
        if (_deltaTime <= 0f) return;

        UpdateMovementAnimation();
    }

    [BurstCompile]
    private void UpdateMovementAnimation()
    {
        var currentPosition = _transform.position;
        var inverseDeltaTime = 1f / _deltaTime;
        var worldVelocity = new Vector3(
            (currentPosition.x - _previousPosition.x) * inverseDeltaTime,
            (currentPosition.y - _previousPosition.y) * inverseDeltaTime,
            (currentPosition.z - _previousPosition.z) * inverseDeltaTime
        );

        TransformWorldToLocalVelocity(ref worldVelocity, out var localVelocity);
        _previousPosition = currentPosition;

        UpdateAnimationState(localVelocity);
    }

    [BurstCompile]
    private void TransformWorldToLocalVelocity(ref Vector3 worldVelocity, out Vector3 localVelocity)
    {
        var inverseRotation = Quaternion.Inverse(_transform.rotation);
        localVelocity = inverseRotation * worldVelocity;
    }

    [BurstCompile]
    private void UpdateAnimationState(Vector3 localVelocity)
    {
        localVelocity.y = 0f;
        var speedSq = localVelocity.x * localVelocity.x + localVelocity.z * localVelocity.z;
        var speed = speedSq > _idleThresholdSq ? math.sqrt(speedSq) : 0f;

        UpdateMovementStates(speedSq);
        UpdateAnimationParameters(localVelocity, speed, speedSq);
    }

    [BurstCompile]
    private void UpdateMovementStates(float speedSq)
    {
        var runHysteresis = _currentState.IsRunning ? -HysteresisRunThreshold : HysteresisRunThreshold;
        var walkHysteresis = _currentState.IsWalking ? -HysteresisWalkThreshold : HysteresisWalkThreshold;

        var runThresholdWithHysteresis = _runThreshold + runHysteresis;
        var walkThresholdWithHysteresis = IdleThreshold + walkHysteresis;

        _runThresholdWithHysteresisSq = runThresholdWithHysteresis * runThresholdWithHysteresis;
        _walkThresholdWithHysteresisSq = walkThresholdWithHysteresis * walkThresholdWithHysteresis;

        _currentState.IsRunning = speedSq > _runThresholdWithHysteresisSq;
        _currentState.IsWalking = !_currentState.IsRunning && speedSq > _walkThresholdWithHysteresisSq;
    }

    [BurstCompile]
    private void UpdateAnimationParameters(Vector3 localVelocity, float speed, float speedSq)
    {
        if (speedSq > _idleThresholdSq)
        {
            var inverseSpeed = 1f / speed;
            _currentState.Direction.x = localVelocity.x * inverseSpeed;
            _currentState.Direction.y = 0f;
            _currentState.Direction.z = localVelocity.z * inverseSpeed;
            _currentState.SpeedZ = _currentState.Direction.z;
        }
        else
        {
            _currentState.Direction = Vector3.zero;
            _currentState.SpeedZ = 0f;
        }

        if (_currentState.IsWalking)
            _currentState.MotionScale = speed * _inverseWalkSpeed;
        else if (_currentState.IsRunning)
            _currentState.MotionScale = CalculateRunMotionScale(speed);
        else
            _currentState.MotionScale = 1f;

        ApplyAnimatorParameters();
    }

    [BurstCompile]
    private float CalculateRunMotionScale(float speed)
    {
        if (speed < _normalSprintSpeed)
            return speed * _inverseSprintSpeed;

        return math.min(_maxSprintScale, 1f + (speed - _normalSprintSpeed) * _inverseSprintScaleDivisor);
    }

    private void ApplyAnimatorParameters()
    {
        if (math.abs(_cachedSpeed - _currentState.SpeedZ) > FloatPrecision)
        {
            _cachedSpeed = _currentState.SpeedZ;
            _animator.SetFloat(Speed, _cachedSpeed);
        }
        if (math.abs(_cachedMotionScale - _currentState.MotionScale) > FloatPrecision)
        {
            _cachedMotionScale = _currentState.MotionScale;
            _animator.SetFloat(MotionScale, _cachedMotionScale);
        }
        if (_cachedWalking != _currentState.IsWalking)
        {
            _cachedWalking = _currentState.IsWalking;
            _animator.SetBool(Walking, _cachedWalking);
        }
        if (_cachedRunning != _currentState.IsRunning)
        {
            _cachedRunning = _currentState.IsRunning;
            _animator.SetBool(Running, _cachedRunning);
        }
        if (_currentState.RollTriggered)
        {
            _animator.SetTrigger(Roll);
            _currentState.RollTriggered = false;
        }
    }

    public void OnRollStart() => _currentState.RollTriggered = true;

    [BurstCompile]
    private struct AnimationCalculationJob : IJob
    {
        public float3 CurrentPosition;
        public float3 PreviousPosition;
        public quaternion InverseRotation;
        public float DeltaTime;
        public float NormalWalkSpeed;
        public float NormalSprintSpeed;
        public float InverseWalkSpeed;
        public float InverseSprintSpeed;
        public float MaxSprintScale;
        public float InverseSprintScaleDivisor;
        public float IdleThresholdSq;
        public float RunThreshold;
        public float HysteresisRunThreshold;
        public float HysteresisWalkThreshold;
        public NativeArray<AnimationState> CurrentState;

        public void Execute()
        {
            if (DeltaTime <= 0f) return;

            var inverseDeltaTime = 1f / DeltaTime;
            var worldVelocity = (CurrentPosition - PreviousPosition) * inverseDeltaTime;
            var localVelocity = math.mul(InverseRotation, worldVelocity);

            localVelocity.y = 0f;
            var speedSq = math.lengthsq(localVelocity);
            var speed = speedSq > IdleThresholdSq ? math.sqrt(speedSq) : 0f;

            var state = CurrentState[0];
            UpdateState(ref state, localVelocity, speed, speedSq);
            CurrentState[0] = state;
        }

        [BurstCompile]
        private readonly void UpdateState(ref AnimationState state, float3 localVelocity, float speed, float speedSq)
        {
            var runHysteresis = state.IsRunning ? -HysteresisRunThreshold : HysteresisRunThreshold;
            var walkHysteresis = state.IsWalking ? -HysteresisWalkThreshold : HysteresisWalkThreshold;

            var runThresholdWithHysteresis = RunThreshold + runHysteresis;
            var walkThresholdWithHysteresis = math.sqrt(IdleThresholdSq) + walkHysteresis;

            var runThresholdSq = runThresholdWithHysteresis * runThresholdWithHysteresis;
            var walkThresholdSq = walkThresholdWithHysteresis * walkThresholdWithHysteresis;

            state.IsRunning = speedSq > runThresholdSq;
            state.IsWalking = !state.IsRunning && speedSq > walkThresholdSq;

            if (speedSq > IdleThresholdSq)
            {
                var inverseSpeed = 1f / speed;
                state.Direction = localVelocity * inverseSpeed;
                state.SpeedZ = state.Direction.z;
            }
            else
            {
                state.Direction = float3.zero;
                state.SpeedZ = 0f;
            }

            if (state.IsWalking)
                state.MotionScale = speed * InverseWalkSpeed;
            else if (state.IsRunning)
                state.MotionScale = speed < NormalSprintSpeed
                    ? speed * InverseSprintSpeed
                    : math.min(MaxSprintScale, 1f + (speed - NormalSprintSpeed) * InverseSprintScaleDivisor);
            else
                state.MotionScale = 1f;
        }
    }
}