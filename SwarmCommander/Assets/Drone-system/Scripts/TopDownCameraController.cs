using UnityEngine;
using UnityEngine.InputSystem;

public class TopDownCameraController : MonoBehaviour
{
    public const string MoveSpeedPrefsKey = "Settings.CameraMoveSpeed";

    [Header("Move Settings")]
    public float moveSpeed = 20f;
    public float edgeSize = 20f;

    [Header("Zoom Settings")]
    public float zoomSpeed = 50f;
    public float minHeight = 20f;
    public float maxHeight = 80f;

    void Awake()
    {
        if (PlayerPrefs.HasKey(MoveSpeedPrefsKey))
            moveSpeed = PlayerPrefs.GetFloat(MoveSpeedPrefsKey, moveSpeed);
    }

    public void SetMoveSpeed(float value)
    {
        moveSpeed = Mathf.Max(0f, value);
        PlayerPrefs.SetFloat(MoveSpeedPrefsKey, moveSpeed);
        PlayerPrefs.Save();
    }

    void Update()
    {
        HandleEdgeMove();
        HandleKeyboardMove();
        HandleZoom();
    }

    void HandleEdgeMove()
    {
        if (Mouse.current == null) return;

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Vector3 moveDir = Vector3.zero;

        if (mousePos.x <= edgeSize)
            moveDir.x = -1f;
        else if (mousePos.x >= Screen.width - edgeSize)
            moveDir.x = 1f;

        if (mousePos.y <= edgeSize)
            moveDir.z = -1f;
        else if (mousePos.y >= Screen.height - edgeSize)
            moveDir.z = 1f;

        transform.position += moveDir.normalized * moveSpeed * Time.deltaTime;
    }

    void HandleKeyboardMove()
    {
        if (Keyboard.current == null) return;

        Vector3 moveDir = Vector3.zero;

        if (Keyboard.current.wKey.isPressed) moveDir.z += 1f;
        if (Keyboard.current.sKey.isPressed) moveDir.z -= 1f;
        if (Keyboard.current.aKey.isPressed) moveDir.x -= 1f;
        if (Keyboard.current.dKey.isPressed) moveDir.x += 1f;

        transform.position += moveDir.normalized * moveSpeed * Time.deltaTime;
    }

    void HandleZoom()
    {
        if (Mouse.current == null) return;

        float scroll = Mouse.current.scroll.ReadValue().y;

        Vector3 pos = transform.position;
        pos.y -= scroll * zoomSpeed * Time.deltaTime;
        pos.y = Mathf.Clamp(pos.y, minHeight, maxHeight);

        transform.position = pos;
    }

    public void FocusOnWorldPosition(Vector3 worldPosition)
    {
        Vector3 pos = transform.position;
        pos.x = worldPosition.x;
        pos.z = worldPosition.z;
        transform.position = pos;
    }
}
