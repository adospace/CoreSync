using CoreSync;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoreSync.Http;

public class BulkSyncChangeSet
{
    public Guid SessionId { get; set; }

    public int TotalChanges { get; set; }

    public SyncAnchor SourceAnchor { get; set; } = default!;

    public SyncAnchor TargetAnchor { get; set; } = default!;

    public Dictionary<string, int> ChangesByTable { get; set; } = default!;

}
