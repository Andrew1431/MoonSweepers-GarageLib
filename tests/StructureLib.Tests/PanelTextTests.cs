using System.Linq;
using StructureLib.Pure;
using Xunit;

namespace StructureLib.Tests;

public class PanelTextTests
{
    static PanelLayout Gen(bool running = true, double fuelKg = 6, bool onGrid = true, int poles = 3) =>
        PanelText.Generator("Coalite furnace", 10f, 4f, "coalite", "coalite", fuelKg, 10, running, onGrid, poles);

    static PanelSlot[] Slots() => new[]
    {
        new PanelSlot("sand", 4.5, 16, 1, true),
        new PanelSlot("coalite", 0, 10, 3, false),
    };

    static PanelLayout Mach(bool running = true, int grid = 14, float progress = 0.25f, int cycles = 3, PanelSlot[] slots = null, bool withPower = true, int maxRows = 8, string status = null) =>
        PanelText.Machine("Coking oven", status, running, grid, 0.5f, 6f, 12f, progress, cycles, slots ?? Slots(), withPower, maxRows);

    [Fact]
    public void Generator_Running_ShowsBurningAndThreeRows()
    {
        var l = Gen();
        Assert.Equal("FUEL POWER", l.Kicker);
        Assert.Equal("Coalite furnace", l.Title);
        Assert.Equal("Burning coalite", l.State);
        Assert.Equal(PanelColour.Mint, l.StateColour);
        Assert.Equal(new[] { "OUTPUT", "FUEL", "GRID" }, l.Rows.Select(r => r.Label));
        Assert.Equal("4.0 / 10 kW", l.Rows[0].Value);
        Assert.Equal(0.4f, l.Rows[0].Fill, 3);
        Assert.Equal(PanelColour.Cyan, l.Rows[0].Colour);
        Assert.Equal("6 / 10 kg", l.Rows[1].Value);
        Assert.Equal(0.6f, l.Rows[1].Fill, 3);
        Assert.Equal(PanelColour.Tint, l.Rows[1].Colour);
    }

    [Fact]
    public void Generator_GridRow()
    {
        var r = Gen().Rows[2];
        Assert.Equal("GRID", r.Label);
        Assert.Equal("3 poles", r.Value);
        Assert.Equal(PanelColour.Cyan, r.Colour);
        Assert.Equal("1 pole", Gen(poles: 1).Rows[2].Value);
        var none = Gen(running: false, onGrid: false, poles: 0).Rows[2];
        Assert.Equal("No pole in reach", none.Value);
        Assert.Equal(PanelColour.Coral, none.Colour);
    }

    [Fact]
    public void Generator_NoFuel_Coral()
    {
        var l = Gen(running: false, fuelKg: 0);
        Assert.Equal("No fuel: feed coalite by belt", l.State);
        Assert.Equal(PanelColour.Coral, l.StateColour);
    }

    [Fact]
    public void Generator_NoPole_Coral()
    {
        var l = Gen(running: false, onGrid: false, poles: 0);
        Assert.Equal("No pole in reach", l.State);
        Assert.Equal(PanelColour.Coral, l.StateColour);
    }

    [Fact]
    public void Generator_StandingBy_Muted()
    {
        var l = Gen(running: false);
        Assert.Equal("Standing by: grid has power", l.State);
        Assert.Equal(PanelColour.Muted, l.StateColour);
    }

    static PanelLayout Pole(int poles = 3, float satisfaction = 0.5f, float used = 5f, int fed = 2) =>
        PanelText.Pole("Power pole", poles, 5f, 10f, used, 10f, 0, 20f, satisfaction, fed);

