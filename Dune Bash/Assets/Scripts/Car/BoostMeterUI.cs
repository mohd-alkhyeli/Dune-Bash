using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives a filled UI Image's fill amount from a vehicle's remaining boost fuel (0-1).
/// </summary>
public class BoostMeterUI : MonoBehaviour
{
    [Tooltip("The vehicle whose boost fuel this bar displays.")]
    public VehicleDriveTrain driveTrain;
    [Tooltip("UI Image with Image Type set to Filled - its Fill Amount is driven by remaining boost fuel (0 = empty, 1 = full).")]
    public Image fillImage;

    void Update()
    {
        if (driveTrain == null || fillImage == null) return;
        fillImage.fillAmount = driveTrain.BoostFuel01;
    }
}
