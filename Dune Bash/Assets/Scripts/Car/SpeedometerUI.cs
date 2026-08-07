using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays a vehicle's current speed as plain text, e.g. "72 km/h". Works with any IVehicleDriveTrain -
/// VehicleDriveTrain or KartDriveTrain. Exposed as a plain MonoBehaviour field rather than the interface
/// directly, same reasoning as PlayerVehicleInput.driveTrain: Unity's default Inspector can't render a
/// bare interface-typed field as an assignable slot at all, so drag any component implementing
/// IVehicleDriveTrain onto Drive Train and it's cast where actually used.
/// </summary>
public class SpeedometerUI : MonoBehaviour
{
    [Tooltip("Any component implementing IVehicleDriveTrain (VehicleDriveTrain or KartDriveTrain) - the vehicle whose speed this readout displays.")]
    public MonoBehaviour driveTrain;
    [Tooltip("The UI Text element the speed value is written to.")]
    public Text speedText;
    [Tooltip("Decimal places shown after the speed number - 0 for a whole-number readout like most speedometers.")]
    public int decimalPlaces = 0;

    IVehicleDriveTrain Drive => driveTrain as IVehicleDriveTrain;

    void Awake()
    {
        if (driveTrain != null && Drive == null)
            Debug.LogError($"{name}: SpeedometerUI.driveTrain is a {driveTrain.GetType().Name}, which doesn't implement IVehicleDriveTrain.", this);
    }

    void Update()
    {
        IVehicleDriveTrain drive = Drive;
        if (drive == null || speedText == null) return;

        string format = "F" + Mathf.Max(0, decimalPlaces);
        speedText.text = drive.SpeedKmh.ToString(format) + " km/h";
    }
}
