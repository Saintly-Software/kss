namespace KirbySuperStar.Movement;

/// <summary>Buttons held during one tick.</summary>
public readonly record struct KirbyInput(bool Left, bool Right, bool Up, bool Down, bool Jump, bool Attack, bool Run)
{
    /// <summary>Horizontal direction: -1 left, 1 right, 0 for none or both.</summary>
    public int Direction => (Right ? 1 : 0) - (Left ? 1 : 0);
}
