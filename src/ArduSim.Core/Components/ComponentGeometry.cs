namespace ArduSim.Core.Components;

/// <summary>
/// Placement math shared by the canvas and the simulator: a part sits at (x, y) with its
/// unrotated box at that top-left corner; it is mirrored horizontally (flip), then rotated
/// clockwise in 90° steps about the box center.
/// </summary>
public static class ComponentGeometry
{
    public static (double X, double Y) PinPosition(ComponentDefinition definition, PinDefinition pin, double x, double y, int rotation, bool flip)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(pin);
        double px = flip ? definition.Width - pin.X : pin.X;
        double py = pin.Y;
        double cx = definition.Width / 2;
        double cy = definition.Height / 2;
        (double dx, double dy) = (px - cx, py - cy);
        (dx, dy) = (((rotation % 360) + 360) % 360) switch
        {
            90 => (-dy, dx),
            180 => (-dx, -dy),
            270 => (dy, -dx),
            _ => (dx, dy),
        };
        return (x + cx + dx, y + cy + dy);
    }
}
