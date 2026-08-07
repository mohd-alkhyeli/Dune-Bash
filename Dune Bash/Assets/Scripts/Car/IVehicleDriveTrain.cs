/// <summary>
/// What AIVehicleInput (and anything else driving a vehicle generically) actually needs from a
/// drivetrain: control inputs to set, a couple of readouts to react to. VehicleDriveTrain (full
/// sand-buggy physics - grip/drag tuning, boost, handbrake drift, air control) and KartDriveTrain
/// (minimal test rig, no sand simulation) both implement this, so the same AI logic can drive either
/// without caring which - the point of having two is to isolate whether a driving problem comes from
/// the AI itself or from the sand-specific physics, by swapping the drivetrain underneath it unchanged.
/// </summary>
public interface IVehicleDriveTrain
{
    float ThrottleInput { get; set; }
    float SteerInput { get; set; }
    bool BoostInput { get; set; }
    bool HandbrakeInput { get; set; }
    float SpeedKmh { get; }
    bool IsGrounded { get; }
}
