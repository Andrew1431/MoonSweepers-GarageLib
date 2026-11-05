using System.Linq;
using StructureLib.Pure;
using Xunit;

namespace StructureLib.Tests;

public class PanelTextTests
{
    static PanelLayout Gen(bool running = true, double charge = 3.5, double fuelKg = 6, int fed = 1) =>
        PanelText.Generator("Coalite furnace", 24f, 10f, 4f, 6f, charge, "coalite", "coalite", fuelKg, 10, running, fed);

    static PanelSlot[] Slots() => new[]
    {
        new PanelSlot("sand", 4.5, 16, 1, true),
        new PanelSlot("coalite", 0, 10, 3, false),
    };

    static PanelLayout Mach(bool running = true, int grid = 14, float progress = 0.25f, int cycles = 3, PanelSlot[] slots = null, bool withPower = true, int maxRows = 8, string status = null) =>
        PanelText.Machine("Coking oven", status, running, grid, 0.5f, 6f, 12f, progress, cycles, slots ?? Slots(), withPower, maxRows);

    [Fact]
    public void Generator_Running_ShowsBurningAndFourRows()
    {
        var l = Gen();
        Assert.Equal("FUEL POWER  ·  24 M RANGE", l.Kicker);
        Assert.Equal("Coalite furnace", l.Title);
        Assert.Equal("Burning coalite", l.State);
        Assert.Equal(PanelColour.Mint, l.StateColour);
        Assert.Equal(4, l.Rows.Count);
        Assert.Equal(new[] { "OUTPUT", "BATTERY", "FUEL", "POWERING" }, l.Rows.Select(r => r.Label));
        Assert.Equal("4.0 / 10 kW", l.Rows[0].Value);
        Assert.Equal(0.4f, l.Rows[0].Fill, 3);
        Assert.Equal(PanelColour.Cyan, l.Rows[0].Colour);
        Assert.Equal("3.5 / 6 kWh", l.Rows[1].Value);
        Assert.Equal(0.583f, l.Rows[1].Fill, 3);
        Assert.Equal(PanelColour.Mint, l.Rows[1].Colour);
        Assert.Equal("6 / 10 kg", l.Rows[2].Value);
        Assert.Equal(0.6f, l.Rows[2].Fill, 3);
        Assert.Equal(PanelColour.Tint, l.Rows[2].Colour);
        Assert.Equal("1 machine", l.Rows[3].Value);
        Assert.Equal(1f, l.Rows[3].Fill);
        Assert.Equal(PanelColour.Cyan, l.Rows[3].Colour);
    }

    [Fact]
    public void Generator_FullBattery_Idle()
    {
        var l = Gen(running: false, charge: 6);
        Assert.Equal("Battery full: burns when power is drawn", l.State);
        Assert.Equal(PanelColour.Muted, l.StateColour);
    }

    [Fact]
    public void Generator_NoFuel_Coral()
    {
        var l = Gen(running: false, fuelKg: 0);
        Assert.Equal("No fuel: feed coalite by belt", l.State);
        Assert.Equal(PanelColour.Coral, l.StateColour);
    }

    [Fact]
    public void Generator_FuelNotYetBurning_Starting()
    {
        var l = Gen(running: false);
        Assert.Equal("Starting", l.State);
        Assert.Equal(PanelColour.Muted, l.StateColour);
    }

    [Fact]
    public void Generator_NoMachine_AmberZeroFill()
    {
        var r = Gen(fed: 0).Rows[3];
        Assert.Equal("no machine running", r.Value);
        Assert.Equal(0f, r.Fill);
        Assert.Equal(PanelColour.Amber, r.Colour);
        Assert.Equal("3 machines", Gen(fed: 3).Rows[3].Value);
    }

    [Fact]
    public void Machine_WithPower_PowerRowFirst()
    {
        var r = Mach().Rows[0];
        Assert.Equal("POWER", r.Label);
        Assert.Equal("50% · 6.0 of 12 kW", r.Value);
        Assert.Equal(0.5f, r.Fill, 3);
        Assert.Equal(PanelColour.Cyan, r.Colour);
    }

    [Fact]
    public void Machine_NoGrid_Coral()
    {
        var l = Mach(grid: 0);
        Assert.Equal("no power source in reach", l.Rows[0].Value);
        Assert.Equal(0f, l.Rows[0].Fill);
        Assert.Equal(PanelColour.Coral, l.Rows[0].Colour);
        Assert.Equal(PanelColour.Coral, l.StateColour);
    }

    [Fact]
    public void Machine_SlotsAndCycle()
    {
        var l = Mach();
        Assert.Equal(4, l.Rows.Count);
        Assert.Equal("SAND", l.Rows[1].Label);
        Assert.Equal("4.5 / 16 kg", l.Rows[1].Value);
        Assert.Equal(PanelColour.Sand, l.Rows[1].Colour);
        Assert.Equal("COALITE", l.Rows[2].Label);
        Assert.Equal("0 / 10 kg", l.Rows[2].Value);
        Assert.Equal(PanelColour.Tint, l.Rows[2].Colour);
        Assert.Equal("CYCLE", l.Rows[3].Label);
        Assert.Equal("25% · 3 done", l.Rows[3].Value);
        Assert.Equal(0.25f, l.Rows[3].Fill, 3);
        Assert.Equal(PanelColour.Cyan, l.Rows[3].Colour);
        Assert.Equal(PanelColour.Cyan, l.StateColour);
    }

    [Fact]
    public void Machine_Idle_CycleRowReadsDone()
    {
        var l = Mach(running: false);
        var r = l.Rows[l.Rows.Count - 1];
        Assert.Equal("3 done", r.Value);
        Assert.Equal(0f, r.Fill);
        Assert.Equal(PanelColour.Cyan, r.Colour);
        Assert.Equal("Idle", l.State);
        Assert.Equal(PanelColour.Amber, l.StateColour);
    }

    [Fact]
    public void Machine_StatusShownAsState()
    {
        Assert.Equal("Waiting for sand", Mach(status: "Waiting for sand").State);
    }

    [Fact]
    public void RowsCapped_KeepsCycleRow()
    {
        var slots = new[]
        {
            new PanelSlot("a", 1, 2, 0, true), new PanelSlot("b", 1, 2, 0, true),
            new PanelSlot("c", 1, 2, 0, true), new PanelSlot("d", 1, 2, 0, false),
        };
        var l = Mach(slots: slots, maxRows: 4);
        Assert.Equal(new[] { "POWER", "A", "B", "CYCLE" }, l.Rows.Select(r => r.Label));
    }

    [Fact]
    public void Kicker_UsesFirstInputAndOutput()
    {
        Assert.Equal("PRODUCTION  ·  SAND INTO COALITE", Mach().Kicker);
        Assert.Equal("PRODUCTION", Mach(slots: new[] { new PanelSlot("sand", 0, 1, 0, true) }).Kicker);
    }

    [Fact]
    public void Fill_ClampedToOne()
    {
        var l = Mach(slots: new[] { new PanelSlot("sand", 25, 20, 0, true) });
        Assert.Equal(1f, l.Rows[1].Fill);
        Assert.Equal(PanelColour.Dust, l.Rows[1].Colour);
    }

    [Fact]
    public void Formats_UseInvariantOneDecimalKwAndTrimmedKg()
    {
        Assert.Equal("0.0", PanelText.Kw(0f));
        Assert.Equal("10.0", PanelText.Kw(10f));
        Assert.Equal("0", PanelText.Kg(0));
        Assert.Equal("4.5", PanelText.Kg(4.5));
    }
}
