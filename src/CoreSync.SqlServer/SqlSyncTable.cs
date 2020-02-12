using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync.SqlServer
{
    public class SqlSyncTable : SyncTable
    {
        internal SqlSyncTable(string name, SyncDirection syncDirection = SyncDirection.UploadAndDownload, string schema = "dbo") : base(name, syncDirection)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(name, nameof(name));
            Validate.NotNullOrEmptyOrWhiteSpace(schema, nameof(schema));
            Schema = schema;
        }

        /// <summary>
        /// Schema of table (Default: dbo)
        /// </summary>
        public string Schema { get; }

        public string NameWithSchema => $"{(Schema == null ? string.Empty : "[" + Schema + "].")}[{Name}]";

        internal string[] PrimaryColumnNames { get; set; }

        /// <summary>
        /// Table columns (discovered)
        /// </summary>
        internal Dictionary<string, SqlColumn> Columns { get; set; } = new Dictionary<string, SqlColumn>();

        internal string InitialSnapshotQuery { get; set; }

        internal string IncrementalAddOrUpdatesQuery { get; set; }

        internal string IncrementalDeletesQuery { get; set; }

        internal string InsertQuery { get; set; }

        internal string UpdateQuery { get; set; }

        internal string DeleteQuery { get; set; }
    }
}
