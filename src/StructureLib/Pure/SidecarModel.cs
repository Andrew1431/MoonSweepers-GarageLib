using System.Collections.Generic;

namespace StructureLib
{
    // structurelib.json, beside the vanilla save. Everything a modded structure needs that the vanilla save cannot carry
    // (a kind above MaxStructureKind fails the game's own read-back check). Records name structures and cargo by their
    // string ids, never by kind numbers or cargo values, so they survive a mod being removed and installed again.
    // Owners are stored verbatim (Steam ids): the file never leaves the save folder, and logs redact them.

    /// <summary>The whole sidecar file.</summary>
    public sealed class SidecarFile
    {
        public int Version { get; set; } = 1;
        /// <summary>Local time of the write, ISO 8601 ("s" format).</summary>
        public string Written { get; set; }
        public List<StructureRecord> Structures { get; set; } = new();
        /// <summary>Production state of modded processors (Task 10).</summary>
        public List<ProductionRecord> Production { get; set; } = new();
        /// <summary>Canisters holding modded cargo.</summary>
        public List<CanisterRecord> Canisters { get; set; } = new();
    }

    /// <summary>One built modded structure: a MoonDepotSnapshot with its kind replaced by the structure id.</summary>
    public sealed class StructureRecord
    {
        public string Id { get; set; }
        public int DepotId { get; set; }
        public string Owner { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Yaw { get; set; }
        public bool PlacedPose { get; set; }
        public float Qx { get; set; }
        public float Qy { get; set; }
        public float Qz { get; set; }
        public float Qw { get; set; }
        public int Support { get; set; }
        public double Stored { get; set; }
        public int Vehicles { get; set; }
        public int Style { get; set; }
        public List<ShareRecord> Contributions { get; set; } = new();
    }

    /// <summary>An owner's share: a MoonWalletSnapshot (player, dollars, pendingKilograms).</summary>
    public sealed class ShareRecord
    {
        public string Owner { get; set; }
        /// <summary>MoonWalletSnapshot.dollars (in a production slot: kilograms).</summary>
        public double Amount { get; set; }
        /// <summary>MoonWalletSnapshot.pendingKilograms, kept so the round trip loses nothing.</summary>
        public double Pending { get; set; }
    }

    /// <summary>Production state of one modded processor (MoonProductionSnapshot). Used from Task 10.</summary>
    public sealed class ProductionRecord
    {
        /// <summary>Structure id of the machine.</summary>
        public string Id { get; set; }
        public int DepotId { get; set; }
        public float Progress { get; set; }
        public bool Running { get; set; }
        public double Charge { get; set; }
        public List<ProductionSlotRecord> Slots { get; set; } = new();
    }

    /// <summary>One processor slot: its cargo id and the per-owner shares held in it.</summary>
    public sealed class ProductionSlotRecord
    {
        /// <summary>Cargo id held by the slot (a registered cargo id, or the game's own cargo name).</summary>
        public string Cargo { get; set; }
        public List<ShareRecord> Shares { get; set; } = new();
    }

    /// <summary>
    /// A canister holding modded cargo: a MoonCanisterSnapshot (id, owner, kilograms, capacity, pose) plus the cargo id
    /// that replaces the cargo byte the vanilla save keeps in a separate MoonCargoSnapshot list.
    /// </summary>
    public sealed class CanisterRecord
    {
        public int Id { get; set; }
        public string Owner { get; set; }
        /// <summary>Registered cargo id (CargoDefinition.Id), never the byte value.</summary>
        public string CargoId { get; set; }
        public double Kilograms { get; set; }
        public double Capacity { get; set; }
        /// <summary>x, y, z.</summary>
        public float[] Position { get; set; }
        /// <summary>ux, uy, uz.</summary>
        public float[] Up { get; set; }
        /// <summary>qx, qy, qz, qw.</summary>
        public float[] Rotation { get; set; }
        public bool PhysicalPose { get; set; }
    }
}
