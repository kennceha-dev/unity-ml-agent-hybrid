using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(NavMeshAgent))]
public class Player : MonoBehaviour, ISpeedModifiable
{
    CharacterController characterController;
    private bool isReady;

    [SerializeField] private float moveSpeed = 10f;
    [SerializeField] private float lookSpeed = 0.5f;
    [SerializeField] private float jumpHeight = 1.2f;
    [SerializeField] private float jumpTimeToApex = 0.28f;
    [SerializeField] private float fallGravityMultiplier = 2.5f;
    [SerializeField] private float groundedGravity = -5f;
    [SerializeField] private float terminalVelocity = -45f;

    [SerializeField] private Transform lookPivot;

    [Header("NavMesh Settings")]
    [Tooltip("Enable NavMesh-based movement in MovingTarget training phase")]
    [SerializeField] private bool enableTrainingMode = true;

    private NavMeshAgent agent;
    private float rotationX = 0f;
    private float rotationY = 0f;
    private float verticalVelocity = 0f;
    private float gravity;
    private float initialJumpVelocity;
    private float currentSpeedMultiplier = 1f;
    private readonly Dictionary<Object, float> speedModifiers = new Dictionary<Object, float>();

    private Transform exitTarget;
    private bool useNavMeshMovement;
    private bool destinationSet;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        characterController = GetComponent<CharacterController>();
        RecalculateJumpValues();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        DungeonRunner.OnDungeonReady += OnDungeonReady;

        if (lookPivot == null && Camera.main != null)
        {
            lookPivot = Camera.main.transform;
        }

