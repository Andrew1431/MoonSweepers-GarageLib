using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MoonCleaners;
using Steamworks;
using UnityEngine;

namespace SharedUpgrades
{
    /// <summary>
    /// The shared level of a kind is the highest level any owned vehicle has in it. Nothing is stored besides the
    /// vanilla per-vehicle bytes: every vehicle the kind applies to is raised to that level, so the save stays vanilla
    /// and keeps the levels when the mod is removed.
    /// </summary>
    internal static class Shared
    {
        internal const int Kinds = 7;
        static MoonWorld world;
        static long lastSignature = long.MinValue;
        static bool loggedFirst;

        internal static MoonWorld FindWorld()
        {
            if (world != null && world.Pointer != IntPtr.Zero && world) return world;
            var all = UnityEngine.Object.FindObjectsByType<MoonWorld>(FindObjectsSortMode.None);
            world = all != null && all.Length > 0 ? all[0] : null;
            return world;
        }

        internal static void Reset() { world = null; lastSignature = long.MinValue; loggedFirst = false; }

        /// <summary>Single player, the host, or a client whose host runs the mod.</summary>
        internal static bool Active => Plugin.Enabled.Value && (!MoonSteamLobby.InLobby || MoonSteamLobby.IsHost || Lobby.HostShares);

        internal static List<MoonOwnedVehicle> Owned(MoonWorld w)
        {
            var list = new List<MoonOwnedVehicle>();
            var d = w?.ownedVehicles;
            if (d == null) return list;
            foreach (var kv in d) if (kv.Value != null) list.Add(kv.Value);
            return list;
        }

        internal static bool Applies(MoonVehicleUpgrades.Def def, int vehicle)
        {
            try { return def != null && MoonVehicleUpgrades.Applies(def, vehicle); } catch { return false; }
        }

        // Re-run only when some vehicle's levels or the set of vehicles changed.
        internal static void Check(MoonWorld w)
        {
            var owned = Owned(w);
            if (Signature(owned) == lastSignature) return;
            if (!loggedFirst) { loggedFirst = true; Plugin.L.LogInfo("Levels on load:\n" + Table(owned)); }
            if (Active && Equalize(w, owned)) Plugin.L.LogInfo("Levels now:\n" + Table(owned));
            lastSignature = Signature(owned);
        }

        static long Signature(List<MoonOwnedVehicle> owned)
        {
            long sig = owned.Count;
            foreach (var v in owned)
            {
                sig = sig * 31 + v.vehicle;
                for (int k = 0; k < Kinds; k++) sig = sig * 31 + MoonWorld.VehicleUpgradeLevel(v, (MoonVehicleUpgrades.Kind)k);
            }
            return sig;
        }

        /// <summary>Raises every vehicle to the highest level of each kind it can take. Never lowers, never exceeds maxLevel.</summary>
        internal static bool Equalize(MoonWorld w, List<MoonOwnedVehicle> owned)
        {
            bool changed = false;
            for (int k = 0; k < Kinds; k++)
            {
                var kind = (MoonVehicleUpgrades.Kind)k;
                var def = MoonVehicleUpgrades.Find(kind);
                if (def == null) continue;
                int shared = 0;
                foreach (var v in owned)
                    if (Applies(def, v.vehicle)) shared = Math.Max(shared, MoonWorld.VehicleUpgradeLevel(v, kind));
                shared = Math.Min(shared, def.maxLevel);
                foreach (var v in owned)
                {
                    if (!Applies(def, v.vehicle)) continue;
                    int level = MoonWorld.VehicleUpgradeLevel(v, kind);
                    if (level >= shared) continue;
                    MoonWorld.EnsureUpgrades(v);
                    v.upgrades[k] = (byte)shared;
                    Plugin.L.LogInfo($"{kind}: vehicle #{v.vehicle} {level} -> {shared}");
                    changed = true;
                }
            }
            if (changed)
            {
                // Spawned vehicles re-apply upgrades when this moves; the revision makes the host resend its vehicle list.
                w._VehicleUpgradesVersion_k__BackingField = w.VehicleUpgradesVersion + 1;
                w._OwnedVehiclesRevision_k__BackingField = w.OwnedVehiclesRevision + 1;
            }
            return changed;
        }

