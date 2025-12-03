using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterMovement))]
[RequireComponent(typeof(SpeedModifierHandler))]
public class Player : MonoBehaviour, ISpeedModifiable
{
    private CharacterMovement characterMovement;
    private SpeedModifierHandler speedModifierHandler;
    private bool isReady;

    [SerializeField] private float lookSpeed = 0.5f;
    [SerializeField] private Transform lookPivot;

    private float rotationX = 0f;
    private float rotationY = 0f;

    void Start()
    {
        characterMovement = GetComponent<CharacterMovement>();
        speedModifierHandler = GetComponent<SpeedModifierHandler>();

        // Sync speed multiplier changes to the movement component
        speedModifierHandler.OnSpeedMultiplierChanged += (multiplier) =>
        {
            characterMovement.CurrentSpeedMultiplier = multiplier;
        };

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        DungeonRunner.OnDungeonReady += () => isReady = true;

        if (lookPivot == null && Camera.main != null)
        {
            lookPivot = Camera.main.transform;
        }
    }

    void Update()
    {
        if (!isReady) return;

        // Handle look input
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

        // Handle movement input
        float moveX = InputSystem.actions.FindAction("Move").ReadValue<Vector2>().x;
        float moveZ = InputSystem.actions.FindAction("Move").ReadValue<Vector2>().y;

        Vector3 move = transform.right * moveX + transform.forward * moveZ;

        // Handle jump
        bool jumpRequested = InputSystem.actions.FindAction("Jump").WasPressedThisFrame();
        characterMovement.ProcessVerticalMovement(jumpRequested);

        // Apply movement
        characterMovement.Move(move);
    }

    public void ApplySpeedMultiplier(Object source, float multiplier)
    {
        speedModifierHandler.ApplySpeedMultiplier(source, multiplier);
    }

    public void RemoveSpeedMultiplier(Object source)
    {
        speedModifierHandler.RemoveSpeedMultiplier(source);
    }
}
