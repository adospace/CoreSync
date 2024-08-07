using CoreSync;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoreSync.Http;


public class BulkChangeSetUploadItem
{
    public Guid SessionId { get; set; }

    public IReadOnlyList<SyncItem> Items { get; set; } = [];
}
