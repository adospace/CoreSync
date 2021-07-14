using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;

namespace CoreSync.SqlServer
{
    public class SqlSyncTable : SyncTable
    {
        internal SqlSyncTable(string name, SyncDirection syncDirection = SyncDirection.UploadAndDownload, string schema = "dbo", bool skipInitialSnapshot = false, string selectIncrementalQuery = null, string customSnapshotQuery = null) 
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

        internal string InitialSnapshotQuery => (CustomSnapshotQuery ?? SelectIncrementalQuery) ?? $@"SELECT * FROM {NameWithSchema}";
       
        private string SelectIncrementalQueryWithFilter
            => SelectIncrementalQuery != null ? $"({SelectIncrementalQuery})" : $"{NameWithSchema}";

        internal string IncrementalAddOrUpdatesQuery => $@"SELECT DISTINCT { string.Join(",", Columns.Keys.Except(SkipColumns).Select(_ => "T.[" + _ + "]"))}, CT.OP AS __OP FROM {SelectIncrementalQueryWithFilter} AS T 
INNER JOIN __CORE_SYNC_CT AS CT ON T.[{PrimaryColumnName}] = CT.[PK_{PrimaryColumnType}] WHERE CT.ID > @version AND CT.TBL = '{NameWithSchema}' AND (CT.SRC IS NULL OR CT.SRC != @sourceId)";

        internal string IncrementalDeletesQuery 
            => $@"SELECT PK_{PrimaryColumnType} AS [{PrimaryColumnName}] FROM __CORE_SYNC_CT WHERE TBL = '{NameWithSchema}' AND ID > @version AND OP = 'D' AND (SRC IS NULL OR SRC != @sourceId)";

        internal string SelectExistingQuery => $@"SELECT COUNT (*) FROM {NameWithSchema}
WHERE [{PrimaryColumnName}] = @PrimaryColumnParameter";

        internal string[] SkipColumns { get; set; } = new string[] { };

        internal bool HasTableIdentityColumn { get; set; }

        internal string[] PrimaryKeyColumns { get; set; }

        internal void SetupCommand(SqlCommand cmd, ChangeType itemChangeType, Dictionary<string, SyncItemValue> syncItemValues)
        {
            var allColumnsExceptSkipColumns = Columns.Keys.Except(SkipColumns).ToArray();

            //take values only for existing columns (server table schema could be not in sync with local table schema)
            var allSyncItems = syncItemValues
                .Where(value => allColumnsExceptSkipColumns.Any(_ => StringComparer.OrdinalIgnoreCase.Compare(_, value.Key) == 0))
                .ToList();

            var allSyncItemsExceptPrimaryKey = allSyncItems.Where(_ => !PrimaryKeyColumns.Any(kc => kc == _.Key)).ToArray();

            switch (itemChangeType)
            {
                case ChangeType.Insert:
                    {
                        cmd.CommandText = $@"{(HasTableIdentityColumn ? $"SET IDENTITY_INSERT {NameWithSchema} ON" : string.Empty)}
BEGIN TRY 
INSERT INTO {NameWithSchema} ({string.Join(", ", allSyncItems.Select(_ => "[" + _.Key + "]"))}) 
VALUES ({string.Join(", ", allSyncItems.Select((_, index) => $"@p{index}"))});
END TRY  
BEGIN CATCH  
PRINT ERROR_MESSAGE()
END CATCH
{(HasTableIdentityColumn ? $"SET IDENTITY_INSERT {NameWithSchema} OFF" : string.Empty)}";


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

    }
}
