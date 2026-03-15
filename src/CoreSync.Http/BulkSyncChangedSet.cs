using CoreSync;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoreSync.Http;

/// <summary>
/// Represents the metadata for a bulk synchronization session, including anchors and per-table change counts.
/// Used to coordinate paginated change transfers between client and server.
/// </summary>
public class BulkSyncChangeSet
{
    /// <summary>
    /// Gets or sets the unique identifier for this bulk sync session.
    /// </summary>
    public Guid SessionId { get; set; }

    /// <summary>
    /// Gets or sets the total number of changes across all tables in this session.
    /// </summary>
    public int TotalChanges { get; set; }

    /// <summary>
    /// Gets or sets the version anchor of the store that produced these changes.
    /// </summary>
    public SyncAnchor SourceAnchor { get; set; } = default!;

    /// <summary>
    /// Gets or sets the version anchor representing the last known version from the source store.
    /// </summary>
    public SyncAnchor TargetAnchor { get; set; } = default!;

    /// <summary>
    /// Gets or sets a dictionary mapping table names to the number of changes in each table.
    /// </summary>
    public Dictionary<string, int> ChangesByTable { get; set; } = default!;

}
