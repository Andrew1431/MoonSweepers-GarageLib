namespace StructureLib
{
    /// <summary>
    /// Ordering of sidecar commits. Saves can overlap (each runs on its own worker thread), so a write prepared from an
    /// older save snapshot may reach its commit after a newer one. Every write carries the order of its snapshot and its
    /// own .tmp file; a commit older than the last one committed to the same file is dropped.
    /// </summary>
    public static class CommitOrder
    {
        /// <summary>True if a write of snapshot order <paramref name="order"/> may replace the file last committed at <paramref name="lastCommitted"/> (-1: none yet).</summary>
        public static bool ShouldCommit(long order, long lastCommitted) => order >= lastCommitted;

        /// <summary>The .tmp of one write: unique per write, so overlapping writes never share a file.</summary>
        public static string TmpPath(string path, long write) => $"{path}.{write}.tmp";
    }
}
