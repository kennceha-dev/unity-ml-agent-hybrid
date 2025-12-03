using UnityEngine;

/// <summary>
/// Handles character movement physics including gravity, jumping, and velocity calculations.
/// Attach to any GameObject with a CharacterController to enable consistent movement behavior.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class CharacterMovement : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 10f;
    [SerializeField] private float rotationSpeed = 180f;

    [Header("Jump Settings")]
    [SerializeField] private float jumpHeight = 1.2f;
    [SerializeField] private float jumpTimeToApex = 0.28f;

    [Header("Gravity Settings")]
    [SerializeField] private float fallGravityMultiplier = 2.5f;
    [SerializeField] private float groundedGravity = -5f;
    [SerializeField] private float terminalVelocity = -45f;

    private CharacterController characterController;
    private float verticalVelocity = 0f;
    private float gravity;
    private float initialJumpVelocity;
    private float currentSpeedMultiplier = 1f;

    public float MoveSpeed => moveSpeed;
    public float RotationSpeed => rotationSpeed;
    public float VerticalVelocity => verticalVelocity;
    public bool IsGrounded => characterController != null && characterController.isGrounded;
    public CharacterController Controller => characterController;

    public float CurrentSpeedMultiplier
    {
        get => currentSpeedMultiplier;
        set => currentSpeedMultiplier = Mathf.Clamp(value, 0.01f, 10f);
    }

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        RecalculateJumpValues();
    }

    private void OnValidate()
    {
        RecalculateJumpValues();
    }

    private void RecalculateJumpValues()
    {
        jumpHeight = Mathf.Max(0.1f, jumpHeight);
        jumpTimeToApex = Mathf.Max(0.05f, jumpTimeToApex);
        gravity = 2f * jumpHeight / (jumpTimeToApex * jumpTimeToApex);
        initialJumpVelocity = gravity * jumpTimeToApex;
    }

    /// <summary>
    /// Applies gravity and processes jump input. Call this every frame/fixed update.
    /// </summary>
    /// <param name="jumpRequested">Whether a jump was requested this frame</param>
    /// <returns>True if a jump was initiated</returns>
    public bool ProcessVerticalMovement(bool jumpRequested)
    {
        bool jumped = false;

        if (characterController.isGrounded)
        {
            verticalVelocity = groundedGravity;

            if (jumpRequested)
            {
                verticalVelocity = initialJumpVelocity;
                jumped = true;
            }
        }
        else
        {
            float appliedGravity = gravity;

            if (verticalVelocity < 0f)
            {
                appliedGravity *= fallGravityMultiplier;
            }

            verticalVelocity -= appliedGravity * Time.deltaTime;
            verticalVelocity = Mathf.Max(verticalVelocity, terminalVelocity);
        }

        return jumped;
    }

    /// <summary>
    /// Applies gravity using fixed delta time. Use in FixedUpdate.
    /// </summary>
    /// <param name="jumpRequested">Whether a jump was requested</param>
    /// <returns>True if a jump was initiated</returns>
    public bool ProcessVerticalMovementFixed(bool jumpRequested)
    {
        bool jumped = false;

        if (characterController.isGrounded)
        {
            verticalVelocity = groundedGravity;

            if (jumpRequested)
            {
                verticalVelocity = initialJumpVelocity;
                jumped = true;
            }
        }
        else
        {
            float appliedGravity = gravity;

            if (verticalVelocity < 0f)
            {
                appliedGravity *= fallGravityMultiplier;
            }

            verticalVelocity -= appliedGravity * Time.fixedDeltaTime;
            verticalVelocity = Mathf.Max(verticalVelocity, terminalVelocity);
        }

        return jumped;
    }

    /// <summary>
    /// Moves the character using the CharacterController.
    /// </summary>
    /// <param name="horizontalMovement">The horizontal movement vector (will have Y overwritten with vertical velocity)</param>
    /// <param name="useFixedDeltaTime">Whether to use fixed delta time (for FixedUpdate)</param>
    public void Move(Vector3 horizontalMovement, bool useFixedDeltaTime = false)
    {
        horizontalMovement *= moveSpeed * currentSpeedMultiplier;
        horizontalMovement.y = verticalVelocity;

        float deltaTime = useFixedDeltaTime ? Time.fixedDeltaTime : Time.deltaTime;
        characterController.Move(horizontalMovement * deltaTime);
    }

    /// <summary>
    /// Rotates the character around the Y axis.
    /// </summary>
    /// <param name="rotationInput">Rotation input (-1 to 1)</param>
    /// <param name="useFixedDeltaTime">Whether to use fixed delta time (for FixedUpdate)</param>
    public void Rotate(float rotationInput, bool useFixedDeltaTime = false)
    {
        float deltaTime = useFixedDeltaTime ? Time.fixedDeltaTime : Time.deltaTime;
        float rotationAmount = rotationInput * rotationSpeed * deltaTime;
        transform.Rotate(0f, rotationAmount, 0f);
    }

    /// <summary>
    /// Resets vertical velocity (useful when respawning or teleporting).
    /// </summary>
    public void ResetVerticalVelocity()
    {
        verticalVelocity = 0f;
    }
}
