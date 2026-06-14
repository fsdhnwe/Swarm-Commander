using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Draws the rubber-band selection box on screen.
///
/// Setup:
///   1. Add a Canvas (Screen Space – Overlay, no scaler needed) to the scene.
///   2. Inside the Canvas create an Image GameObject named "SelectionBox".
///   3. Set its color to something semi-transparent (e.g. R:0 G:200 B:255 A:60).
///   4. Optionally give it an Outline component for a visible border.
///   5. Attach THIS script to the same GameObject that has SelectionManager,
///      OR attach it separately and assign the selectionBoxImage reference.
/// </summary>
[RequireComponent(typeof(SelectionManager))]
public class SelectionBoxUI : MonoBehaviour
{
    [Tooltip("The UI Image used as the selection rectangle. Must live inside a Canvas.")]
    public RectTransform selectionBoxRect;

    void Awake()
    {
        if (selectionBoxRect != null)
            selectionBoxRect.gameObject.SetActive(false);
    }

    /// <summary>Update box each frame while dragging.</summary>
    public void UpdateBox(Vector2 screenStart, Vector2 screenEnd)
    {
        if (selectionBoxRect == null) return;

        selectionBoxRect.gameObject.SetActive(true);

        // Convert to a rect with positive width/height
        float x = Mathf.Min(screenStart.x, screenEnd.x);
        float y = Mathf.Min(screenStart.y, screenEnd.y);
        float w = Mathf.Abs(screenStart.x - screenEnd.x);
        float h = Mathf.Abs(screenStart.y - screenEnd.y);

        // RectTransform: pivot at bottom-left
        selectionBoxRect.anchorMin = Vector2.zero;
        selectionBoxRect.anchorMax = Vector2.zero;
        selectionBoxRect.pivot     = Vector2.zero;

        selectionBoxRect.anchoredPosition = new Vector2(x, y);
        selectionBoxRect.sizeDelta        = new Vector2(w, h);
    }

    /// <summary>Hide the box when drag ends.</summary>
    public void Hide()
    {
        if (selectionBoxRect != null)
            selectionBoxRect.gameObject.SetActive(false);
    }
}