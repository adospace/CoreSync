using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoreSync.Http;

public class BulkChangeSetDownloadItem
{
    public Guid SessionId { get; set; }

    public int Skip { get; set; }

    public int Take { get; set; }
}
