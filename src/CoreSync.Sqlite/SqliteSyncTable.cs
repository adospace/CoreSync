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

        internal string PrimaryColumnName => Columns.First(_ => _.Value.IsPrimaryKey).Value.Name;

        internal SqlitePrimaryColumnType PrimaryColumnType => GetPrimaryColumnType(Columns[PrimaryColumnName].Type);

        private SqlitePrimaryColumnType GetPrimaryColumnType(string type)
        {
            switch (type)
            {
                case "INTEGER":
                    return SqlitePrimaryColumnType.Int;
                case "TEXT":
                    return SqlitePrimaryColumnType.Text;
                case "BLOB":
                    return SqlitePrimaryColumnType.Blob;

            }

            throw new NotSupportedException($"Table {Name} primary key type '{type}'");
        }

        /// <summary>
        /// Table columns (discovered)
        /// </summary>
        internal Dictionary<string, SqliteColumn> Columns { get; set; } = new Dictionary<string, SqliteColumn>();

        internal string InitialSnapshotQuery { get; set; }
        
        internal string IncrementalAddOrUpdatesQuery { get; set; }
        
        internal string IncrementalDeletesQuery { get; set; }
        
        internal string InsertQuery { get; set; }

        internal string UpdateQuery { get; set; }

        internal string DeleteQuery { get; set; }

    }
}