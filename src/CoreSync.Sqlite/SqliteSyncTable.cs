using System;
using System.Collections.Generic;

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
        public string SelectIncrementalAddsOrUpdates { get; internal set; }
        public string SelectIncrementalDeletes { get; internal set; }

        /// <summary>
        /// Table columns (discovered)
        /// </summary>
        internal List<SqliteColumn> Columns { get; set; } = new List<SqliteColumn>();

        internal string InsertQuery { get; set; }

        internal string UpdateQuery { get; set; }

        internal string DeleteQuery { get; set; }

    }
}