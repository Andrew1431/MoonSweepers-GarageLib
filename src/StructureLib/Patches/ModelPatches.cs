using System;
using HarmonyLib;
using MoonCleaners;
using UnityEngine;

namespace StructureLib.Patches
{
    // The built view. MoonDepotConstruction.Spawn(data) instantiates a depot's view on build and on restore; its root is
    // __instance.ViewOf(data.id). For a modded kind the postfix applies DecorateModel, names the root
    // "ModStructure_<id>" and raises MoonStructures.Built. BuildModel is honoured earlier, as the cloned definition's
    // prefab (Injection.DefinitionFor), so the game instantiates the mod's model itself.
    [HarmonyPatch]
    internal static class ModelPatches
    {
        internal const string ViewPrefix = "ModStructure_";

        [HarmonyPatch(typeof(MoonDepotConstruction), nameof(MoonDepotConstruction.Spawn)), HarmonyPostfix]
        static void Spawn(MoonDepotConstruction __instance, MoonDepotSnapshot data, bool __result)
        {
            if (data == null) return;
            var d = Remap.Live(data.kind);
            if (d == null) return;
            try
            {
                var view = __instance.ViewOf(data.id);
                if (view == null)
                {
                    CoreLog.Once("spawn-noview:" + d.Id, $"Spawn(depot {data.id}, '{d.Id}') returned {__result} with no view; nothing decorated", 3);
                    return;
                }
                // A view this postfix already handled (Spawn called again for the same depot) is left alone.
                string name = ViewPrefix + d.Id;
                if (view.name == name) return;

                var world = __instance.world ?? CoreState.World;
                var record = world?.depots != null && world.depots.ContainsKey(data.id) ? world.depots.get_Item(data.id) : data;
                if (d.DecorateModel != null)
                {
                    var ctx = new ModelContext { Definition = d, Parent = view.transform.parent, Donor = world?.BuildDefinition(d.BaseStructure) };
                    try { d.DecorateModel(view, ctx); }
                    catch (Exception e) { CoreLog.Error($"'{d.Id}': DecorateModel threw on depot {data.id}; the view stays undecorated: {e}"); }
                }
                string was = view.name;
                view.name = name;
                CoreLog.Info($"Spawned modded structure '{d.Id}' depot={data.id} view '{was}' -> '{name}' at ({record.x:F1},{record.y:F1},{record.z:F1})");
                MoonStructures.RaiseBuilt(new StructureBuilt { Definition = d, DepotId = data.id, Record = record, View = view });
            }
            catch (Exception e) { CoreLog.Once("spawn:" + d.Id, $"Spawn postfix for depot {data.id} ('{d.Id}') threw: {e}", 3); }
        }
    }
}
