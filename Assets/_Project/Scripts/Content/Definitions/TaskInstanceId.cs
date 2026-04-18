namespace OSE.Content
{
    /// <summary>
    /// Helpers for the multi-instance <see cref="TaskOrderEntry.id"/> convention
    /// introduced by Phase G.2.
    ///
    /// <para>A Part task's <c>TaskOrderEntry.id</c> is the <c>partId</c> for its
    /// first instance in a step's <c>taskOrder</c>, and <c>partId#N</c> (where
    /// N ≥ 2) for subsequent instances. This lets the same part participate in
    /// multiple tasks within one step (place → tool-adjust → tool-adjust-again)
    /// with each task owning its own end-transform.</para>
    ///
    /// <para>Non-Part task ids (<c>action_&lt;targetId&gt;</c>, wire ids, etc.) do
    /// not use this convention and pass through all helpers unchanged since they
    /// never contain the <see cref="Sep"/> character.</para>
    ///
    /// <para><b>Rule of thumb</b>: any code that compares <c>entry.id</c> to a
    /// <c>partId</c>, uses it as a key into a partId-indexed map, or passes it
    /// to a method that expects a bare partId, must call
    /// <see cref="ToPartId"/> first.</para>
    /// </summary>
    public static class TaskInstanceId
    {
        /// <summary>Separator character between partId and instance number.</summary>
        public const char Sep = '#';

        /// <summary>
        /// Strips the <c>#N</c> instance suffix from a Part-task entry id,
        /// returning the bare <c>partId</c>. For entry ids without a suffix
        /// (first instance, or non-Part tasks) the input is returned unchanged.
        /// </summary>
        public static string ToPartId(string entryId)
        {
            if (string.IsNullOrEmpty(entryId)) return entryId;
            int hash = entryId.IndexOf(Sep);
            return hash < 0 ? entryId : entryId.Substring(0, hash);
        }

        /// <summary>
        /// Returns the 1-based instance number encoded in a Part-task entry id.
        /// Bare <c>partId</c> → 1. <c>partId#2</c> → 2. Malformed suffixes
        /// (non-numeric, zero, negative) fall back to 1 so callers always get a
        /// sane positive integer.
        /// </summary>
        public static int ToInstance(string entryId)
        {
            if (string.IsNullOrEmpty(entryId)) return 1;
            int hash = entryId.IndexOf(Sep);
            if (hash < 0 || hash >= entryId.Length - 1) return 1;
            if (int.TryParse(entryId.Substring(hash + 1), out int n) && n > 0) return n;
            return 1;
        }

        /// <summary>
        /// Builds a Part-task entry id for the given <paramref name="partId"/> and
        /// 1-based <paramref name="instance"/> number. Instance 1 returns the bare
        /// partId (preserves backward compat with single-instance content). Higher
        /// instances get the <c>#N</c> suffix.
        /// </summary>
        public static string Build(string partId, int instance)
        {
            if (string.IsNullOrEmpty(partId) || instance <= 1) return partId;
            return partId + Sep + instance;
        }

        /// <summary>
        /// True when <paramref name="entryId"/> carries a <c>#N</c> instance suffix
        /// (i.e. instance ≥ 2). Useful for "is this a secondary instance?" checks
        /// in UI code without a full parse.
        /// </summary>
        public static bool HasInstanceSuffix(string entryId)
            => !string.IsNullOrEmpty(entryId) && entryId.IndexOf(Sep) >= 0;
    }
}
