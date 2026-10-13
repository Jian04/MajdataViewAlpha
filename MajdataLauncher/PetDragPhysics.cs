namespace MajdataLauncher;

// A damped pendulum hanging from the cursor. Constant velocity produces no
// force; acceleration makes the lower body lag, braking makes it swing ahead.
internal sealed class PetDragPhysics
{
    private double requestedVelocity, filteredVelocity, angularVelocity;
    internal double Angle { get; private set; }

    internal void SetVelocity(double velocity) => requestedVelocity = Math.Clamp(velocity, -2200, 2200);

    internal double Step(double elapsed)
    {
        // Small integration steps keep the response stable across render rates.
        int steps = Math.Max(1, (int)Math.Ceiling(elapsed / .008));
        double dt = elapsed / steps;
        for (int i = 0; i < steps; i++)
        {
            double nextVelocity = filteredVelocity + (requestedVelocity - filteredVelocity) * (1 - Math.Exp(-dt / .045));
            double acceleration = (nextVelocity - filteredVelocity) / dt;
            filteredVelocity = nextVelocity;
            // Positive WPF rotation around the upper pivot moves the feet left.
            angularVelocity += (acceleration * .36 - Angle * 100 - angularVelocity * 12) * dt;
            angularVelocity = Math.Clamp(angularVelocity, -300, 300);
            Angle += angularVelocity * dt;
            if (Math.Abs(Angle) > 35)
            {
                Angle = Math.Clamp(Angle, -35, 35);
                if (Math.Sign(angularVelocity) == Math.Sign(Angle)) angularVelocity = 0;
            }
        }
        return Angle;
    }

    internal void Reset() { requestedVelocity = filteredVelocity = angularVelocity = Angle = 0; }
}
