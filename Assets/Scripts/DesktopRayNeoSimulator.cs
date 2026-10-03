using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Keyboard and mouse 6DoF stand-in for the RayNeo headset.</summary>
public class DesktopRayNeoSimulator : MonoBehaviour
{
    [Min(0f)] public float moveSpeed = 2.5f;
    [Min(1f)] public float fastMoveMultiplier = 2f;
    [Min(0f)] public float lookSensitivity = 2f;

    private float yaw;
    private float pitch;

    private void Start()
    {
        Vector3 angles = transform.eulerAngles;
        yaw = angles.y;
        pitch = NormalizeAngle(angles.x);

        if (IsDesktopSimulation())
        {
            LockCursor();
        }
    }

    private void Update()
    {
        if (!IsDesktopSimulation())
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (keyboard == null)
        {
            return;
        }

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (mouse != null && mouse.leftButton.wasPressedThisFrame &&
            Cursor.lockState != CursorLockMode.Locked)
        {
            LockCursor();
        }

        if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
        {
            UpdateLook(mouse.delta.ReadValue());
        }

        UpdateMovement(keyboard);
    }

    private void UpdateLook(Vector2 mouseDelta)
    {
        yaw += mouseDelta.x * lookSensitivity * 0.1f;
        pitch -= mouseDelta.y * lookSensitivity * 0.1f;
        pitch = Mathf.Clamp(pitch, -80f, 80f);
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    private void UpdateMovement(Keyboard keyboard)
    {
        Vector3 movement = Vector3.zero;

        if (keyboard.wKey.isPressed) movement += transform.forward;
        if (keyboard.sKey.isPressed) movement -= transform.forward;
        if (keyboard.aKey.isPressed) movement -= transform.right;
        if (keyboard.dKey.isPressed) movement += transform.right;
        if (keyboard.eKey.isPressed) movement += Vector3.up;
        if (keyboard.qKey.isPressed) movement -= Vector3.up;

        float currentSpeed = keyboard.leftShiftKey.isPressed
            ? moveSpeed * fastMoveMultiplier
            : moveSpeed;

        transform.position += movement.normalized * currentSpeed * Time.deltaTime;
    }

    private static bool IsDesktopSimulation()
    {
        return Application.isEditor ||
               Application.platform == RuntimePlatform.WindowsPlayer ||
               Application.platform == RuntimePlatform.OSXPlayer ||
               Application.platform == RuntimePlatform.LinuxPlayer;
    }

    private static float NormalizeAngle(float angle)
    {
        return angle > 180f ? angle - 360f : angle;
    }

    private static void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}
