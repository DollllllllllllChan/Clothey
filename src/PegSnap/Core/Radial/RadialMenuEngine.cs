using System;

namespace PegSnap.Core.Radial;

public enum RadialSectorType
{
    None = 0,
    AreaCapture = 1,     // North / Top: Snip drag box
    ScreenRecording = 2, // East / Right: Mini recorder / frame capture
    TextOcr = 3,         // South / Bottom: Text extraction to clipboard
    FullCapture = 4      // West / Left: Full window / screen capture
}

public class RadialMenuEngine
{
    public double DeadZoneRadius { get; set; } = 30.0;

    /// <summary>
    /// Calculates the radial sector based on mouse displacement from the center of the radial menu.
    /// In screen coordinates: deltaX = mouseX - centerX, deltaY = mouseY - centerY (Y points downwards).
    /// </summary>
    public RadialSectorType GetSector(double deltaX, double deltaY)
    {
        double radius = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (radius < DeadZoneRadius)
        {
            return RadialSectorType.None;
        }

        // Math.Atan2 returns angle in radians [-PI, PI]
        // 0 rad = East (X+, Y 0)
        // PI/2 rad = South (X 0, Y+)
        // PI rad / -PI rad = West (X-, Y 0)
        // -PI/2 rad = North (X 0, Y-)
        double angleRad = Math.Atan2(deltaY, deltaX);
        double angleDeg = angleRad * (180.0 / Math.PI);
        if (angleDeg < 0)
        {
            angleDeg += 360.0;
        }

        // Angles in [0, 360):
        // East:  315° to 360° and 0° to 45°
        // South: 45° to 135°
        // West:  135° to 225°
        // North: 225° to 315°
        if (angleDeg >= 315.0 || angleDeg < 45.0)
        {
            return RadialSectorType.ScreenRecording;
        }
        else if (angleDeg >= 45.0 && angleDeg < 135.0)
        {
            return RadialSectorType.TextOcr;
        }
        else if (angleDeg >= 135.0 && angleDeg < 225.0)
        {
            return RadialSectorType.FullCapture;
        }
        else // 225.0 <= angleDeg < 315.0
        {
            return RadialSectorType.AreaCapture;
        }
    }

    /// <summary>
    /// Helper to get descriptive title and shortcut description for each sector.
    /// </summary>
    public static (string Title, string Description, string Icon) GetSectorInfo(RadialSectorType sector)
    {
        return sector switch
        {
            RadialSectorType.AreaCapture => ("Area Snip", "Drag selection box", "✂"),
            RadialSectorType.ScreenRecording => ("Recording", "Mini screen recorder", "⏺"),
            RadialSectorType.TextOcr => ("Extract OCR", "Copy text to clipboard", "📄"),
            RadialSectorType.FullCapture => ("Full Window", "Capture active window", "🖵"),
            _ => ("Clothey", "Drag towards an action", "⚡")
        };
    }
}
