using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerAnimation))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float walkSpeed = 3f;
    [SerializeField] private float runSpeed = 6f;
    [SerializeField] private float jumpHeight = 1.5f;
    [SerializeField] private float rotationSpeed = 10f;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundDistance = 0.4f;

    [Header("References")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private PlayerAnimation animationController;
    [SerializeField] private CharacterController controller;
    
    private bool isRolling = false;
    private Coroutine currentRollCoroutine;
    private Transform _transform;
    private Vector3 velocity;
    private Vector3 cameraForward;
    private Vector3 cameraRight;
    private Vector2 movementInput;
    private bool jumpPressed;
    private bool rollPressed;
    private float jumpVelocity;
    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction runAction;
    private InputAction rollAction;
    private const float GROUNDED_VELOCITY_Y = -2f;
    private static readonly WaitForSeconds _waitForSecondsRollRoutine = new(0.5f);

    public Vector3 Velocity => controller.velocity;
    public Vector2 MovementInput => movementInput;
    public bool IsEnabled { get; private set; } = true;
    public bool IsGrounded { get; private set; }
    public bool IsRunning { get; private set; }
    public float CurrentSpeed { get; private set; }

    private void Start()
    {
        controller = GetComponent<CharacterController>();
        if (animationController == null)
            animationController = GetComponent<PlayerAnimation>();
        if (playerCamera == null)
            playerCamera = Camera.main;

        _transform = transform;
        jumpVelocity = Mathf.Sqrt(jumpHeight * -2f * Physics.gravity.y);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        moveAction = InputSystem.actions.FindAction("Move");
        jumpAction = InputSystem.actions.FindAction("Jump");
        runAction = InputSystem.actions.FindAction("Sprint");
        rollAction = InputSystem.actions.FindAction("Roll");
    }

    private void Update() => GetInput();

    private void GetInput()
    {
        if (!IsEnabled) return;

        movementInput.x = moveAction.ReadValue<Vector2>().x;
        movementInput.y = moveAction.ReadValue<Vector2>().y;
        if (movementInput.sqrMagnitude > 1f)
            movementInput.Normalize();
        IsRunning = runAction.IsPressed();
        jumpPressed = jumpAction.WasPressedThisFrame();
        rollPressed = rollAction.WasPressedThisFrame();
    }

    private void FixedUpdate()
    {
        HandleGroundCheck();
        HandleMovement();
        HandleJumpAndGravity();
        HandleRoll();
    }

    private void HandleGroundCheck() => IsGrounded = controller.isGrounded;

    private void HandleMovement()
    {
        if (movementInput.sqrMagnitude < 0.01f)
        {
            CurrentSpeed = 0f;
            return;
        }

        CurrentSpeed = IsRunning ? runSpeed : walkSpeed;

        CacheCameraVectors();

        var moveDirection = (cameraForward * movementInput.y + cameraRight * movementInput.x).normalized;
        if (moveDirection != Vector3.zero)
        {
            controller.Move(CurrentSpeed * Time.deltaTime * moveDirection);
            RotateTowardsMovement(moveDirection);
        }
    }

    private void CacheCameraVectors()
    {
        if (playerCamera != null)
        {
            cameraForward = playerCamera.transform.forward;
            cameraRight = playerCamera.transform.right;

            cameraForward.y = 0f;
            cameraRight.y = 0f;

            cameraForward.Normalize();
            cameraRight.Normalize();
        }
        else
        {
            cameraForward = Vector3.forward;
            cameraRight = Vector3.right;
        }
    }

    private void RotateTowardsMovement(Vector3 moveDirection)
    {
        var targetRotation = Quaternion.LookRotation(moveDirection);
        _transform.rotation = Quaternion.Slerp(_transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
    }

    private void HandleJumpAndGravity()
    {
        if (IsGrounded && velocity.y < 0f)
            velocity.y = GROUNDED_VELOCITY_Y;

        if (jumpPressed && IsGrounded)
            velocity.y = jumpVelocity;

        velocity.y += Physics.gravity.y * Time.deltaTime;

        controller.Move(velocity * Time.deltaTime);
    }


    private void HandleRoll()
    {
        if (!rollPressed || isRolling) return;

        if (currentRollCoroutine != null)
            StopCoroutine(currentRollCoroutine);

        currentRollCoroutine = StartCoroutine(RollRoutine());
    }

    private IEnumerator RollRoutine()
    {
        isRolling = true;

        try
        {
            animationController.Roll();
        }
        catch (Exception e)
        {
            Debug.LogError($"Error during Roll(): {e.Message}");
            isRolling = false;
            yield break;
        }

        yield return _waitForSecondsRollRoutine;

        try
        {
            animationController.OnRollEnd();
        }
        catch (Exception e)
        {
            Debug.LogError($"Error during OnRollEnd(): {e.Message}");
        }
        finally
        {
            isRolling = false;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;

        Gizmos.color = IsGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(groundCheck.position, groundDistance);
    }

    private void OnDisable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void SetMovementEnabled(bool enabled) => IsEnabled = enabled;

    public void AddKnockback(Vector3 force)
    {
        velocity.x += force.x;
        velocity.y += force.y;
        velocity.z += force.z;
    }
}