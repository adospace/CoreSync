using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CoreSync
{
    /// <summary>
    /// Base class for sync configurations that hold the set of tables participating in synchronization.
    /// </summary>
    public abstract class SyncConfiguration(SyncTable[] tables)
    {
        /// <summary>
        /// Gets the tables registered for synchronization.
        /// </summary>
        public SyncTable[] Tables { get; } = tables;

        /// <summary>
        /// Returns the subset of <see cref="Tables"/> that match the requested table names,
        /// preserving the order of <paramref name="requestedTables"/>. When <paramref name="requestedTables"/>
        /// is <c>null</c>, all configured tables are returned.
        /// </summary>
        /// <param name="requestedTables">
        /// An optional list of table names to filter by. All names must exist in the configuration.
        /// </param>
        /// <returns>The filtered (or full) table array.</returns>
        /// <exception cref="ArgumentException">
        /// One or more requested table names do not exist in the configuration.
        /// </exception>
        public SyncTable[] ResolveTableFilter(string[]? requestedTables)
        {
            if (requestedTables == null || requestedTables.Length == 0)
                return Tables;

            var requestedSet = new HashSet<string>(requestedTables, StringComparer.OrdinalIgnoreCase);
            var configuredNames = new HashSet<string>(Tables.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);

            var unknown = requestedSet.Where(name => !configuredNames.Contains(name)).ToArray();
            if (unknown.Length > 0)
                throw new ArgumentException($"The following tables are not present in the sync configuration: {string.Join(", ", unknown)}");

            var tablesByName = Tables.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
            return requestedTables.Select(name => tablesByName[name]).ToArray();
        }
    }
}
