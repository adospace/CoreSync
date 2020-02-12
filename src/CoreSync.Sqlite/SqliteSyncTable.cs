using System;
using System.Collections.Generic;
using System.Linq;

namespace CoreSync.Sqlite
{
    public class SqliteSyncTable : SyncTable
    {
        internal SqliteSyncTable(string name, Type recordType = null, SyncDirection syncDirection = SyncDirection.UploadAndDownload) : base(name, syncDirection)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(name, nameof(name));

            RecordType = recordType;
        }

        /// <summary>
        /// Record type can be useful to cast back to correct types record values 
        /// when are read from Sqlite database
        /// </summary>
        public Type RecordType { get; }

        public IEnumerable<string> PrimaryColumnNames => Columns.Where(_ => _.IsPrimaryKey).Select(_ => _.Name);

        /// <summary>
        /// Table columns (discovered)
        /// </summary>
        internal List<SqliteColumn> Columns { get; set; } = new List<SqliteColumn>();

        internal string InitialSnapshotQuery { get; set; }
        
        internal string IncrementalAddOrUpdatesQuery { get; set; }
        
        internal string IncrementalDeletesQuery { get; set; }
        
        internal string InsertQuery { get; set; }

        internal string UpdateQuery { get; set; }

        internal string DeleteQuery { get; set; }

    }
}