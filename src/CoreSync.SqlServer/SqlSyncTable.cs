using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;

namespace CoreSync.SqlServer
{
    public class SqlSyncTable : SyncTable
    {
        private string? _primaryColumnName;
        private string[]? _primaryKeyColumns;

        internal SqlSyncTable(string name, SyncDirection syncDirection = SyncDirection.UploadAndDownload, string schema = "dbo", bool skipInitialSnapshot = false, string? selectIncrementalQuery = null, string? customSnapshotQuery = null) 
            : base(name, syncDirection, skipInitialSnapshot, selectIncrementalQuery, customSnapshotQuery)
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

        public string NameWithSchemaRaw => $"{(Schema == null ? string.Empty : Schema + ".")}{Name}";

        internal string PrimaryColumnName
        {
            get => _primaryColumnName ?? throw new InvalidOperationException();
            set => _primaryColumnName = value;
        }

        internal SqlPrimaryColumnType PrimaryColumnType => GetPrimaryColumnType(Columns[PrimaryColumnName].DbType);

        private SqlPrimaryColumnType GetPrimaryColumnType(SqlDbType dbType)
        {
            return dbType switch
            {
                SqlDbType.Int or SqlDbType.SmallInt or SqlDbType.BigInt => SqlPrimaryColumnType.Int,
                SqlDbType.Char or SqlDbType.NVarChar or SqlDbType.VarChar or SqlDbType.NChar => SqlPrimaryColumnType.String,
                SqlDbType.UniqueIdentifier => SqlPrimaryColumnType.Guid,
                _ => throw new NotSupportedException($"Table {NameWithSchema} primary key type {dbType}"),
            };
        }

        /// <summary>
        /// Table columns (discovered)
        /// </summary>
        internal Dictionary<string, SqlColumn> Columns { get; set; } = [];

        internal string InitialSnapshotQuery => (CustomSnapshotQuery ?? SelectIncrementalQuery) ?? $@"SELECT * FROM {NameWithSchema}";
       
        private string SelectIncrementalQueryWithFilter
            => SelectIncrementalQuery != null ? $"({SelectIncrementalQuery})" : $"{NameWithSchema}";

        internal string IncrementalAddOrUpdatesQuery => $@"SELECT DISTINCT { string.Join(",", Columns.Keys.Except(SkipColumns).Select(_ => "T.[" + _ + "]"))}, CT.OP AS __OP FROM {SelectIncrementalQueryWithFilter} AS T 
INNER JOIN __CORE_SYNC_CT AS CT ON T.[{PrimaryColumnName}] = CT.[PK_{PrimaryColumnType}] WHERE CT.ID > @version AND CT.TBL = '{NameWithSchema}' AND (CT.SRC IS NULL OR CT.SRC != @sourceId)";

        internal string IncrementalDeletesQuery 
            => $@"SELECT PK_{PrimaryColumnType} AS [{PrimaryColumnName}] FROM __CORE_SYNC_CT WHERE TBL = '{NameWithSchema}' AND ID > @version AND OP = 'D' AND (SRC IS NULL OR SRC != @sourceId)";

        internal string SelectExistingQuery => $@"SELECT COUNT (*) FROM {NameWithSchema}
WHERE [{PrimaryColumnName}] = @PrimaryColumnParameter";

        internal string[] SkipColumns { get; set; } = [];

        internal string[] SkipColumnsOnInsertOrUpdate { get; set; } = [];

        internal bool ForceReloadInsertedRecords { get; set; } = false;

        internal bool HasTableIdentityColumn { get; set; }

        internal string[] PrimaryKeyColumns
        {
            get => _primaryKeyColumns ?? throw new InvalidOperationException();
            set => _primaryKeyColumns = value;
        }

