using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerAnimator animationController;
    [SerializeField] private CharacterController controller;
    [Header("Settings")]
    [SerializeField] private float walkSpeed = 3f;
    [SerializeField] private float runSpeed = 6f;
    [SerializeField] private float rotationSpeed = 10f;
    [SerializeField] private float gravityMultiplier = 2f;
    private Transform _transform;
    private Transform _cameraTransform;
    private Camera playerCamera;
    private Vector3 _velocity;
    private Vector2 _movementInput;
    private float _currentSpeed;
    private bool _rollPressed;
    private Vector3 _cameraForward;
    private Vector3 _cameraRight;
    private Quaternion _targetRotation;
    private Vector3 _lastPosition;
    private bool _hasValidCamera;
    private float _deltaTime;
    private InputAction _moveAction;
    private InputAction _runAction;
    private InputAction _rollAction;
    private const float GROUNDED_VELOCITY_Y = -2f;
    private const float INPUT_DEADZONE_SQR = 0.0001f;
    private readonly Vector3 _upVector = Vector3.up;
    private readonly Vector3 _zeroVector = Vector3.zero;

    public bool IsGrounded => controller.isGrounded;
    public bool IsMoving
    {
        get
        {
            var currentPos = _transform.position;
            var displacement = new Vector3(currentPos.x - _lastPosition.x, 0f, currentPos.z - _lastPosition.z);
            var moving = displacement.sqrMagnitude > 0.0001f;
            _lastPosition = currentPos;
            return moving;
        }
    }
    public bool IsCanMove { get; private set; } = true;
    public bool IsRunning { get; private set; }

    private void Start()
    {
        InitializeComponents();
        CacheReferences();
    }

    private void InitializeComponents()
    {
        Cursor.lockState = CursorLockMode.Locked;
        playerCamera = Camera.main;
    }

    private void CacheReferences()
    {
        _transform = controller.transform;
        _lastPosition = _transform.position;
        _cameraTransform = playerCamera != null ? playerCamera.transform : null;
        _hasValidCamera = _cameraTransform != null;

        var actions = InputSystem.actions;
        if (actions != null)
        {
            _moveAction = actions.FindAction("Move", true);
            _runAction = actions.FindAction("Sprint", true);
            _rollAction = actions.FindAction("Roll", true);
        }
    }

    private void Update()
    {
        if (!IsCanMove) return;

        _deltaTime = Time.deltaTime;

        GetInput();
        HandleRoll();
        HandleMovementAndGravity();
    }

    private void GetInput()
    {
        if (_moveAction?.enabled == true)
        {
            _movementInput = _moveAction.ReadValue<Vector2>();

            var inputSqrMag = _movementInput.sqrMagnitude;
            if (inputSqrMag > 1f)
                _movementInput *= FastInverseSqrt(inputSqrMag);
        }
        else
            _movementInput = Vector2.zero;

        IsRunning = _runAction?.IsPressed() == true;
        _rollPressed = _rollAction?.WasPressedThisFrame() == true;
    }

    private void HandleRoll()
    {
        if (!_rollPressed) return;

        animationController.OnRollStart();
    }

    private void HandleMovementAndGravity()
    {
        HandleGravity();

        if (_movementInput.sqrMagnitude < INPUT_DEADZONE_SQR)
        {
            _currentSpeed = 0f;
            return;
        }

        _currentSpeed = IsRunning ? runSpeed : walkSpeed;

        var moveDirection = CalculateMovementDirection();
        if (moveDirection != _zeroVector)
        {
            var horizontalMovement = moveDirection * _currentSpeed;
            var finalMovement = (horizontalMovement + new Vector3(0f, _velocity.y, 0f)) * _deltaTime;

            controller.Move(finalMovement);
            RotateTowardsMovement(moveDirection);
        }
        else
            controller.Move(new Vector3(0f, _velocity.y * _deltaTime, 0f));
    }

    private Vector3 CalculateMovementDirection()
    {
        if (!_hasValidCamera) return _zeroVector;

        _cameraForward = _cameraTransform.forward;
        _cameraRight = _cameraTransform.right;

        _cameraForward.y = 0f;
        _cameraRight.y = 0f;

        var forwardSqrMag = _cameraForward.sqrMagnitude;
        var rightSqrMag = _cameraRight.sqrMagnitude;

        if (forwardSqrMag > 1.01f)
            _cameraForward *= FastInverseSqrt(forwardSqrMag);
        if (rightSqrMag > 1.01f)
            _cameraRight *= FastInverseSqrt(rightSqrMag);

        var direction = _cameraForward * _movementInput.y + _cameraRight * _movementInput.x;
        var directionSqrMag = direction.sqrMagnitude;

        return directionSqrMag > 1.01f ? direction * FastInverseSqrt(directionSqrMag) : direction;
    }

    private void RotateTowardsMovement(Vector3 moveDirection)
    {
        if (moveDirection.sqrMagnitude < INPUT_DEADZONE_SQR) return;

        _targetRotation = Quaternion.LookRotation(moveDirection, _upVector);
        _transform.rotation = Quaternion.Lerp(_transform.rotation, _targetRotation, rotationSpeed * _deltaTime);
    }

    private void HandleGravity()
    {
        if (IsGrounded && _velocity.y < 0f)
            _velocity.y = GROUNDED_VELOCITY_Y;
        else
            _velocity.y += Physics.gravity.y * gravityMultiplier * _deltaTime;
    }

    private float FastInverseSqrt(float x)
    {
        if (x < 0.0001f) return 0f;

        var xhalf = 0.5f * x;
        var i = BitConverter.SingleToInt32Bits(x);
        i = 0x5f3759df - (i >> 1);
        x = BitConverter.Int32BitsToSingle(i);
        x *= 1.5f - xhalf * x * x;
        return x;
    }
}