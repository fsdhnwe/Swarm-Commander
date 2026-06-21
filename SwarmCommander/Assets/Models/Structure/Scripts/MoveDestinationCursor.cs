using UnityEngine;

public class MoveDestinationCursor : MonoBehaviour
{
    public enum SurfaceAlignment
    {
        LocalZFacesSurfaceNormal,
        LocalYFacesSurfaceNormal
    }

    [Header("Display")]
    public Transform visualRoot;
    public Renderer[] renderers;
    public SurfaceAlignment surfaceAlignment = SurfaceAlignment.LocalZFacesSurfaceNormal;
    public float heightOffset = 0.05f;

    [Header("Animation")]
    public float startScale = 0.65f;
    public float endScale = 1f;
    public float rotationSpeed = 90f;
    public float scaleInTime = 0.25f;

    private float elapsed;
    private Color[][] originalColors;

    private void Awake()
    {
        if (visualRoot == null)
            visualRoot = transform;

        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);

        CacheOriginalColors();
    }

    public void Show(Vector3 worldPosition, Vector3 surfaceNormal)
    {
        elapsed = 0f;
        gameObject.SetActive(true);

        Vector3 normal = surfaceNormal.sqrMagnitude > 0.0001f ? surfaceNormal.normalized : Vector3.up;
        transform.position = worldPosition + normal * heightOffset;
        transform.rotation = GetSurfaceRotation(normal);
        visualRoot.localScale = Vector3.one * startScale;
        SetAlpha(1f);
    }

    private void Update()
    {
        elapsed += Time.deltaTime;

        float scaleT = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, scaleInTime));
        visualRoot.localScale = Vector3.one * Mathf.Lerp(startScale, endScale, scaleT);
        visualRoot.Rotate(Vector3.forward, rotationSpeed * Time.deltaTime, Space.Self);
    }

    public void Dismiss()
    {
        Destroy(gameObject);
    }

    private Quaternion GetSurfaceRotation(Vector3 normal)
    {
        return surfaceAlignment == SurfaceAlignment.LocalYFacesSurfaceNormal
            ? Quaternion.FromToRotation(Vector3.up, normal)
            : Quaternion.FromToRotation(Vector3.forward, normal);
    }

    private void CacheOriginalColors()
    {
        originalColors = new Color[renderers.Length][];

        for (int i = 0; i < renderers.Length; i++)
        {
            Material[] materials = renderers[i].materials;
            originalColors[i] = new Color[materials.Length];

            for (int j = 0; j < materials.Length; j++)
                originalColors[i][j] = materials[j].color;
        }
    }

    private void SetAlpha(float alpha)
    {
        if (renderers == null || originalColors == null) return;

        for (int i = 0; i < renderers.Length; i++)
        {
            Material[] materials = renderers[i].materials;

            for (int j = 0; j < materials.Length; j++)
            {
                Color color = originalColors[i][j];
                color.a *= alpha;
                materials[j].color = color;
            }
        }
    }
}
