using System;
using System.Collections.Generic;
using System.Globalization;

namespace StructureLib.Pure;

/// <summary>Colour roles the look-at panel maps to its palette. Pure: no Unity types.</summary>
public enum PanelColour { Cyan, Amber, Coral, Mint, Muted, Dust, Sand, Stone, Tint }

/// <summary>One bar row: label, fill 0..1, value text, colour.</summary>
public readonly record struct PanelRow(string Label, float Fill, string Value, PanelColour Colour);

/// <summary>The text of a look-at panel: kicker, title, state line and bar rows.</summary>
public readonly record struct PanelLayout(string Kicker, string Title, string State, PanelColour StateColour, IReadOnlyList<PanelRow> Rows);

/// <summary>A processor slot as the panel shows it (batch slots are not passed).</summary>
public readonly record struct PanelSlot(string ShortName, double Kilograms, double Capacity, byte Cargo, bool IsInput);

/// <summary>Text and fill rules for the look-at panel of generators and machines.</summary>
public static class PanelText
{
    const string Sep = "  ·  ";
    const string RowSep = " · ";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Kw(float kw) => kw.ToString("F1", Inv);

    public static string Kg(double kg) => kg.ToString("0.#", Inv);

    static float Fill(double value, double capacity)
    {
        if (!(capacity > 0)) return 0f;
        double f = value / capacity;
        if (double.IsNaN(f)) return 0f;
        return (float)Math.Max(0.0, Math.Min(1.0, f));
    }

    static string G(float v) => v.ToString("G", Inv);
    static string G(double v) => v.ToString("G", Inv);

    static string Poles(int n) => n == 1 ? "1 pole" : n.ToString(Inv) + " poles";

    /// <summary>The GRID row of a source or bank: its grid's pole count, or coral when no pole reaches it.</summary>
    static PanelRow GridRow(bool onGrid, int gridPoles) =>
        !onGrid ? new PanelRow("GRID", 0f, "No pole in reach", PanelColour.Coral)
        : gridPoles <= 0 ? new PanelRow("GRID", 1f, "on a grid", PanelColour.Cyan)
        : new PanelRow("GRID", 1f, Poles(gridPoles), PanelColour.Cyan);

    public static PanelLayout Generator(string name, float peakKw, float suppliedKw, string fuelName, string fuelShortName,
        double fuelKg, double fuelCapacityKg, bool running, bool onGrid, int gridPoles)
    {
        string state;
        PanelColour colour;
        if (running) { state = "Burning " + fuelShortName; colour = PanelColour.Mint; }
        else if (fuelKg <= 0) { state = "No fuel: feed " + fuelName + " by belt"; colour = PanelColour.Coral; }
        else if (!onGrid) { state = "No pole in reach"; colour = PanelColour.Coral; }
        else { state = "Standing by: grid has power"; colour = PanelColour.Muted; }

        var rows = new List<PanelRow>
        {
            new PanelRow("OUTPUT", Fill(suppliedKw, peakKw), Kw(suppliedKw) + " / " + G(peakKw) + " kW", PanelColour.Cyan),
            new PanelRow("FUEL", Fill(fuelKg, fuelCapacityKg), Kg(fuelKg) + " / " + G(fuelCapacityKg) + " kg", PanelColour.Tint),
            GridRow(onGrid, gridPoles),
        };
        return new PanelLayout("FUEL POWER", name, state, colour, rows);
    }

    /// <summary>
    /// A pole's panel: its whole grid's totals. <paramref name="machinesFed"/> is the machines the grid powered in the
    /// last solve (negative when unknown, as on a client).
    /// </summary>
    public static PanelLayout Pole(string name, int poles, float supplyKw, float peakKw, float usedKw, float demandKw, double storedKwh,
        float capacityKwh, float satisfaction, int machinesFed)
    {
        int pct = (int)Math.Round(Math.Max(0f, Math.Min(1f, satisfaction)) * 100f, MidpointRounding.AwayFromZero);
        bool shortGrid = demandKw > 0f && satisfaction < 0.995f;
        string state;
        PanelColour colour;
        if (shortGrid) { state = "Grid short: " + pct.ToString(Inv) + " %"; colour = PanelColour.Amber; }
        else if (machinesFed < 0) { state = "Grid powered"; colour = PanelColour.Cyan; }
        else if (machinesFed == 0) { state = "Nothing drawing power"; colour = PanelColour.Muted; }
        else { state = "Powering " + (machinesFed == 1 ? "1 machine" : machinesFed.ToString(Inv) + " machines"); colour = PanelColour.Cyan; }

        var rows = new List<PanelRow>
        {
            new PanelRow("SUPPLY", Fill(supplyKw, peakKw), Kw(supplyKw) + " / " + G(peakKw) + " kW", PanelColour.Cyan),
            new PanelRow("DEMAND", Fill(usedKw, demandKw), Kw(usedKw) + " / " + G(demandKw) + " kW", usedKw < demandKw ? PanelColour.Amber : PanelColour.Cyan),
            new PanelRow("STORAGE", Fill(storedKwh, capacityKwh), storedKwh.ToString("F1", Inv) + " / " + G(capacityKwh) + " kWh", PanelColour.Mint),
            new PanelRow("SATISFACTION", Fill(satisfaction, 1), pct.ToString(Inv) + " %", shortGrid ? PanelColour.Amber : PanelColour.Cyan),
        };
        return new PanelLayout("POWER GRID" + Sep + (poles == 1 ? "1 POLE" : poles.ToString(Inv) + " POLES"), name, state, colour, rows);
    }

