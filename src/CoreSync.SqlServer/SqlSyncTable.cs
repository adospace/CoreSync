using System;
using System.Collections.Generic;
using System.Data;
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

        internal string PrimaryColumnName { get; set; }

        internal SqlPrimaryColumnType PrimaryColumnType => GetPrimaryColumnType(Columns[PrimaryColumnName].DbType);

        private SqlPrimaryColumnType GetPrimaryColumnType(SqlDbType dbType)
        {
            switch (dbType)
            {
                case SqlDbType.Int:
                case SqlDbType.SmallInt:
                case SqlDbType.BigInt:
                    return SqlPrimaryColumnType.Int;
                case SqlDbType.Char:
                case SqlDbType.NVarChar:
                case SqlDbType.VarChar:
                case SqlDbType.NChar:
                    return SqlPrimaryColumnType.String;
                case SqlDbType.UniqueIdentifier:
                    return SqlPrimaryColumnType.Guid;

            }

            throw new NotSupportedException($"Table {NameWithSchema} primary key type {dbType}");
        }

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
