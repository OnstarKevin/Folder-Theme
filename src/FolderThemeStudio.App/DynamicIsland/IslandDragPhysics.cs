namespace FolderThemeStudio.App.DynamicIsland;

/// <summary>只改变胶囊外观，不让窗口惯性漂移。小步积分使不同刷新率下的阻尼弹簧稳定。</summary>
public sealed class IslandDragPhysics
{
    private double x, y, angle, vx, vy, va, targetX, targetY, targetAngle;
    public double ScaleX => 1 + x;
    public double ScaleY => 1 + y;
    public double Angle => angle;
    public bool IsResting => Math.Abs(x) + Math.Abs(y) + Math.Abs(angle) + Math.Abs(vx) + Math.Abs(vy) + Math.Abs(va) < .0001;
    public void Drag(double velocityX, double velocityY)
    {
        velocityX = double.IsFinite(velocityX) ? Math.Clamp(velocityX, -5000, 5000) : 0;
        velocityY = double.IsFinite(velocityY) ? Math.Clamp(velocityY, -5000, 5000) : 0;
        var speed = Math.Min(1, Math.Sqrt(velocityX * velocityX + velocityY * velocityY) / 2500);
        targetX = .012 * speed; targetY = -.012 * speed;
        targetAngle = Math.Clamp(velocityX / 3500, -1, 1);
    }
    public void Release() { targetX = targetY = targetAngle = 0; }
    public void Reset() { Release(); x = y = angle = vx = vy = va = 0; }
    public void Step(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return;
        seconds = Math.Min(seconds, .1);
        while (seconds > 0)
        {
            var dt = Math.Min(seconds, 1d / 120); seconds -= dt;
            Spring(ref x, ref vx, targetX, dt); Spring(ref y, ref vy, targetY, dt); Spring(ref angle, ref va, targetAngle, dt);
        }
    }
    private static void Spring(ref double position, ref double velocity, double target, double dt)
    {
        velocity += ((target - position) * 240 - velocity * 24) * dt;
        position += velocity * dt;
    }
}
