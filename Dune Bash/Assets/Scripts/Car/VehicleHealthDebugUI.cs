using UnityEngine;

/// <summary>
/// TEMPORARY debug overlay - draws this car's current health as a floating number above it every
/// frame via OnGUI, projected from its world position. No Canvas/TextMeshPro setup needed since it's
/// throwaway: delete this component (and this script) once a real damage UI exists.
/// </summary>
[RequireComponent(typeof(VehicleHealth))]
public class VehicleHealthDebugUI : MonoBehaviour
{
    [Tooltip("World-space height (m) above the car's pivot the label is projected from.")]
    public float heightOffset = 2.5f;
    [Tooltip("Text size in pixels.")]
    public int fontSize = 18;

    VehicleHealth health;
    static GUIStyle style;

    void Awake()
    {
        health = GetComponent<VehicleHealth>();
    }

    void OnGUI()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 worldPos = transform.position + Vector3.up * heightOffset;
        Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
        if (screenPos.z < 0f) return; // behind the camera

        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label);
            style.alignment = TextAnchor.MiddleCenter;
            style.fontStyle = FontStyle.Bold;
        }
        style.fontSize = fontSize;

        // GUI space has Y flipped relative to screen space (0 at top, not bottom).
        float guiY = Screen.height - screenPos.y;
        Rect rect = new Rect(screenPos.x - 40f, guiY - 10f, 80f, 20f);
        string text = Mathf.RoundToInt(health.Health).ToString();
        Color healthColor = Color.Lerp(Color.red, Color.green, health.HealthFraction);

        // Drawn twice - a black offset copy behind the colored one - so the number stays legible over bright sand.
        Rect shadowRect = new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height);
        style.normal.textColor = Color.black;
        GUI.Label(shadowRect, text, style);

        style.normal.textColor = healthColor;
        GUI.Label(rect, text, style);
    }
}
