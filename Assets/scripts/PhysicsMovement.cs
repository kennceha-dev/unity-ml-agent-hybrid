using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PhysicsMovement : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 10f;
    [SerializeField] private float acceleration = 50f;
    [SerializeField] private float deceleration = 40f;

    [Header("Jumping")]
    [SerializeField] private float jumpHeight = 1.2f;
    [SerializeField] private float jumpTimeToApex = 0.28f;
    [SerializeField] private float fallGravityMultiplier = 2.5f;
    [SerializeField] private float groundedGravity = -5f;
    [SerializeField] private float terminalVelocity = -45f;

    private CharacterController characterController;
    private Vector3 horizontalVelocity;
    private float verticalVelocity;
    private float gravity;
    private float initialJumpVelocity;
    private float currentSpeedMultiplier = 1f;
    private bool jumpRequested;

    public float MoveSpeed => moveSpeed;
    public float CurrentSpeed => moveSpeed * currentSpeedMultiplier;
    public bool IsGrounded => characterController != null && characterController.isGrounded;
    public Vector3 Velocity => new Vector3(horizontalVelocity.x, verticalVelocity, horizontalVelocity.z);
    public Vector3 HorizontalVelocity => horizontalVelocity;

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
    /// Call this every FixedUpdate or Update to apply movement with acceleration physics.
    /// </summary>
    /// <param name="inputDirection">Normalized direction to move (y component ignored)</param>
    /// <param name="useFixedDelta">True if called from FixedUpdate, false for Update</param>
    public void Move(Vector3 inputDirection, bool useFixedDelta = true)
    {
        float deltaTime = useFixedDelta ? Time.fixedDeltaTime : Time.deltaTime;

        // Flatten input direction
        inputDirection.y = 0f;
        if (inputDirection.magnitude > 1f)
            inputDirection.Normalize();

        // Calculate target velocity
        float targetSpeed = moveSpeed * currentSpeedMultiplier;
        Vector3 targetVelocity = inputDirection * targetSpeed;

        // Apply acceleration/deceleration for slippery physics feel
        float accelRate = inputDirection.magnitude > 0.1f ? acceleration : deceleration;
        horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity, accelRate * deltaTime);

        // Handle gravity and jumping
        if (characterController.isGrounded)
        {
            verticalVelocity = groundedGravity;

            if (jumpRequested)
            {
                verticalVelocity = initialJumpVelocity;
                jumpRequested = false;
            }
        }
        else
        {
            float appliedGravity = verticalVelocity < 0f ? gravity * fallGravityMultiplier : gravity;
            verticalVelocity -= appliedGravity * deltaTime;
            verticalVelocity = Mathf.Max(verticalVelocity, terminalVelocity);
        }

        // Apply final movement
        Vector3 move = new Vector3(horizontalVelocity.x, verticalVelocity, horizontalVelocity.z);
        characterController.Move(move * deltaTime);
    }

    /// <summary>
    /// Request a jump on the next Move() call (only works when grounded).
    /// </summary>
    public void Jump()
    {
        if (characterController.isGrounded)
            jumpRequested = true;
    }

    /// <summary>
    /// Set the speed multiplier (e.g., from sticky floors or buffs).
    /// </summary>
    public void SetSpeedMultiplier(float multiplier)
    {
        currentSpeedMultiplier = Mathf.Clamp(multiplier, 0.01f, 10f);
    }

    /// <summary>
    /// Reset velocity (useful on episode reset or teleport).
    /// </summary>
    public void ResetVelocity()
    {
        horizontalVelocity = Vector3.zero;
        verticalVelocity = 0f;
        jumpRequested = false;
    }
}
