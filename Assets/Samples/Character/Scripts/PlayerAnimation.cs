using UnityEngine;

[RequireComponent(typeof(Animator), typeof(CharacterController), typeof(PlayerMovement))]
public class PlayerAnimation : MonoBehaviour
{
    public enum PlayerState
    {
        Idle,
        Walking,
        Running,
        Jumping,
        Falling,
        Attacking,
        Rolling,
        Damaged
    }

    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private CharacterController controller;
    [SerializeField] private PlayerMovement movement;

    [Header("State Settings")]
    [SerializeField] private float walkThreshold = 0.1f;
    [SerializeField] private float runThreshold = 4f;
    [SerializeField] private float attackAnimationLength = 0.5f;
    [SerializeField] private float fallingThreshold = -0.1f;
    private float stateTimer = 0f;
    private float calculatedSpeed;
    private readonly int moveSpeedHash = Animator.StringToHash("MoveSpeed");
    private readonly int verticalSpeedHash = Animator.StringToHash("VerticalSpeed");
    private readonly int isGroundedHash = Animator.StringToHash("IsGrounded");
    private readonly int isMovingHash = Animator.StringToHash("IsMoving");
    private readonly int isRunningHash = Animator.StringToHash("IsRunning");
    private readonly int jumpHash = Animator.StringToHash("Jump");
    private readonly int attackHash = Animator.StringToHash("Attack");
    private readonly int rollHash = Animator.StringToHash("Roll");
    private readonly int damageHash = Animator.StringToHash("Damage");

    public PlayerState CurrentState { get; private set; } = PlayerState.Idle;
    private bool CanAttack => !IsInUninterruptibleState();
    private bool CanRoll => !IsInUninterruptibleState() && controller.isGrounded;

    private void Start()
    {
        if (animator == null)
            animator = GetComponent<Animator>();
        if (controller == null)
            controller = GetComponent<CharacterController>();
        if (movement == null)
            movement = GetComponent<PlayerMovement>();
    }

    private void Update()
    {
        stateTimer += Time.deltaTime;
        CalculateSpeed();
        HandleStateTransitions();
        UpdateAnimator();
    }

    private void CalculateSpeed()
    => calculatedSpeed = new Vector3(controller.velocity.x, 0f, controller.velocity.z).magnitude;

    private void HandleStateTransitions()
    {
        var isMoving = calculatedSpeed > walkThreshold;
        var isRunning = calculatedSpeed > runThreshold;

        if (IsInUninterruptibleState()) return;

        switch (CurrentState)
        {
            case PlayerState.Idle:
                if (!controller.isGrounded)
                    ChangeState(PlayerState.Jumping);
                else if (isMoving)
                    ChangeState(isRunning ? PlayerState.Running : PlayerState.Walking);
                break;

            case PlayerState.Walking:
                if (!controller.isGrounded)
                    ChangeState(PlayerState.Jumping);
                else if (!isMoving)
                    ChangeState(PlayerState.Idle);
                else if (isRunning)
                    ChangeState(PlayerState.Running);
                break;

            case PlayerState.Running:
                if (!controller.isGrounded)
                    ChangeState(PlayerState.Jumping);
                else if (!isMoving)
                    ChangeState(PlayerState.Idle);
                else if (!isRunning)
                    ChangeState(PlayerState.Walking);
                break;

            case PlayerState.Jumping:
                if (controller.velocity.y < fallingThreshold)
                    ChangeState(PlayerState.Falling);
                else if (controller.isGrounded)
                    ChangeState(GetGroundedState(isMoving, isRunning));
                break;

            case PlayerState.Falling:
                if (controller.isGrounded)
                    ChangeState(GetGroundedState(isMoving, isRunning));
                break;

            case PlayerState.Attacking:
                if (stateTimer > attackAnimationLength)
                    ChangeState(PlayerState.Idle);
                break;

            case PlayerState.Rolling:
                    ChangeState(PlayerState.Falling);
                
                break;
        }
    }

    private bool IsInUninterruptibleState()
    => CurrentState == PlayerState.Attacking ||
        CurrentState == PlayerState.Rolling ||
        CurrentState == PlayerState.Damaged;

    private PlayerState GetGroundedState(bool isMoving, bool isRunning)
    => isMoving ? (isRunning ? PlayerState.Running : PlayerState.Walking) : PlayerState.Idle;

    private void UpdateAnimator()
    {
        animator.SetFloat(moveSpeedHash, calculatedSpeed);
        animator.SetFloat(verticalSpeedHash, controller.velocity.y);
        animator.SetBool(isGroundedHash, controller.isGrounded);
        animator.SetBool(isMovingHash, calculatedSpeed > walkThreshold);
        animator.SetBool(isRunningHash, CurrentState == PlayerState.Running);
    }

    public void Attack()
    {
        if (!CanAttack) return;
        ChangeState(PlayerState.Attacking);
    }

    public void TakeDamage()
    {
        if (CurrentState == PlayerState.Damaged) return;
        ChangeState(PlayerState.Damaged);
    }

    public void OnAttackEnd()
    {
        if (CurrentState != PlayerState.Attacking) return;
        ChangeState(PlayerState.Idle);
    }

    public void Roll()
    {
        if (!CanRoll) return;
        ChangeState(PlayerState.Rolling);
    }

    public void OnRollEnd()
    {
        if (CurrentState != PlayerState.Rolling) return;
        ChangeState(PlayerState.Idle);
    }

    public void OnDamageEnd()
    {
        if (CurrentState != PlayerState.Damaged) return;
        ChangeState(PlayerState.Idle);
    }

    private void ChangeState(PlayerState newState)
    {
        if (CurrentState == newState) return;

        switch (CurrentState)
        {
            case PlayerState.Attacking:
                animator.ResetTrigger(attackHash);
                break;
        }

        var previousState = CurrentState;
        CurrentState = newState;
        stateTimer = 0f;

        switch (newState)
        {
            case PlayerState.Jumping:
                animator.SetTrigger(jumpHash);
                break;
            case PlayerState.Attacking:
                animator.SetTrigger(attackHash);
                break;
            case PlayerState.Rolling:
                animator.SetTrigger(rollHash);
                break;
            case PlayerState.Damaged:
                animator.SetTrigger(damageHash);
                break;
        }

        Debug.Log($"PlayerState changed: {previousState} -> {newState}");
    }
}