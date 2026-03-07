using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace CoreSync.SqlServerCT
{
    public class SqlServerCTSyncTable : SyncTable
    {
        private string? _primaryColumnName;
        private string[]? _primaryKeyColumns;

        internal SqlServerCTSyncTable(string name, SyncDirection syncDirection = SyncDirection.UploadAndDownload, string schema = "dbo", bool skipInitialSnapshot = false, string? selectIncrementalQuery = null, string? customSnapshotQuery = null)
            : base(name, syncDirection, skipInitialSnapshot, selectIncrementalQuery, customSnapshotQuery)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(name, nameof(name));
            Validate.NotNullOrEmptyOrWhiteSpace(schema, nameof(schema));
            Schema = schema;
        }

        public string Schema { get; }

        public string NameWithSchema => $"{(Schema == null ? string.Empty : "[" + Schema + "].")}[{Name}]";

        public string NameWithSchemaRaw => $"{(Schema == null ? string.Empty : Schema + ".")}{Name}";

        internal string PrimaryColumnName
        {
            get => _primaryColumnName ?? throw new InvalidOperationException();
            set => _primaryColumnName = value;
        }

        internal Dictionary<string, SqlColumn> Columns { get; set; } = new Dictionary<string, SqlColumn>(StringComparer.OrdinalIgnoreCase);

        internal string InitialSnapshotQuery => (CustomSnapshotQuery ?? SelectIncrementalQuery) ?? $@"SELECT * FROM {NameWithSchema}";

        private string SelectIncrementalQueryWithFilter
            => SelectIncrementalQuery != null ? $"({SelectIncrementalQuery})" : $"{NameWithSchema}";

        /// <summary>
        /// Query to get incremental inserts and updates using CHANGETABLE.
        /// Uses RIGHT JOIN so that even if a row was inserted then deleted before we read,
        /// we still get the CT record (with NULL data columns).
        /// We filter those out for I/U operations (they'll appear as deletes).
        /// </summary>
        internal string IncrementalAddOrUpdatesQuery => $@"SELECT {string.Join(",", Columns.Keys.Except(SkipColumns).Select(_ => "T.[" + _ + "]"))}, CT.SYS_CHANGE_OPERATION AS __OP
FROM CHANGETABLE(CHANGES {NameWithSchema}, @version) AS CT
LEFT JOIN {SelectIncrementalQueryWithFilter} AS T ON T.[{PrimaryColumnName}] = CT.[{PrimaryColumnName}]
WHERE CT.SYS_CHANGE_OPERATION IN ('I', 'U')
AND T.[{PrimaryColumnName}] IS NOT NULL
AND (CT.SYS_CHANGE_CONTEXT IS NULL OR CT.SYS_CHANGE_CONTEXT <> @sourceId)";

        /// <summary>
        /// Query to get incremental deletes using CHANGETABLE.
        /// For deletes, the row no longer exists in the source table, so we read the PK from CT directly.
        /// </summary>
        internal string IncrementalDeletesQuery => $@"SELECT CT.[{PrimaryColumnName}]
FROM CHANGETABLE(CHANGES {NameWithSchema}, @version) AS CT
WHERE CT.SYS_CHANGE_OPERATION = 'D'
AND (CT.SYS_CHANGE_CONTEXT IS NULL OR CT.SYS_CHANGE_CONTEXT <> @sourceId)";

        internal string SelectExistingQuery => $@"SELECT COUNT(*) FROM {NameWithSchema}
WHERE [{PrimaryColumnName}] = @PrimaryColumnParameter";

        internal string[] SkipColumns { get; set; } = Array.Empty<string>();

        internal string[] SkipColumnsOnInsertOrUpdate { get; set; } = Array.Empty<string>();

        internal bool HasTableIdentityColumn { get; set; }

        internal string[] PrimaryKeyColumns
        {
            get => _primaryKeyColumns ?? throw new InvalidOperationException();
            set => _primaryKeyColumns = value;
        }

        internal void SetupCommand(SqlCommand cmd, ChangeType itemChangeType, Dictionary<string, SyncItemValue> syncItemValues)
        {
            var allColumnsExceptSkipColumns = Columns.Keys.Except(SkipColumns.Concat(SkipColumnsOnInsertOrUpdate)).ToArray();

            var allSyncItems = syncItemValues
                .Where(value => allColumnsExceptSkipColumns.Any(_ => StringComparer.OrdinalIgnoreCase.Compare(_, value.Key) == 0))
                .ToList();

            var allSyncItemsExceptPrimaryKey = allSyncItems.Where(_ => !PrimaryKeyColumns.Any(kc => kc == _.Key)).ToArray();

            // WITH CHANGE_TRACKING_CONTEXT(@sync_client_id) must immediately precede the DML statement
            const string ctContext = ";WITH CHANGE_TRACKING_CONTEXT(@sync_client_id) ";

            switch (itemChangeType)
            {
                case ChangeType.Insert:
                    {
                        var identityInsertCommand = string.Empty;
                        if (IdentityInsert == IdentityInsertMode.Auto)
                        {
                            if (HasTableIdentityColumn)
                            {
                                identityInsertCommand = $"SET IDENTITY_INSERT {NameWithSchema} ON\n";
                            }
                        }
                        else if (IdentityInsert == IdentityInsertMode.On)
                        {
                            identityInsertCommand = $"SET IDENTITY_INSERT {NameWithSchema} ON\n";
                        }
                        else if (IdentityInsert == IdentityInsertMode.Off)
                        {
                            identityInsertCommand = $"SET IDENTITY_INSERT {NameWithSchema} OFF\n";
                        }

                        cmd.CommandText = $@"{identityInsertCommand}BEGIN TRY
{ctContext}INSERT INTO {NameWithSchema} ({string.Join(", ", allSyncItems.Select(_ => "[" + _.Key + "]"))})
VALUES ({string.Join(", ", allSyncItems.Select((_, index) => $"@p{index}"))});
END TRY
BEGIN CATCH
PRINT ERROR_MESSAGE()
END CATCH
";

                        int pIndex = 0;
                        foreach (var valueItem in allSyncItems)
                        {
                            cmd.Parameters.Add(
                                Columns[valueItem.Key].CreateParameter($"@p{pIndex}", valueItem.Value));
                            pIndex++;
                        }
                    }
                    break;

                case ChangeType.Update:
                    {
                        cmd.CommandText = $@"BEGIN TRY
{ctContext}UPDATE {NameWithSchema}
SET {string.Join(", ", allSyncItemsExceptPrimaryKey.Select((_, index) => $"[{_.Key}] = @p{index}"))}
WHERE {NameWithSchema}.[{PrimaryColumnName}] = @PrimaryColumnParameter
AND (@sync_force_write = 1 OR NOT EXISTS (
    SELECT 1 FROM CHANGETABLE(CHANGES {NameWithSchema}, @last_sync_version) AS CT
    WHERE CT.[{PrimaryColumnName}] = @PrimaryColumnParameter
    AND (CT.SYS_CHANGE_CONTEXT IS NULL OR CT.SYS_CHANGE_CONTEXT <> @sync_client_id)
))
END TRY
BEGIN CATCH
PRINT ERROR_MESSAGE()
END CATCH";

                        cmd.Parameters.Add(Columns[PrimaryColumnName].CreateParameter("@PrimaryColumnParameter", syncItemValues[PrimaryColumnName]));

                        int pIndex = 0;
                        foreach (var valueItem in allSyncItemsExceptPrimaryKey)
                        {
                            cmd.Parameters.Add(
                                Columns[valueItem.Key].CreateParameter($"@p{pIndex}", valueItem.Value));
                            pIndex++;
                        }
                    }
                    break;

                case ChangeType.Delete:
                    {
                        cmd.CommandText = $@"BEGIN TRY
{ctContext}DELETE FROM {NameWithSchema}
WHERE {NameWithSchema}.[{PrimaryColumnName}] = @PrimaryColumnParameter
AND (@sync_force_write = 1 OR NOT EXISTS (
    SELECT 1 FROM CHANGETABLE(CHANGES {NameWithSchema}, @last_sync_version) AS CT
    WHERE CT.[{PrimaryColumnName}] = @PrimaryColumnParameter
    AND (CT.SYS_CHANGE_CONTEXT IS NULL OR CT.SYS_CHANGE_CONTEXT <> @sync_client_id)
))
END TRY
BEGIN CATCH
PRINT ERROR_MESSAGE()
END CATCH";

                        cmd.Parameters.Add(Columns[PrimaryColumnName]
                            .CreateParameter("@PrimaryColumnParameter", syncItemValues[PrimaryColumnName]));
                    }
                    break;
            }
        }

        internal IdentityInsertMode IdentityInsert { get; set; }
    }

    public enum IdentityInsertMode
    {
        Auto,
        On,
        Off,
        Disabled
    }
}
