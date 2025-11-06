using UnityEngine;

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
    private struct AnimationState
    {
        public bool IsWalking;
        public bool IsRunning;
        public bool RollTriggered;
        public Vector3 Direction;
        public float MotionScale;
    }
    private float _deltaTime;
    private float _inverseWalkSpeed;
    private float _inverseSprintSpeed;
    private float _inverseSprintScaleDivisor;

    private const float IdleThreshold = 0.2f;
    private const float HysteresisRunThreshold = 0.15f;
    private const float HysteresisWalkThreshold = 0.05f;
    private const float SprintScaleDivisor = 3f;

    private static readonly int Speed = Animator.StringToHash("Speed");
    private static readonly int MotionScale = Animator.StringToHash("MotionScale");
    private static readonly int Walking = Animator.StringToHash("Walking");
    private static readonly int Running = Animator.StringToHash("Running");
    private static readonly int Roll = Animator.StringToHash("Roll");

    public bool IsMoving => _currentState.IsWalking || _currentState.IsRunning;
    public Vector3 MovementDirection => _currentState.Direction;

    private void Start()
    {
        _transform = controller.transform;
        _previousPosition = controller.transform.position;

        _inverseWalkSpeed = 1f / _normalWalkSpeed;
        _inverseSprintSpeed = 1f / _normalSprintSpeed;
        _inverseSprintScaleDivisor = 1f / (SprintScaleDivisor * _normalSprintSpeed);
    }

    private void LateUpdate()
    {
        _deltaTime = Time.deltaTime;
        if (_deltaTime <= 0f) return;

        UpdateMovementAnimation();
    }

    private void UpdateMovementAnimation()
    {
        var currentPosition = _transform.position;

        var inverseDeltaTime = 1f / _deltaTime;
        var worldVelocity = (currentPosition - _previousPosition) * inverseDeltaTime;

        var localVelocity = Quaternion.Inverse(_transform.rotation) * worldVelocity;
        _previousPosition = currentPosition;

        UpdateAnimationState(localVelocity);
    }

    private void UpdateAnimationState(Vector3 localVelocity)
    {
        localVelocity.y = 0f;
        var speed = localVelocity.magnitude;

        UpdateMovementStates(speed);
        UpdateAnimationParameters(localVelocity, speed);
    }

    private void UpdateMovementStates(float speed)
    {
        var runThreshold = _normalWalkSpeed * 2f;
        var runHysteresis = _currentState.IsRunning ? -HysteresisRunThreshold : HysteresisRunThreshold;
        var walkHysteresis = _currentState.IsWalking ? -HysteresisWalkThreshold : HysteresisWalkThreshold;

        _currentState.IsRunning = speed > runThreshold + runHysteresis;
        _currentState.IsWalking = !_currentState.IsRunning && speed > IdleThreshold + walkHysteresis;
    }

    private void UpdateAnimationParameters(Vector3 localVelocity, float speed)
    {
        if (speed > IdleThreshold)
        {
            var inverseSpeed = 1f / speed;
            _currentState.Direction = localVelocity * inverseSpeed;
        }
        else
            _currentState.Direction = Vector3.zero;

        if (_currentState.IsWalking)
            _currentState.MotionScale = speed * _inverseWalkSpeed;
        else if (_currentState.IsRunning)
            _currentState.MotionScale = CalculateRunMotionScale(speed);
        else
            _currentState.MotionScale = 1f;

        ApplyAnimatorParameters();
    }

    private float CalculateRunMotionScale(float speed)
    {
        if (speed < _normalSprintSpeed)
            return speed * _inverseSprintSpeed;
        return Mathf.Min(_maxSprintScale, 1f + (speed - _normalSprintSpeed) * _inverseSprintScaleDivisor);
    }

    private void ApplyAnimatorParameters()
    {
        _animator.SetFloat(Speed, _currentState.Direction.z);
        _animator.SetFloat(MotionScale, _currentState.MotionScale);

        _animator.SetBool(Walking, _currentState.IsWalking);
        _animator.SetBool(Running, _currentState.IsRunning);

        if (_currentState.RollTriggered)
            _animator.SetTrigger(Roll);

        ResetTriggers();
    }

    void ResetTriggers() => _currentState.RollTriggered = false;

    public void OnRollStart() => _currentState.RollTriggered = true;
}