    [Fact]
    public void Pole_Short()
    {
        var l = Pole();
        Assert.Equal("POWER GRID  ·  3 POLES", l.Kicker);
        Assert.Equal("Power pole", l.Title);
        Assert.Equal("Grid short: 50 %", l.State);
        Assert.Equal(PanelColour.Amber, l.StateColour);
        Assert.Equal(new[] { "SUPPLY", "DEMAND", "STORAGE", "SATISFACTION" }, l.Rows.Select(r => r.Label));
        Assert.Equal("5.0 / 10 kW", l.Rows[0].Value);
        Assert.Equal(PanelColour.Cyan, l.Rows[0].Colour);
        Assert.Equal("5.0 / 10 kW", l.Rows[1].Value);
        Assert.Equal(PanelColour.Amber, l.Rows[1].Colour);
        Assert.Equal("0.0 / 20 kWh", l.Rows[2].Value);
        Assert.Equal(PanelColour.Mint, l.Rows[2].Colour);
        Assert.Equal("50 %", l.Rows[3].Value);
        Assert.Equal(0.5f, l.Rows[3].Fill, 3);
        Assert.Equal(PanelColour.Amber, l.Rows[3].Colour);
    }

    [Fact]
    public void Pole_SinglePoleWording()
    {
        Assert.Equal("POWER GRID  ·  1 POLE", Pole(poles: 1).Kicker);
    }

    [Fact]
    public void Pole_Full()
    {
        var l = Pole(satisfaction: 1f, used: 10f);
        Assert.Equal("Powering 2 machines", l.State);
        Assert.Equal(PanelColour.Cyan, l.StateColour);
        Assert.Equal(PanelColour.Cyan, l.Rows[1].Colour);
        Assert.Equal(PanelColour.Cyan, l.Rows[3].Colour);
        Assert.Equal("100 %", l.Rows[3].Value);
        Assert.Equal("Powering 1 machine", Pole(satisfaction: 1f, used: 10f, fed: 1).State);
    }

    [Fact]
    public void Solar_Night()
    {
        var l = PanelText.Solar("Solar array", 0f, 10f, 0f, true, 2);
        Assert.Equal("SOLAR POWER", l.Kicker);
        Assert.Equal("Night", l.State);
        Assert.Equal(new[] { "OUTPUT", "GRID" }, l.Rows.Select(r => r.Label));
        Assert.Equal("0.0 / 10 kW", l.Rows[0].Value);
        Assert.Equal("2 poles", l.Rows[1].Value);
    }

    [Fact]
    public void Solar_Day()
    {
        var l = PanelText.Solar("Solar array", 7.5f, 10f, 0.75f, true, 1);
        Assert.Equal("Sun 75 %", l.State);
        Assert.Equal("7.5 / 10 kW", l.Rows[0].Value);
        Assert.Equal("1 pole", l.Rows[1].Value);
    }

    [Fact]
    public void Solar_NoPole()
    {
        var r = PanelText.Solar("Solar array", 0f, 10f, 1f, false, 0).Rows[1];
        Assert.Equal("GRID", r.Label);
        Assert.Equal("No pole in reach", r.Value);
        Assert.Equal(PanelColour.Coral, r.Colour);
    }

    [Theory]
    [InlineData(5.0, 1f, "Charging")]
    [InlineData(5.0, -1f, "Discharging")]
    [InlineData(10.0, 0f, "Full")]
    [InlineData(0.0, 0f, "Empty")]
    public void Storage_States(double charge, float batteryKw, string state)
    {
        var l = PanelText.Storage("Battery bank", charge, 10f, batteryKw, true, 2);
        Assert.Equal("POWER STORAGE", l.Kicker);
        Assert.Equal(state, l.State);
        Assert.Equal(new[] { "STORED", "GRID" }, l.Rows.Select(r => r.Label));
        Assert.Equal(charge.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " / 10 kWh", l.Rows[0].Value);
        Assert.Equal("2 poles", l.Rows[1].Value);
    }

    [Fact]
    public void Storage_NoPole()
    {
        var r = PanelText.Storage("Battery bank", 1, 10f, 0f, false, 0).Rows[1];
        Assert.Equal("No pole in reach", r.Value);
        Assert.Equal(PanelColour.Coral, r.Colour);
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
