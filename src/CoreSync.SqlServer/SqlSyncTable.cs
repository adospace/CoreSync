using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync.SqlServer
{
    public class SqlSyncTable
    {
        internal SqlSyncTable(string name, bool bidirectional = true, string schema = "dbo")
        {
            Name = name;
            Bidirectional = bidirectional;
            Schema = schema;
        }

        public string Name { get; }

        public bool Bidirectional { get; }
        public string Schema { get; }
        internal string InitialDataQuery { get; set; }

        internal string IncrementalInsertQuery { get; set; }


    }
}
