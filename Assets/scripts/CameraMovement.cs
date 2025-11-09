using UnityEngine;

public class CameraMovement : MonoBehaviour
{
    public float moveSpeed = 5f;
    public float lookSpeed = 2f;

    private float rotationX = 0f;
    private float rotationY = 0f;

    void Start()
    {
        // Lock and hide the cursor for a more immersive experience
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        // Camera Movement (WASD)
        float horizontalInput = Input.GetAxis("Horizontal"); // A/D or Left/Right Arrow
        float verticalInput = Input.GetAxis("Vertical");   // W/S or Up/Down Arrow

        Vector3 moveDirection = transform.right * horizontalInput + transform.forward * verticalInput;
        transform.position += moveDirection * moveSpeed * Time.deltaTime;

        // Camera Look (Mouse)
        float mouseX = Input.GetAxis("Mouse X") * lookSpeed;
        float mouseY = Input.GetAxis("Mouse Y") * lookSpeed;

        rotationY += mouseX;
        rotationX -= mouseY;
        rotationX = Mathf.Clamp(rotationX, -90f, 90f); // Clamp vertical rotation

        transform.rotation = Quaternion.Euler(rotationX, rotationY, 0f);
    }
}