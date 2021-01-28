using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CoreSync.Sqlite
{
    public class SqliteSyncTable : SyncTable
    {
        internal SqliteSyncTable(string name, Type recordType = null, SyncDirection syncDirection = SyncDirection.UploadAndDownload, bool skipInitialSnapshot = false, string selectQuery = null)
            : base(name, syncDirection, skipInitialSnapshot, selectQuery)
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

        internal string InitialSnapshotQuery => SelectQuery ?? $@"SELECT * FROM [{Name}]";

        private string SelectQueryWithFilter
            => SelectQuery != null ? $"({SelectQuery})" : $"[{Name}]";

        internal string IncrementalAddOrUpdatesQuery => $@"SELECT DISTINCT {string.Join(",", Columns.Select(_ => "T.[" + _.Key + "]"))}, CT.OP AS __OP 
                                FROM {SelectQueryWithFilter} AS T INNER JOIN __CORE_SYNC_CT AS CT ON T.[{PrimaryColumnName}] = CT.PK_{PrimaryColumnType} WHERE CT.ID > @version AND CT.TBL = '{Name}' AND (CT.SRC IS NULL OR CT.SRC != @sourceId)";

        internal string IncrementalDeletesQuery => 
            $@"SELECT PK_{PrimaryColumnType} AS [{PrimaryColumnName}] FROM [__CORE_SYNC_CT] WHERE TBL = '{Name}' AND ID > @version AND OP = 'D' AND (SRC IS NULL OR SRC != @sourceId)";



        internal string SelectExistingQuery => $@"SELECT COUNT(*) FROM [{Name}] 
            WHERE [{PrimaryColumnName}] = @{PrimaryColumnName.Replace(' ', '_')}";

        internal void SetupCommand(SqliteCommand cmd, ChangeType itemChangeType, Dictionary<string, SyncItemValue> syncItemValues)
        {
            //take values only for existing columns (server table schema could be not in sync with local table schema)
            var valuesForValidColumns = syncItemValues
                .Where(value => Columns.Any(_ => StringComparer.OrdinalIgnoreCase.Compare(_.Key, value.Key) == 0))
                .ToList();

            switch (itemChangeType)
            {
                case ChangeType.Insert:
                    cmd.CommandText = $@"INSERT OR IGNORE INTO [{Name}] ({string.Join(", ", valuesForValidColumns.Select(_ => "[" + _.Key + "]"))}) 
VALUES ({string.Join(", ", valuesForValidColumns.Select(_ => "@" + _.Key.Replace(' ', '_')))});";
                    break;

                case ChangeType.Update:
                    cmd.CommandText = $@"UPDATE [{Name}]
SET {string.Join(", ", valuesForValidColumns.Select(_ => "[" + _.Key + "] = @" + _.Key.Replace(' ', '_')))}
WHERE [{Name}].[{PrimaryColumnName}] = @{PrimaryColumnName.Replace(' ', '_')}
AND (@sync_force_write = 1 OR (SELECT MAX(ID) FROM __CORE_SYNC_CT WHERE PK_{PrimaryColumnType} = @{PrimaryColumnName.Replace(' ', '_')} AND TBL = '{Name}') <= @last_sync_version)";
                    break;

                case ChangeType.Delete:
                    cmd.CommandText = $@"DELETE FROM [{Name}]
WHERE [{Name}].[{PrimaryColumnName}] = @{PrimaryColumnName.Replace(' ', '_')}
AND (@sync_force_write = 1 OR (SELECT MAX(ID) FROM __CORE_SYNC_CT WHERE PK_{PrimaryColumnType} = @{PrimaryColumnName.Replace(' ', '_')} AND TBL = '{Name}') <= @last_sync_version)";
                    break;
            }

            foreach (var valueItem in valuesForValidColumns)
                cmd.Parameters.Add(new SqliteParameter("@" + valueItem.Key.Replace(" ", "_"), valueItem.Value.Value ?? DBNull.Value));
        }
    }
}