        internal void SetupCommand(SqlCommand cmd, ChangeType itemChangeType, Dictionary<string, SyncItemValue> syncItemValues)
        {
            var allColumnsExceptSkipColumns = Columns.Keys.Except(SkipColumns.Concat(SkipColumnsOnInsertOrUpdate)).ToArray();

            //take values only for existing columns (server table schema could be not in sync with local table schema)
            var allSyncItems = syncItemValues
                .Where(value => allColumnsExceptSkipColumns.Any(_ => StringComparer.OrdinalIgnoreCase.Compare(_, value.Key) == 0))
                .ToList();

            var allSyncItemsExceptPrimaryKey = allSyncItems.Where(_ => !PrimaryKeyColumns.Any(kc => kc == _.Key)).ToArray();

            switch (itemChangeType)
            {
                case ChangeType.Insert:
                    {
                        var identityInsertCommand = string.Empty;
                        if (IdentityInsert == IdentityInsertMode.Auto)
                        {
                            if (HasTableIdentityColumn)
                            {
                                identityInsertCommand = $"SET IDENTITY_INSERT {NameWithSchema} ON";
                            }
                        }
                        else if (IdentityInsert == IdentityInsertMode.On)
                        {
                            identityInsertCommand = $"SET IDENTITY_INSERT {NameWithSchema} ON";
                        }
                        else if (IdentityInsert == IdentityInsertMode.Off)
                        {
                            identityInsertCommand = $"SET IDENTITY_INSERT {NameWithSchema} OFF";
                        }

                        cmd.CommandText = $@"{identityInsertCommand}
BEGIN TRY 
INSERT INTO {NameWithSchema} ({string.Join(", ", allSyncItems.Select(_ => "[" + _.Key + "]"))}) 
VALUES ({string.Join(", ", allSyncItems.Select((_, index) => $"@p{index}"))});
END TRY  
BEGIN CATCH  
PRINT ERROR_MESSAGE()
END CATCH
"; //{(setIdentityInsertOn ? $"SET IDENTITY_INSERT {NameWithSchema} OFF" : string.Empty)}


                        int pIndex = 0;
                        foreach (var valueItem in allSyncItems)
                        {
                            cmd.Parameters.Add(new SqlParameter($"@p{pIndex}", Columns[valueItem.Key].DbType)
                            {
                                Value = Utils.ConvertToSqlType(valueItem.Value, Columns[valueItem.Key].DbType)
                            });
                            //cmd.Parameters.AddWithValue("@" + valueItem.Key.Replace(" ", "_"), valueItem.Value.Value ?? DBNull.Value);
                            pIndex++;
                        }
                    }
                    break;

                case ChangeType.Update:
                    {
                        cmd.CommandText = $@"BEGIN TRY 
UPDATE {NameWithSchema}
SET {string.Join(", ", allSyncItemsExceptPrimaryKey.Select((_, index) => $"[{_.Key}] = @p{index}"))}
WHERE {NameWithSchema}.[{PrimaryColumnName}] = @PrimaryColumnParameter
AND (@sync_force_write = 1 OR (SELECT MAX(ID) FROM __CORE_SYNC_CT WHERE PK_{PrimaryColumnType} = @PrimaryColumnParameter AND TBL = '{NameWithSchema}') <= @last_sync_version)
END TRY  
BEGIN CATCH  
PRINT ERROR_MESSAGE()
END CATCH";
                        cmd.Parameters.Add(new SqlParameter("@PrimaryColumnParameter", Columns[PrimaryColumnName].DbType)
                        {
                            Value = Utils.ConvertToSqlType(syncItemValues[PrimaryColumnName], Columns[PrimaryColumnName].DbType)
                        });

                        int pIndex = 0;
                        foreach (var valueItem in allSyncItemsExceptPrimaryKey)
                        {
                            cmd.Parameters.Add(new SqlParameter($"@p{pIndex}", Columns[valueItem.Key].DbType)
                            {
                                Value = Utils.ConvertToSqlType(valueItem.Value, Columns[valueItem.Key].DbType)
                            });
                            //cmd.Parameters.AddWithValue("@" + valueItem.Key.Replace(" ", "_"), valueItem.Value.Value ?? DBNull.Value);
                            pIndex++;
                        }

                    }
                    break;

                case ChangeType.Delete:
                    {
                        cmd.CommandText = $@"BEGIN TRY 
DELETE FROM {NameWithSchema}
WHERE {NameWithSchema}.[{PrimaryColumnName}] = @PrimaryColumnParameter
AND (@sync_force_write = 1 OR (SELECT MAX(ID) FROM __CORE_SYNC_CT WHERE PK_{PrimaryColumnType} = @PrimaryColumnParameter AND TBL = '{NameWithSchema}') <= @last_sync_version)
END TRY  
BEGIN CATCH  
PRINT ERROR_MESSAGE()
END CATCH";

                        cmd.Parameters.Add(new SqlParameter("@PrimaryColumnParameter", Columns[PrimaryColumnName].DbType)
                        {
                            Value = Utils.ConvertToSqlType(syncItemValues[PrimaryColumnName], Columns[PrimaryColumnName].DbType)
                        });

                    }
                    break;
            }
        }

        internal IdentityInsertMode IdentityInsert { get; set; }
    }

    public enum IdentityInsertMode
    {
        /// <summary>
        /// Auto discover the identity column
        /// </summary>
        Auto,

        /// <summary>
        /// Set IDENTITY_INSERT to ON
        /// </summary>
        On,

        /// <summary>
        /// Set IDENTITY_INSERT to OFF
        /// </summary>
        Off,


        /// <summary>
        /// Do not set IDENTITY_INSERT
        /// </summary>
        Disabled
    }
}