    public static PanelLayout Solar(string name, float suppliedKw, float peakKw, float sunFactor, bool onGrid, int gridPoles)
    {
        bool night = !(sunFactor > 0f);
        string state = night ? "Night" : "Sun " + Math.Round(Math.Min(1f, sunFactor) * 100f, MidpointRounding.AwayFromZero).ToString("F0", Inv) + " %";
        var rows = new List<PanelRow>
        {
            new PanelRow("OUTPUT", Fill(suppliedKw, peakKw), Kw(suppliedKw) + " / " + G(peakKw) + " kW", PanelColour.Cyan),
            GridRow(onGrid, gridPoles),
        };
        return new PanelLayout("SOLAR POWER", name, state, night ? PanelColour.Muted : PanelColour.Mint, rows);
    }

    public static PanelLayout Storage(string name, double chargeKwh, float capacityKwh, float batteryKw, bool onGrid, int gridPoles)
    {
        string state;
        PanelColour colour;
        if (batteryKw > 0.05f) { state = "Charging"; colour = PanelColour.Mint; }
        else if (batteryKw < -0.05f) { state = "Discharging"; colour = PanelColour.Cyan; }
        else if (chargeKwh >= capacityKwh - 0.001) { state = "Full"; colour = PanelColour.Mint; }
        else if (chargeKwh <= 0.001) { state = "Empty"; colour = PanelColour.Amber; }
        else { state = "Idle"; colour = PanelColour.Muted; }
        var rows = new List<PanelRow>
        {
            new PanelRow("STORED", Fill(chargeKwh, capacityKwh), chargeKwh.ToString("F1", Inv) + " / " + G(capacityKwh) + " kWh", PanelColour.Mint),
            GridRow(onGrid, gridPoles),
        };
        return new PanelLayout("POWER STORAGE", name, state, colour, rows);
    }

    public static PanelLayout Machine(string name, string status, bool running, int grid, float power, float usedKw, float runningKw,
        float progress, int cycles, IReadOnlyList<PanelSlot> slots, bool withPower, int maxRows)
    {
        slots ??= Array.Empty<PanelSlot>();

        string input = null, output = null;
        foreach (var s in slots)
        {
            if (s.IsInput) { if (input == null) input = s.ShortName; }
            else if (output == null) output = s.ShortName;
        }
        string kicker = input != null && output != null
            ? "PRODUCTION" + Sep + input.ToUpperInvariant() + " INTO " + output.ToUpperInvariant()
            : "PRODUCTION";

        string state = string.IsNullOrEmpty(status) ? "Idle" : status;
        PanelColour stateColour = grid == 0 ? PanelColour.Coral : running ? PanelColour.Cyan : PanelColour.Amber;

        var head = new List<PanelRow>();
        if (withPower)
        {
            head.Add(grid == 0
                ? new PanelRow("POWER", 0f, "no power source in reach", PanelColour.Coral)
                : new PanelRow("POWER", Fill(power, 1), (power * 100).ToString("F0", Inv) + "%" + RowSep + Kw(usedKw) + " of " + G(runningKw) + " kW", PanelColour.Cyan));
        }

        var slotRows = new List<PanelRow>();
        foreach (var s in slots)
        {
            var colour = s.Cargo == 0 ? PanelColour.Dust : s.Cargo == 1 ? PanelColour.Sand : s.Cargo == 2 ? PanelColour.Stone : PanelColour.Tint;
            slotRows.Add(new PanelRow((s.ShortName ?? "").ToUpperInvariant(), Fill(s.Kilograms, s.Capacity), Kg(s.Kilograms) + " / " + G(s.Capacity) + " kg", colour));
        }

        var cycle = running
            ? new PanelRow("CYCLE", Fill(progress, 1), (progress * 100).ToString("F0", Inv) + "%" + RowSep + cycles.ToString(Inv) + " done", PanelColour.Cyan)
            : new PanelRow("CYCLE", 0f, cycles.ToString(Inv) + " done", PanelColour.Cyan);

        while (head.Count + slotRows.Count + 1 > maxRows && slotRows.Count > 0)
            slotRows.RemoveAt(slotRows.Count - 1);

        var rows = new List<PanelRow>(head);
        rows.AddRange(slotRows);
        rows.Add(cycle);
        return new PanelLayout(kicker, name, state, stateColour, rows);
    }
}
