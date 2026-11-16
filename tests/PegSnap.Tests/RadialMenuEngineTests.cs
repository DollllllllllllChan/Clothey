using PegSnap.Core.Radial;
using Xunit;

namespace PegSnap.Tests;

public class RadialMenuEngineTests
{
    private readonly RadialMenuEngine _engine = new() { DeadZoneRadius = 30.0 };

    [Fact]
    public void GetSector_WithinDeadZone_ReturnsNone()
    {
        // Displacement smaller than 30 pixels
        var sector = _engine.GetSector(10, 10);
        Assert.Equal(RadialSectorType.None, sector);

        var sectorCenter = _engine.GetSector(0, 0);
        Assert.Equal(RadialSectorType.None, sectorCenter);
    }

    [Fact]
    public void GetSector_TowardsNorth_ReturnsAreaCapture()
    {
        // North in screen coordinates: deltaX ~ 0, deltaY negative
        var sector = _engine.GetSector(0, -60);
        Assert.Equal(RadialSectorType.AreaCapture, sector);
    }

    [Fact]
    public void GetSector_TowardsEast_ReturnsScreenRecording()
    {
        // East: deltaX positive, deltaY ~ 0
        var sector = _engine.GetSector(60, 0);
        Assert.Equal(RadialSectorType.ScreenRecording, sector);
    }

    [Fact]
    public void GetSector_TowardsSouth_ReturnsTextOcr()
    {
        // South: deltaX ~ 0, deltaY positive
        var sector = _engine.GetSector(0, 60);
        Assert.Equal(RadialSectorType.TextOcr, sector);
    }

    [Fact]
    public void GetSector_TowardsWest_ReturnsFullCapture()
    {
        // West: deltaX negative, deltaY ~ 0
        var sector = _engine.GetSector(-60, 0);
        Assert.Equal(RadialSectorType.FullCapture, sector);
    }

    [Theory]
    [InlineData(RadialSectorType.AreaCapture, "Area Snip")]
    [InlineData(RadialSectorType.ScreenRecording, "Recording")]
    [InlineData(RadialSectorType.TextOcr, "Extract OCR")]
    [InlineData(RadialSectorType.FullCapture, "Full Window")]
    public void GetSectorInfo_ReturnsExpectedTitles(RadialSectorType sector, string expectedTitle)
    {
        var (title, desc, icon) = RadialMenuEngine.GetSectorInfo(sector);
        Assert.Equal(expectedTitle, title);
        Assert.False(string.IsNullOrEmpty(desc));
        Assert.False(string.IsNullOrEmpty(icon));
    }
}