        /// <summary>Price scale a shared level of this kind costs at (before PriceMultiplier).</summary>
        internal static decimal TeamScale(MoonVehicleUpgrades.Kind kind, int vehicle)
        {
            decimal own = Money.From(MoonVehicleUpgrades.PriceScale(vehicle));
            switch (Plugin.Scale.Value)
            {
                case ScaleMode.Vehicle: return own;
                case ScaleMode.Fixed: return (decimal)Plugin.FixedScale.Value;
            }
            var def = MoonVehicleUpgrades.Find(kind);
            decimal best = own;
            foreach (var v in Owned(FindWorld()))
                if (Applies(def, v.vehicle)) best = Math.Max(best, Money.From(MoonVehicleUpgrades.PriceScale(v.vehicle)));
            return best;
        }

        /// <summary>Shared price of the next level after <paramref name="level"/>, rounded to $10 like vanilla.</summary>
        internal static decimal SharedPrice(MoonVehicleUpgrades.Def def, int vehicle, int level)
        {
            var prices = def.prices;
            if (prices == null || level < 0 || level >= prices.Length) return 0;
            decimal price = prices[level] * TeamScale(def.kind, vehicle) * (decimal)Plugin.PriceMultiplier.Value;
            return Math.Max(0, Math.Round(price / 10, MidpointRounding.AwayFromZero) * 10);
        }

        internal static string Table(List<MoonOwnedVehicle> owned)
        {
            var sb = new StringBuilder();
            string me = MoonWorld.LocalPlayer;
            foreach (var v in owned)
            {
                sb.Append($"  #{v.vehicle} garage {v.garage} ({(v.owner == me ? "me" : "crew")}):");
                for (int k = 0; k < Kinds; k++)
                {
                    var kind = (MoonVehicleUpgrades.Kind)k;
                    var def = MoonVehicleUpgrades.Find(kind);
                    if (!Applies(def, v.vehicle)) continue;
                    sb.Append($" {kind}={MoonWorld.VehicleUpgradeLevel(v, kind)}/{def.maxLevel}");
                    Il2CppSystem.Decimal p = default;
                    if (MoonWorld.TryGetVehicleUpgradePrice(v, kind, out p)) sb.Append($" (${Money.From(p)})");
                }
                sb.Append('\n');
            }
            return sb.Length == 0 ? "  (no vehicles)" : sb.ToString().TrimEnd('\n');
        }
    }

    /// <summary>The host advertises the mod in Steam lobby data so clients with it know to use shared prices.</summary>
    internal static class Lobby
    {
        internal static bool HostShares;
        static ulong published;

        internal static void Refresh()
        {
            try
            {
                if (!MoonSteamLobby.InLobby) { HostShares = false; published = 0; return; }
                var id = new CSteamID(MoonSteamLobby.LobbyId);
                if (MoonSteamLobby.IsHost)
                {
                    if (published == MoonSteamLobby.LobbyId || !Plugin.Enabled.Value) return;
                    SteamMatchmaking.SetLobbyData(id, Plugin.LobbyKey, Plugin.Version);
                    published = MoonSteamLobby.LobbyId;
                    Plugin.L.LogInfo("Hosting: advertised shared upgrades in the lobby");
                    return;
                }
                bool shares = !string.IsNullOrEmpty(SteamMatchmaking.GetLobbyData(id, Plugin.LobbyKey));
                if (shares != HostShares) Plugin.L.LogInfo(shares ? "Host shares upgrades: using shared levels and prices" : "Host doesn't run Shared Upgrades: vanilla upgrades");
                HostShares = shares;
            }
            catch (Exception e) { Plugin.L.LogWarning("Lobby check failed: " + e.Message); }
        }
    }

    internal static class Money
    {
        internal static decimal From(Il2CppSystem.Decimal d)
        {
            try { return decimal.Parse(d.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture); }
            catch { return (decimal)(long)d; }
        }
        internal static Il2CppSystem.Decimal To(decimal d) => new Il2CppSystem.Decimal((double)d);
    }
}