        // Configure NavMeshAgent
        ConfigureNavMeshAgent();
    }

    private void OnDestroy()
    {
        DungeonRunner.OnDungeonReady -= OnDungeonReady;
    }

    private void OnDungeonReady()
    {
        // Reset NavMeshAgent state before updating movement mode
        ResetNavMeshAgent();
        isReady = true;
        UpdateMovementMode();
        FindAndSetExitTarget();
    }

    /// <summary>
    /// Properly reset the NavMeshAgent after dungeon regeneration.
    /// This ensures the agent is properly synced to the new NavMesh.
    /// </summary>
    private void ResetNavMeshAgent()
    {
        if (agent == null) return;

        // Disable the agent first to clear any stale state
        agent.enabled = false;
        destinationSet = false;

        // Re-enable if we're in NavMesh movement mode
        if (enableTrainingMode && GameManager.Instance != null &&
            GameManager.Instance.CurrentTrainingPhase == TrainingPhase.MovingTarget)
        {
            agent.enabled = true;

            // Try to warp to a valid NavMesh position
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
            }
            else
            {
                // If we can't find a valid position, disable the agent
                Debug.LogWarning("[Player] Could not find valid NavMesh position after dungeon reset");
                agent.enabled = false;
            }
        }
    }

    private void ConfigureNavMeshAgent()
    {
        if (agent == null) return;

        // Use NavMeshAgent only for path data, not for movement
        // Movement is handled by CharacterController (like Agent.cs)
        agent.updateRotation = false;
        agent.updatePosition = false;
        agent.enabled = false; // Disabled by default, enabled only in MovingTarget phase
    }

    private void UpdateMovementMode()
    {
        if (GameManager.Instance == null)
        {
            useNavMeshMovement = false;
            return;
        }

        bool shouldUseNavMesh = enableTrainingMode &&
                                GameManager.Instance.CurrentTrainingPhase == TrainingPhase.MovingTarget;

        if (useNavMeshMovement != shouldUseNavMesh)
        {
            useNavMeshMovement = shouldUseNavMesh;
            SetMovementMode(useNavMeshMovement);
        }
    }

    private void SetMovementMode(bool useNavMesh)
    {
        if (agent == null || characterController == null) return;

        if (useNavMesh)
        {
            // Enable NavMeshAgent for path calculation only
            // CharacterController remains enabled for actual movement
            agent.enabled = true;
            agent.updateRotation = false;
            agent.updatePosition = false;
            characterController.enabled = true;
            destinationSet = false; // Reset so path is calculated fresh

            // Sync NavMeshAgent position and set destination
            if (agent.isOnNavMesh)
            {
                agent.Warp(transform.position);
                if (exitTarget != null)
                {
                    agent.SetDestination(exitTarget.position);
                    destinationSet = true;
                }
            }
        }
        else
        {
            // Switch to manual CharacterController control
            agent.enabled = false;
            characterController.enabled = true;
            destinationSet = false;
        }
    }

    private void FindAndSetExitTarget()
    {
        if (GameManager.Instance == null) return;

        string exitTag = GameManager.Instance.ExitTag;
        GameObject exitObject = GameObject.FindGameObjectWithTag(exitTag);

        if (exitObject != null)
        {
            exitTarget = exitObject.transform;
            destinationSet = false; // Reset so path is recalculated

            if (useNavMeshMovement && agent != null && agent.enabled && agent.isOnNavMesh)
            {
                agent.SetDestination(exitTarget.position);
                destinationSet = true;
            }
        }
        else
        {
            Debug.LogWarning("[Player] Exit not found with tag: " + exitTag);
        }
    }

    /// <summary>
    /// Refresh the exit target and navigation. Call this when dungeon is regenerated.
    /// </summary>
    public void RefreshNavigation()
    {
        ResetNavMeshAgent();
        UpdateMovementMode();
        FindAndSetExitTarget();
    }

    void OnValidate()
    {
        RecalculateJumpValues();
    }

    void Update()
    {
        if (!isReady) return;

        // Check if movement mode needs to change (phase might have changed)
        UpdateMovementMode();

        if (useNavMeshMovement)
        {
            HandleNavMeshMovement();
        }
        else
        {
            HandleManualMovement();
        }
    }

    private void HandleNavMeshMovement()
    {
        if (agent == null || !agent.enabled || exitTarget == null) return;

        // Ensure we're on the NavMesh and have a destination set
        if (!agent.isOnNavMesh)
        {
            // Try to warp to a valid position on NavMesh
            if (UnityEngine.AI.NavMesh.SamplePosition(transform.position, out UnityEngine.AI.NavMeshHit hit, 2f, UnityEngine.AI.NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
            }
            return;
        }

        // Set destination only once (or when target changes)
        if (!destinationSet || !agent.hasPath)
        {
            agent.SetDestination(exitTarget.position);
            destinationSet = true;
        }

        // Wait for path to be calculated
        if (agent.pathPending)
        {
            return;
        }

        // Check if path is valid
        if (agent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid)
        {
            Debug.LogWarning("[Player] Invalid path to exit");
            destinationSet = false; // Try again next frame
            return;
        }

        // Get steering target from NavMesh path
        Vector3 steeringTarget = GetSteeringTarget();
        Vector3 directionToTarget = (steeringTarget - transform.position);
        directionToTarget.y = 0f; // Keep movement horizontal

        float distanceToSteeringTarget = directionToTarget.magnitude;

        // Only move if we have somewhere to go
        Vector3 move = Vector3.zero;
        if (distanceToSteeringTarget > 0.1f)
        {
            directionToTarget.Normalize();
            move = directionToTarget * moveSpeed * currentSpeedMultiplier;
        }

        // Handle gravity
        if (characterController.isGrounded)
        {
            verticalVelocity = groundedGravity;
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

        move.y = verticalVelocity;
        characterController.Move(move * Time.deltaTime);

        // Sync NavMeshAgent position AFTER moving (so path updates correctly)
        agent.nextPosition = transform.position;

        // Optionally allow looking around
        float mouseX = InputSystem.actions.FindAction("Look").ReadValue<Vector2>().x * lookSpeed;
        float mouseY = InputSystem.actions.FindAction("Look").ReadValue<Vector2>().y * lookSpeed;

        rotationX -= mouseY;
        rotationX = Mathf.Clamp(rotationX, -90f, 90f);

        if (lookPivot != null)
        {
            lookPivot.localRotation = Quaternion.Euler(rotationX, 0f, 0f);
        }

        // Check if we've reached the exit
        float distanceToExit = Vector3.Distance(transform.position, exitTarget.position);
        if (distanceToExit <= 1.5f)
        {
            OnReachedExit();
        }
    }

    private Vector3 GetSteeringTarget()
    {
        // steeringTarget gives us the next corner in the path
        if (agent != null && agent.hasPath && agent.path.corners.Length > 0)
        {
            // If steeringTarget is very close, look ahead to next corner
            Vector3 steering = agent.steeringTarget;
            float distToSteering = Vector3.Distance(new Vector3(transform.position.x, 0, transform.position.z),
                                                     new Vector3(steering.x, 0, steering.z));

            if (distToSteering < 0.5f && agent.path.corners.Length > 1)
            {
                // Find the next corner after current steering target
                for (int i = 0; i < agent.path.corners.Length - 1; i++)
                {
                    if (Vector3.Distance(agent.path.corners[i], steering) < 0.1f)
                    {
                        return agent.path.corners[i + 1];
                    }
                }
            }

            return steering;
        }
        return exitTarget != null ? exitTarget.position : transform.position;
    }

    private void OnReachedExit()
    {
        // Notify that player reached the exit (for training purposes)
        Debug.Log("[Player] Reached exit target via NavMesh");
    }

    private void HandleManualMovement()
    {
        float mouseX = InputSystem.actions.FindAction("Look").ReadValue<Vector2>().x * lookSpeed;
        float mouseY = InputSystem.actions.FindAction("Look").ReadValue<Vector2>().y * lookSpeed;

        rotationY += mouseX;
        rotationX -= mouseY;
        rotationX = Mathf.Clamp(rotationX, -90f, 90f);

        transform.rotation = Quaternion.Euler(0f, rotationY, 0f);

        if (lookPivot != null)
        {
            lookPivot.localRotation = Quaternion.Euler(rotationX, 0f, 0f);
        }

        float moveX = InputSystem.actions.FindAction("Move").ReadValue<Vector2>().x;
        float moveZ = InputSystem.actions.FindAction("Move").ReadValue<Vector2>().y;

        Vector3 move = transform.right * moveX + transform.forward * moveZ;
        move *= moveSpeed * currentSpeedMultiplier;

        if (characterController.isGrounded)
        {
            verticalVelocity = groundedGravity;

            if (InputSystem.actions.FindAction("Jump").WasPressedThisFrame())
            {
                verticalVelocity = initialJumpVelocity;
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

        move.y = verticalVelocity;
        characterController.Move(move * Time.deltaTime);
    }

    private void RecalculateJumpValues()
    {
        jumpHeight = Mathf.Max(0.1f, jumpHeight);
        jumpTimeToApex = Mathf.Max(0.05f, jumpTimeToApex);
        gravity = 2f * jumpHeight / (jumpTimeToApex * jumpTimeToApex);
        initialJumpVelocity = gravity * jumpTimeToApex;
    }

    public void ApplySpeedMultiplier(Object source, float multiplier)
    {
        if (source == null)
        {
            return;
        }

        speedModifiers[source] = Mathf.Clamp(multiplier, 0.01f, 10f);
        RecalculateSpeedMultiplier();

    }

    public void RemoveSpeedMultiplier(Object source)
    {
        if (source == null)
        {
            return;
        }

        if (speedModifiers.Remove(source))
        {
            RecalculateSpeedMultiplier();
        }
    }

    private void RecalculateSpeedMultiplier()
    {
        currentSpeedMultiplier = 1f;
        foreach (float modifier in speedModifiers.Values)
        {
            currentSpeedMultiplier *= modifier;
        }
        currentSpeedMultiplier = Mathf.Clamp(currentSpeedMultiplier, 0.01f, 10f);
    }
}
