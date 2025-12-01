using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
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

    private float rotationX = 0f;
    private float rotationY = 0f;
    private float verticalVelocity = 0f;
    private float gravity;
    private float initialJumpVelocity;
    private float currentSpeedMultiplier = 1f;
    private readonly Dictionary<Object, float> speedModifiers = new Dictionary<Object, float>();

    void Start()
    {
        characterController = GetComponent<CharacterController>();
        RecalculateJumpValues();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        DungeonRunner.OnDungeonReady += () => isReady = true;

        if (lookPivot == null && Camera.main != null)
        {
            lookPivot = Camera.main.transform;
        }
    }

    void OnValidate()
    {
        RecalculateJumpValues();
    }

    void Update()
    {
        if (!isReady) return;
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
