using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private PlayerAnimator animationController;
    [SerializeField] private CharacterController controller;

    [Header("Movement Settings")]
    [SerializeField] private float walkSpeed = 3f;
    [SerializeField] private float runSpeed = 6f;
    [SerializeField] private float rotationSpeed = 10f;
    [SerializeField] private float gravityMultiplier = 2f;

    [Header("Roll Settings")]
    [SerializeField] private AnimationCurve rollMovementCurve;

    private Transform _transform;
    private Transform _cameraTransform;
    private Vector3 _velocity;
    private float _currentSpeed;
    private Vector2 _movementInput;
    private bool _rollPressed;

    private const float GROUNDED_VELOCITY_Y = -2f;
    private const float INPUT_DEADZONE = 0.01f;

    private InputAction _moveAction;
    private InputAction _runAction;
    private InputAction _rollAction;

    public bool IsCanMove { get; private set; } = true;
    public bool IsGrounded => controller.isGrounded;
    public bool IsRunning { get; private set; }

    private void Start()
    {
        InitializeComponents();
        CacheReferences();
        SetupRollCurve();
    }

    private void InitializeComponents()
    {
        Cursor.lockState = CursorLockMode.Locked;
        playerCamera = Camera.main;
    }

    private void CacheReferences()
    {
        _transform = controller.transform;
        _cameraTransform = playerCamera != null ? playerCamera.transform : null;

        try
        {
            _moveAction = InputSystem.actions.FindAction("Move");
            _runAction = InputSystem.actions.FindAction("Sprint");
            _rollAction = InputSystem.actions.FindAction("Roll");
        }
        catch (Exception)
        {
        }
    }

    private void SetupRollCurve()
    => rollMovementCurve ??= new AnimationCurve(
        new Keyframe(0.0f, 0.8f),
        new Keyframe(0.3f, 1.0f),
        new Keyframe(0.7f, 0.5f),
        new Keyframe(1.0f, 0.2f)
    );

    private void Update()
    {
        if (!IsCanMove) return;

        GetInput();
        HandleRoll();
        HandleMovement();
        HandleGravity();
    }

    private void GetInput()
    {
        _movementInput = _moveAction.ReadValue<Vector2>();
        if (_movementInput.sqrMagnitude > 1f)
            _movementInput.Normalize();

        IsRunning = _runAction.IsPressed();

        _rollPressed = _rollAction.WasPressedThisFrame();
    }

    private void HandleRoll()
    {
        if (!_rollPressed) return;

        animationController.OnRollStart();
    }

    private void HandleMovement()
    {
        if (_movementInput.sqrMagnitude < INPUT_DEADZONE)
        {
            _currentSpeed = 0f;
            return;
        }

        _currentSpeed = IsRunning ? runSpeed : walkSpeed;

        var moveDirection = CalculateMovementDirection();
        if (moveDirection != Vector3.zero)
        {
            controller.Move(_currentSpeed * Time.deltaTime * moveDirection);
            RotateTowardsMovement(moveDirection);
        }
    }

    private Vector3 CalculateMovementDirection()
    {
        if (_cameraTransform == null) return Vector3.zero;

        var forward = _cameraTransform.forward;
        var right = _cameraTransform.right;

        forward.y = 0f;
        right.y = 0f;

        if (forward.sqrMagnitude > 1.01f) forward.Normalize();
        if (right.sqrMagnitude > 1.01f) right.Normalize();

        return (forward * _movementInput.y + right * _movementInput.x).normalized;
    }

    private void RotateTowardsMovement(Vector3 moveDirection)
    {
        if (moveDirection.sqrMagnitude < INPUT_DEADZONE) return;

        var targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
        _transform.rotation = Quaternion.Slerp(
            _transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
    }

    private void HandleGravity()
    {
        if (IsGrounded && _velocity.y < 0f)
            _velocity.y = GROUNDED_VELOCITY_Y;
        _velocity.y += Physics.gravity.y * gravityMultiplier * Time.deltaTime;

        controller.Move(new Vector3(0f, _velocity.y * Time.deltaTime, 0f));
    }
}