using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GarageLib
{
    /// <summary>
    /// Balance numbers kept outside code. Every <c>*.vehicles.json</c> under <c>BepInEx/plugins</c> (shipped with a mod)
    /// and then under <c>BepInEx/config</c> (local tweaks, applied last) is read at startup; a registered vehicle whose
    /// id appears there gets those fields instead of the ones its code set. Later files override earlier ones field by
    /// field, so a config file can change one number. The repo's copy lives in <c>data/</c> and is exported from the
    /// scaling sheet.
    /// </summary>
    public static class VehicleStats
    {
        static readonly Dictionary<string, Dictionary<string, (JsonElement value, string file)>> byId = new(StringComparer.Ordinal);

        /// <summary>Ids that have a stats entry, whether or not a mod registered them.</summary>
        public static IEnumerable<string> Ids => byId.Keys;

        internal static void Load()
        {
            byId.Clear();
            foreach (var root in new[] { BepInEx.Paths.PluginPath, BepInEx.Paths.ConfigPath })
            {
                if (!Directory.Exists(root)) continue;
                var files = Directory.GetFiles(root, "*.vehicles.json", SearchOption.AllDirectories);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (var f in files) Read(f);
            }
            CoreLog.Info($"Vehicle stats: {byId.Count} entries");
        }

        static void Read(string path)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (!doc.RootElement.TryGetProperty("vehicles", out var vehicles) || vehicles.ValueKind != JsonValueKind.Object)
                {
                    CoreLog.Warn($"Vehicle stats: {path} has no \"vehicles\" object; skipped");
                    return;
                }
                int n = 0;
                foreach (var v in vehicles.EnumerateObject())
                {
                    if (v.Value.ValueKind != JsonValueKind.Object) continue;
                    if (!byId.TryGetValue(v.Name, out var fields)) byId[v.Name] = fields = new(StringComparer.Ordinal);
                    foreach (var f in v.Value.EnumerateObject()) fields[f.Name] = (f.Value.Clone(), Path.GetFileName(path));
                    n++;
                }
                CoreLog.Debug($"Vehicle stats: {n} from {path}");
            }
            catch (Exception e) { CoreLog.Error($"Vehicle stats: couldn't read {path}: {e.Message}"); }
        }

        /// <summary>Overwrite the definition's numbers with its stats entry, if it has one. Returns the fields changed.</summary>
        internal static string Apply(VehicleDefinition d)
        {
            if (!byId.TryGetValue(d.Id, out var e)) return null;
            var changed = new List<string>();
            void Str(string k, Action<string> set) { if (e.TryGetValue(k, out var p) && p.value.ValueKind == JsonValueKind.String) { set(p.value.GetString()); changed.Add($"{k} ({p.file})"); } }
            void Num(string k, Action<double> set) { if (e.TryGetValue(k, out var p) && p.value.ValueKind == JsonValueKind.Number) { set(p.value.GetDouble()); changed.Add($"{k} ({p.file})"); } }
            Str("name", v => d.Name = v);
            Str("summary", v => d.Summary = v);
            Num("price", v => d.Price = (decimal)v);
            Num("baseVehicle", v => d.BaseVehicle = (int)v);
            Num("width", v => d.Width = (float)v);
            Num("depth", v => d.Depth = (float)v);
            Num("reach", v => d.Reach = (float)v);
            Num("speed", v => d.Speed = (float)v);
            Num("turn", v => d.Turn = (float)v);
            Num("capacity", v => d.Capacity = (int)v);
            Num("storageKg", v => d.StorageKg = v);
            Num("heft", v => d.Heft = (float)v);
            Num("upgradePriceScale", v => d.UpgradePriceScale = v);
            return changed.Count == 0 ? null : string.Join(", ", changed);
        }
    }
}
