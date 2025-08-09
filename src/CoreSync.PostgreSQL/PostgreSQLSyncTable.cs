using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CoreSync.PostgreSQL
{
    public class PostgreSQLSyncTable : SyncTable
    {
        internal PostgreSQLSyncTable(string name, Type? recordType = null, SyncDirection syncDirection = SyncDirection.UploadAndDownload, bool skipInitialSnapshot = false, string? selectIncrementalQuery = null, string? customSnapshotQuery = null)
            : base(name, syncDirection, skipInitialSnapshot, selectIncrementalQuery, customSnapshotQuery)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(name, nameof(name));

            RecordType = recordType;
        }

        /// <summary>
        /// Record type can be useful to cast back to correct types record values 
        /// when are read from PostgreSQL database
        /// </summary>
        public Type? RecordType { get; }

        internal string PrimaryColumnName => Columns.First(_ => _.Value.IsPrimaryKey).Value.Name;

        internal PostgreSQLPrimaryColumnType PrimaryColumnType => GetPrimaryColumnType(Columns[PrimaryColumnName].Type);

        private PostgreSQLPrimaryColumnType GetPrimaryColumnType(string type)
        {
            return type.ToUpperInvariant() switch
            {
                "INTEGER" or "INT" or "BIGINT" or "SMALLINT" or "SERIAL" or "BIGSERIAL" => PostgreSQLPrimaryColumnType.Integer,
                "TEXT" or "VARCHAR" or "CHARACTER VARYING" or "CHAR" or "CHARACTER" or "UUID" => PostgreSQLPrimaryColumnType.Text,
                "BYTEA" => PostgreSQLPrimaryColumnType.Bytea,
                _ => throw new NotSupportedException($"Table {Name} primary key type '{type}'"),
            };
        }

        /// <summary>
        /// Table columns (discovered)
        /// </summary>
        internal Dictionary<string, PostgreSQLColumn> Columns { get; set; } = [];

        internal string InitialSnapshotQuery => (CustomSnapshotQuery ?? SelectIncrementalQuery) ?? $@"SELECT * FROM ""{Name}""";

        private string SelectQueryWithFilter
            => SelectIncrementalQuery != null ? $"({SelectIncrementalQuery})" : $"\"{Name}\"";

        internal string IncrementalAddOrUpdatesQuery => $@"SELECT DISTINCT {string.Join(",", Columns.Select(_ => "T.\"" + _.Key + "\""))}, CT.op AS __OP 
                                FROM {SelectQueryWithFilter} AS T INNER JOIN __core_sync_ct AS CT ON T.""{PrimaryColumnName}""::text = CT.pk_{PrimaryColumnType.ToString().ToLowerInvariant()} WHERE CT.id > $1 AND CT.tbl = $2 AND (CT.src IS NULL OR CT.src != $3)";

        internal string IncrementalDeletesQuery => 
            $@"SELECT pk_{PrimaryColumnType.ToString().ToLowerInvariant()} AS ""{PrimaryColumnName}"" FROM __core_sync_ct WHERE tbl = $1 AND id > $2 AND op = 'D' AND (src IS NULL OR src != $3)";

        internal string SelectExistingQuery => $@"SELECT COUNT(*) FROM ""{Name}"" 
            WHERE ""{PrimaryColumnName}"" = $1";

        private object ConvertValueForColumn(string columnName, object? value)
        {
            if (value == null)
                return DBNull.Value;

            // Check if this column is a UUID type in PostgreSQL
            if (Columns.TryGetValue(columnName, out var column) && 
                column.Type.Equals("uuid", StringComparison.OrdinalIgnoreCase))
            {
                // Convert string to Guid if needed
                if (value is string stringValue && Guid.TryParse(stringValue, out var guidValue))
                {
                    return guidValue;
                }
            }

            return value;
        }

        internal void SetupCommand(NpgsqlCommand cmd, ChangeType itemChangeType, Dictionary<string, SyncItemValue> syncItemValues)
        {
            //take values only for existing columns (server table schema could be not in sync with local table schema)
            var valuesForValidColumns = syncItemValues
                .Where(value => Columns.Any(_ => StringComparer.OrdinalIgnoreCase.Compare(_.Key, value.Key) == 0))
                .ToList();

            switch (itemChangeType)
            {
                case ChangeType.Insert:
                    {
                        cmd.CommandText = $@"INSERT INTO ""{Name}"" ({string.Join(", ", valuesForValidColumns.Select(_ => "\"" + _.Key + "\""))}) 
VALUES ({string.Join(", ", valuesForValidColumns.Select((_, index) => $"${index + 1}"))}) ON CONFLICT DO NOTHING;";

                        int pIndex = 1;
                        foreach (var valueItem in valuesForValidColumns)
                        {
                            cmd.Parameters.Add(new NpgsqlParameter { Value = ConvertValueForColumn(valueItem.Key, valueItem.Value.Value) });
                            pIndex++;
                        }
                    }
                    break;

                case ChangeType.Update:
                    {
                        cmd.CommandText = $@"UPDATE ""{Name}""
SET {string.Join(", ", valuesForValidColumns.Select((_, index) => $"\"{_.Key}\" = ${index + 1}"))}
WHERE ""{Name}"".""{PrimaryColumnName}"" = ${valuesForValidColumns.Count + 1}
AND (${valuesForValidColumns.Count + 2} = true OR (SELECT MAX(id) FROM __core_sync_ct WHERE pk_{PrimaryColumnType.ToString().ToLowerInvariant()} = ${valuesForValidColumns.Count + 1}::text AND tbl = '{Name}') <= ${valuesForValidColumns.Count + 3})";

                        int pIndex = 1;
                        foreach (var valueItem in valuesForValidColumns)
                        {
                            cmd.Parameters.Add(new NpgsqlParameter { Value = ConvertValueForColumn(valueItem.Key, valueItem.Value.Value) });
                            pIndex++;
                        }
                        
                        cmd.Parameters.Add(new NpgsqlParameter { Value = ConvertValueForColumn(PrimaryColumnName, syncItemValues[PrimaryColumnName].Value) });
                    }
                    break;

                case ChangeType.Delete:
                    {
                        cmd.CommandText = $@"DELETE FROM ""{Name}""
WHERE ""{Name}"".""{PrimaryColumnName}"" = $1
AND ($2 = true OR (SELECT MAX(id) FROM __core_sync_ct WHERE pk_{PrimaryColumnType.ToString().ToLowerInvariant()} = $1::text AND tbl = '{Name}') <= $3)";

                        cmd.Parameters.Add(new NpgsqlParameter { Value = ConvertValueForColumn(PrimaryColumnName, syncItemValues[PrimaryColumnName].Value) });
                    }
                    break;
            }
        }
    }
} 