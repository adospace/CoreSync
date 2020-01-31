using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync.SqlServer
{
    public class SqlSyncTable : SyncTable
    {
        internal SqlSyncTable(string name, bool bidirectional = true, string schema = "dbo") : base(name)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(name, nameof(name));
            Validate.NotNullOrEmptyOrWhiteSpace(schema, nameof(schema));

            Bidirectional = bidirectional;
            Schema = schema;
        }

        /// <summary>
        /// Bidirectional vs upload-only table synchronization (not supported yet)
        /// </summary>
        public bool Bidirectional { get; }

        /// <summary>
        /// Schema of table (Default: dbo)
        /// </summary>
        public string Schema { get; }

        public string NameWithSchema => $"{(Schema == null ? string.Empty : "[" + Schema + "].")}[{Name}]";

        internal string InitialDataQuery { get; set; }

        internal string IncrementalDataQuery { get; set; }

        internal string InsertQuery { get; set; }

        internal string UpdateQuery { get; set; }

        internal string DeleteQuery { get; set; }
    }
}
