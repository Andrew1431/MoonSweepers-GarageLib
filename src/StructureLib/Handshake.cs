using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MoonCleaners;
using Steamworks;
using UnityEngine;

namespace StructureLib
{
    // Co-op handshake. Every StructureLib player in a Steam lobby publishes its manifest (registered structures and cargo,
    // owning mod and version) as Steam lobby member data under Key. A joining client compares the host's manifest with
    // its own when the host offers the world (MoonCrewSession.Offered) and, on any problem, ends the join through the
    // session's own EndJoin with a line naming the problems. The host compares each crewmate's manifest too, and logs and
    // shows what differs. The vanilla session protocol is untouched. See research/STRUCTURES_RECON.md, "Co-op session
    // (Task 12)", for why this channel.
    internal static class Handshake
    {
        internal const string Key = "structurelib.manifest";
        const float Every = 2f;
        // Steam's limit for one lobby data value (k_cubChatMetadataMax).
        const int MaxValueLength = 8192;
        // Problems shown in the refusal line; the log has all of them.
        const int ShownProblems = 3;

        static float next;
        static ulong publishedLobby;
        // Host: the member data last compared, per crewmate.
        static readonly Dictionary<ulong, string> heard = new();

        /// <summary>This player's registry as the handshake sends it. Kinds come from the id hash, so they are known before the registry freezes.</summary>
        internal static RegistryManifest Manifest()
        {
            var m = new RegistryManifest();
            foreach (var d in MoonStructures.All.OrderBy(d => d.Id, StringComparer.Ordinal))
                m.Structures.Add(new ManifestEntry { Id = d.Id, Value = KindAllocator.KindOf(d.Id), Owner = d.Owner, Version = d.OwnerVersion });
            foreach (var c in MoonStructures.AllCargo.OrderBy(c => c.Id, StringComparer.Ordinal))
            {
                var owner = MoonStructures.CargoOwner(c.Id);
                m.Cargo.Add(new ManifestEntry { Id = c.Id, Value = c.Value, Owner = owner?.Guid ?? "unknown", Version = owner?.Version ?? "unknown" });
            }
            return m;
        }

        // From CoreDriver.Update. Outside a lobby (single player) it only asks MoonSteamLobby.InLobby, every 2 s.
        internal static void Tick()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + Every;
            try
            {
                if (!MoonSteamLobby.InLobby)
                {
                    publishedLobby = 0;
                    heard.Clear();
                    return;
                }
                ulong lobby = MoonSteamLobby.LobbyId;
                if (lobby != publishedLobby) Publish(lobby);
                if (MoonSteamLobby.IsHost) CheckCrew(lobby);
            }
            catch (Exception e)
            {
                CoreLog.Once("handshake-tick", "Handshake tick failed: " + e, 3);
                next = Time.unscaledTime + 30f;
            }
        }

        static void Publish(ulong lobby)
        {
            publishedLobby = lobby;
            var m = Manifest();
            string value = Convert.ToBase64String(HandshakePayload.Encode(m));
            if (value.Length >= MaxValueLength)
            {
                CoreLog.Error($"Handshake: the manifest ({m.Structures.Count} structures, {m.Cargo.Count} cargo) encodes to {value.Length} characters, over Steam's {MaxValueLength}; not published, so crewmates see this player as having no StructureLib.");
                return;
            }
            SteamMatchmaking.SetLobbyMemberData(new CSteamID(lobby), Key, value);
            CoreLog.Info($"Handshake: published this player's manifest to the lobby as {(MoonSteamLobby.IsHost ? "host" : "crewmate")} ({m.Structures.Count} structures, {m.Cargo.Count} cargo, {value.Length} characters)");
        }

        // Host side: compare each crewmate's manifest once per change. The crewmate's own game refuses the join on the
        // same messages; the host only reports them.
        static void CheckCrew(ulong lobby)
        {
            var members = MoonSteamLobby.members;
            if (members == null) return;
            RegistryManifest own = null;
            for (int i = 0; i < members.Count; i++)
            {
                var member = members.get_Item(i);
                if (member == null || member.local) continue;
                string data = SteamMatchmaking.GetLobbyMemberData(new CSteamID(lobby), new CSteamID(member.id), Key) ?? "";
                if (heard.TryGetValue(member.id, out var last) && last == data) continue;
                heard[member.id] = data;
                if (data.Length == 0)
                {
                    CoreLog.Info($"Handshake: crewmate '{member.name}' has published no StructureLib manifest (yet); without StructureLib they will not see modded structures.");
                    continue;
                }
                try
                {
                    own ??= Manifest();
                    var problems = HandshakePayload.Compare(own, HandshakePayload.Decode(Convert.FromBase64String(data)));
                    if (problems.Count == 0) { CoreLog.Info($"Handshake: crewmate '{member.name}' has the same mods."); continue; }
                    CoreLog.Warn($"Handshake: crewmate '{member.name}' has different mods; their game refuses the join: {string.Join("; ", problems)}");
                    MoonCrewSession.Instance?.Notify($"{member.name}: {Summary(problems)}");
                }
                catch (Exception e) { CoreLog.Warn($"Handshake: could not read the manifest of crewmate '{member.name}': {e.Message}"); }
            }
        }

        // Client side, before the host's world offer is accepted. False ends the join (EndJoin already called).
        internal static bool Admit(MoonCrewSession session, MoonCrewSession.CrewJoin j)
        {
            if (j == null || j.lobby == 0) return true;
            ulong hostId = j.host != 0 ? j.host : MoonSteamLobby.HostId;
            string data = SteamMatchmaking.GetLobbyMemberData(new CSteamID(j.lobby), new CSteamID(hostId), Key) ?? "";
            RegistryManifest host;
            if (data.Length == 0)
            {
                CoreLog.Info("Handshake: the host published no StructureLib manifest; comparing with an empty one.");
                host = new RegistryManifest();
            }
            else host = HandshakePayload.Decode(Convert.FromBase64String(data));
            var problems = HandshakePayload.Compare(host, Manifest());
            if (problems.Count == 0)
            {
                CoreLog.Info($"Handshake: the host has the same mods ({host.Structures.Count} structures, {host.Cargo.Count} cargo); joining.");
                return true;
            }
            CoreLog.Warn($"Handshake: refusing the join, mods differ from the host's: {string.Join("; ", problems)}");
            session.EndJoin(j, Summary(problems), true);
            return false;
        }

        static string Summary(IReadOnlyList<string> problems)
        {
            string shown = string.Join("; ", problems.Take(ShownProblems));
            string more = problems.Count > ShownProblems ? $" (+{problems.Count - ShownProblems} more in BepInEx/LogOutput.log)" : "";
            return $"Mods differ from the host's: {shown}{more}";
        }
    }

    [HarmonyPatch]
    internal static class HandshakePatches
    {
        // A failure here must never break a join: log it and let the offer through unchecked.
        [HarmonyPatch(typeof(MoonCrewSession), nameof(MoonCrewSession.Offered)), HarmonyPrefix]
        static bool Offered(MoonCrewSession __instance, MoonCrewSession.CrewJoin j)
        {
            try { return Handshake.Admit(__instance, j); }
            catch (Exception e)
            {
                CoreLog.Error("Handshake check on the world offer failed; the join goes ahead unchecked: " + e);
                return true;
            }
        }
    }
}
