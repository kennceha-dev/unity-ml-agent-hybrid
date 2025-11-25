using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class Player : MonoBehaviour
{
    CharacterController characterController;

    [SerializeField] private float moveSpeed = 10f;
    [SerializeField] private float lookSpeed = 0.5f;
    [SerializeField] private float jumpHeight = 1.2f;
    [SerializeField] private float jumpTimeToApex = 0.28f;
    [SerializeField] private float fallGravityMultiplier = 2.5f;
    [SerializeField] private float groundedGravity = -5f;
    [SerializeField] private float terminalVelocity = -45f;

    private float rotationX = 0f;
    private float rotationY = 0f;
    private float verticalVelocity = 0f;
    private float gravity;
    private float initialJumpVelocity;

    void Start()
    {
        characterController = GetComponent<CharacterController>();
        RecalculateJumpValues();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void OnValidate()
    {
        RecalculateJumpValues();
    }

    void Update()
    {
        float mouseX = InputSystem.actions.FindAction("Look").ReadValue<Vector2>().x * lookSpeed;
        float mouseY = InputSystem.actions.FindAction("Look").ReadValue<Vector2>().y * lookSpeed;

        rotationY += mouseX;
        rotationX -= mouseY;
        rotationX = Mathf.Clamp(rotationX, -90f, 90f);

        transform.rotation = Quaternion.Euler(rotationX, rotationY, 0f);

        float moveX = InputSystem.actions.FindAction("Move").ReadValue<Vector2>().x;
        float moveZ = InputSystem.actions.FindAction("Move").ReadValue<Vector2>().y;

        Vector3 move = transform.right * moveX + transform.forward * moveZ;
        move *= moveSpeed;

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
}
