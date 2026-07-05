using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays a vehicle's current speed as plain text, e.g. "72 km/h".
/// </summary>
public class SpeedometerUI : MonoBehaviour
{
    [Tooltip("The vehicle whose speed this readout displays.")]
    public VehicleDriveTrain driveTrain;
    [Tooltip("The UI Text element the speed value is written to.")]
    public Text speedText;
    [Tooltip("Decimal places shown after the speed number - 0 for a whole-number readout like most speedometers.")]
    public int decimalPlaces = 0;

    void Update()
    {
        if (driveTrain == null || speedText == null) return;

        string format = "F" + Mathf.Max(0, decimalPlaces);
        speedText.text = driveTrain.SpeedKmh.ToString(format) + " km/h";
    }
}
