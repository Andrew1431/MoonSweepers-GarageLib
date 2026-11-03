namespace StructureLib
{
    /// <summary>What a load does with one saved production record.</summary>
    public enum ProductionRestoreAction
    {
        /// <summary>Inject it: the machine comes back with the saved slots.</summary>
        Restore,
        /// <summary>
        /// Inject an empty state for the machine (sized by the current definition) and keep the record verbatim, marked
        /// superseded so it is never offered again: the definition's slots changed since it was saved.
        /// </summary>
        RestoreEmpty,
        /// <summary>Inject nothing for it and keep it in the sidecar as it is.</summary>
        Keep,
    }

    public static class ProductionPlan
    {
        /// <summary>
        /// The decision for one record. <paramref name="mismatch"/> is ProcessorLayout.RecordMismatch's answer (null when
        /// the record fits the definition). A machine whose structure is in the world gets a state either way, so a
        /// changed slot layout never leaves it without one (RestoreProduction only creates states for snapshot entries).
        /// </summary>
        public static ProductionRestoreAction Decide(bool processorRegistered, bool structureInWorld, bool depotTaken, bool superseded, string mismatch)
        {
            if (!processorRegistered || !structureInWorld || depotTaken || superseded) return ProductionRestoreAction.Keep;
            return mismatch == null ? ProductionRestoreAction.Restore : ProductionRestoreAction.RestoreEmpty;
        }

        /// <summary>
        /// Inner store lists a RestoreEmpty snapshot gets: one per slot of the current layout, so the game's
        /// RestoreProduction never indexes past the list.
        /// </summary>
        public static int EmptySlotCount(System.Collections.Generic.IReadOnlyCollection<string> currentSlotCargoIds) => currentSlotCargoIds?.Count ?? 0;
    }
}
