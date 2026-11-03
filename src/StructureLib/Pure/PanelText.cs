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

    public static PanelLayout Generator(string name, float rangeM, float peakKw, float suppliedKw, float batteryKwh, double chargeKwh,
        string fuelName, string fuelShortName, double fuelKg, double fuelCapacityKg, bool running, int machinesFed)
    {
        string state;
        PanelColour colour;
        if (running) { state = "Burning " + fuelShortName; colour = PanelColour.Mint; }
        else if (chargeKwh >= batteryKwh) { state = "Battery full: burns when power is drawn"; colour = PanelColour.Muted; }
        else if (fuelKg <= 0) { state = "No fuel: feed " + fuelName + " by belt"; colour = PanelColour.Coral; }
        else { state = "Starting"; colour = PanelColour.Muted; }

        var rows = new List<PanelRow>
        {
            new PanelRow("OUTPUT", Fill(suppliedKw, peakKw), Kw(suppliedKw) + " / " + G(peakKw) + " kW", PanelColour.Cyan),
            new PanelRow("BATTERY", Fill(chargeKwh, batteryKwh), chargeKwh.ToString("F1", Inv) + " / " + G(batteryKwh) + " kWh", PanelColour.Mint),
            new PanelRow("FUEL", Fill(fuelKg, fuelCapacityKg), Kg(fuelKg) + " / " + G(fuelCapacityKg) + " kg", PanelColour.Tint),
            new PanelRow("POWERING",
                machinesFed > 0 ? 1f : 0f,
                machinesFed <= 0 ? "no machine running" : machinesFed == 1 ? "1 machine" : machinesFed.ToString(Inv) + " machines",
                machinesFed > 0 ? PanelColour.Cyan : PanelColour.Amber),
        };
        return new PanelLayout("FUEL POWER" + Sep + G(rangeM) + " M RANGE", name, state, colour, rows);
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
