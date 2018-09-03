using System;
using System.Collections.Generic;

namespace CoreSync.Sqlite
{
    public class SqliteSyncTable : SyncTable
    {
        internal SqliteSyncTable(string name, Type recordType = null, bool bidirectional = true, string schema = "dbo") : base(name)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(name, nameof(name));
            Validate.NotNullOrEmptyOrWhiteSpace(schema, nameof(schema));

            Bidirectional = bidirectional;
            Schema = schema;
            RecordType = recordType;
        }

        /// <summary>
        /// Bidirectional vs upload-only table synchronization (not supported yet)
        /// </summary>
        public bool Bidirectional { get; }

        /// <summary>
        /// Schema of table (Default: main)
        /// </summary>
        public string Schema { get; }

        /// <summary>
        /// Record type can be useful to cast back to correct types record values 
        /// when are read from Sqlite database
        /// </summary>
        public Type RecordType { get; }

        /// <summary>
        /// Table columns (discovered)
        /// </summary>
        internal List<SqliteColumn> Columns { get; set; } = new List<SqliteColumn>();

        internal string InsertQuery { get; set; }

        internal string UpdateQuery { get; set; }

        internal string DeleteQuery { get; set; }

    }
}