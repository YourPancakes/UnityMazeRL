using UnityEngine;

[RequireComponent(typeof(Camera))]
public class CameraController : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float dragSensitivity = 1f;
    [SerializeField] private float smoothTime = 0.1f;

    [Header("Zoom Settings")]
    [SerializeField] private float zoomStep = 1f;

    private Camera cam;
    private Vector3 lastMousePosition;
    private Vector3 velocity = Vector3.zero;
    private Vector3 targetPosition;
    private bool isDragging;

    private void Awake()
    {
        cam = Camera.main;
        targetPosition = cam.transform.position;
    }

    private void LateUpdate()
    {
        HandleMouseDrag();
        HandleZoom();
    }

    private void HandleMouseDrag()
    {
        if (Input.GetMouseButtonDown(0))
        {
            isDragging = true;
            lastMousePosition = Input.mousePosition;
        }
        else if (Input.GetMouseButtonUp(0))
        {
            isDragging = false;
        }

        if (!isDragging)
            return;

        Vector3 mouseDelta = Input.mousePosition - lastMousePosition;

        Vector3 movement = new Vector3(mouseDelta.x, mouseDelta.y, 0f);
        movement *= cam.orthographicSize / Screen.height;
        movement *= dragSensitivity;

        targetPosition -= movement;
        lastMousePosition = Input.mousePosition;

        cam.transform.position = Vector3.SmoothDamp(cam.transform.position, targetPosition, ref velocity, smoothTime);
    }

    private void HandleZoom()
    {
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0f)
        {
            cam.orthographicSize -= scroll * zoomStep;
        }
    }
}
