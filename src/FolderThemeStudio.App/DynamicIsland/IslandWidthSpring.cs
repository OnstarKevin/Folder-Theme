namespace FolderThemeStudio.App.DynamicIsland;

/// <summary>临界阻尼弹簧的解析积分；改变目标保留速度，各刷新率下均稳定。尺寸单位为 WPF DIP。</summary>
public sealed class IslandWidthSpring(double initial, double minimum = IslandWidthSpring.Minimum, double maximum = IslandWidthSpring.Maximum)
{
    public const double Minimum = 144;
    public const double Maximum = 8 / 2.54 * 96;
    public double Value { get; private set; } = Math.Clamp(initial, minimum, maximum);
    public double Velocity { get; private set; }
    private double target = Math.Clamp(initial, minimum, maximum);
    public double Target { get => target; set => target = double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : minimum; }
    public bool IsResting => Math.Abs(Value - Target) < .05 && Math.Abs(Velocity) < .1;
    public void Reset(double value) { Target = value; Value = Target; Velocity = 0; }
    public void Step(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return;
        const double frequency = 19;
        var dt = Math.Min(seconds, .1);
        var displacement = Value - Target;
        var coefficient = Velocity + frequency * displacement;
        var decay = Math.Exp(-frequency * dt);
        var next = Target + (displacement + coefficient * dt) * decay;
        Velocity = (Velocity - frequency * coefficient * dt) * decay;
        Value = Math.Clamp(next, minimum, maximum);
        if (Value != next) Velocity = 0;
        if (IsResting) Reset(Target);
    }
}
