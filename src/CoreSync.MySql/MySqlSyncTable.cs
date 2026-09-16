using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CoreSync.MySql
{
    public class MySqlSyncTable : SyncTable
    {
        internal MySqlSyncTable(string name, Type? recordType = null, SyncDirection syncDirection = SyncDirection.UploadAndDownload, bool skipInitialSnapshot = false, string? selectIncrementalQuery = null, string? customSnapshotQuery = null)
            : base(name, syncDirection, skipInitialSnapshot, selectIncrementalQuery, customSnapshotQuery)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(name, nameof(name));

            RecordType = recordType;
        }

        internal string[] SkipColumns { get; set; } = Array.Empty<string>();

        internal string[] SkipColumnsOnInsertOrUpdate { get; set; } = Array.Empty<string>();

        public Type? RecordType { get; }

        internal string PrimaryColumnName => Columns.First(_ => _.Value.IsPrimaryKey).Value.Name;

        internal MySqlPrimaryColumnType PrimaryColumnType => GetPrimaryColumnType(Columns[PrimaryColumnName].DataType);

        internal Dictionary<string, MySqlColumn> Columns { get; set; } = [];

        internal string InitialSnapshotQuery => (CustomSnapshotQuery ?? SelectIncrementalQuery) ?? $"SELECT * FROM `{Name}`";

        private string SelectQueryWithFilter => SelectIncrementalQuery != null ? $"({SelectIncrementalQuery})" : $"`{Name}`";

        internal string IncrementalAddOrUpdatesQuery =>
            $@"SELECT DISTINCT {string.Join(",", Columns.Keys.Except(SkipColumns).Select(_ => "T.`" + _ + "`"))}, CT.op AS __op
FROM {SelectQueryWithFilter} AS T
INNER JOIN `__core_sync_ct` AS CT ON T.`{PrimaryColumnName}` = CT.`pk_{PrimaryColumnType.ToString().ToLowerInvariant()}`
WHERE CT.`id` > @version AND CT.`tbl` = @tableName AND (CT.`src` IS NULL OR CT.`src` != @sourceId)";

        internal string IncrementalDeletesQuery =>
            $@"SELECT `pk_{PrimaryColumnType.ToString().ToLowerInvariant()}` AS `{PrimaryColumnName}`
FROM `__core_sync_ct`
WHERE `tbl` = @tableName AND `id` > @version AND `op` = 'D' AND (`src` IS NULL OR `src` != @sourceId)";

        internal string SelectExistingQuery =>
            $@"SELECT COUNT(*) FROM `{Name}`
WHERE `{PrimaryColumnName}` = @PrimaryColumnParameter";

        internal object ConvertPrimaryKeyValue(object? value) => ConvertValueForColumn(PrimaryColumnName, value);

        private static string[] IntegerTypes =>
            ["tinyint", "smallint", "mediumint", "int", "integer", "bigint"];

        private MySqlPrimaryColumnType GetPrimaryColumnType(string dataType)
        {
            if (IntegerTypes.Contains(dataType, StringComparer.OrdinalIgnoreCase))
            {
                return MySqlPrimaryColumnType.Integer;
            }

            return dataType.ToLowerInvariant() switch
            {
                "char" or "varchar" or "tinytext" or "text" or "mediumtext" or "longtext" => MySqlPrimaryColumnType.Text,
                "binary" or "varbinary" or "tinyblob" or "blob" or "mediumblob" or "longblob" => MySqlPrimaryColumnType.Blob,
                _ => throw new NotSupportedException($"Table {Name} primary key type '{dataType}'"),
            };
        }

        private object ConvertValueForColumn(string columnName, object? value)
        {
            if (value == null)
            {
                return DBNull.Value;
            }

            if (Columns.TryGetValue(columnName, out var column))
            {
                if (column.IsGuidLike)
                {
                    if (value is Guid guidValue)
                    {
                        return guidValue.ToString("D").ToUpperInvariant();
                    }

                    if (value is string stringValue && Guid.TryParse(stringValue, out var parsedGuid))
                    {
                        return parsedGuid.ToString("D").ToUpperInvariant();
                    }
                }

                if ((column.DataType.Equals("datetime", StringComparison.OrdinalIgnoreCase) ||
                     column.DataType.Equals("timestamp", StringComparison.OrdinalIgnoreCase) ||
                     column.DataType.Equals("date", StringComparison.OrdinalIgnoreCase)) &&
                    value is string stringDate &&
                    DateTime.TryParse(stringDate, out var parsedDate))
                {
                    return parsedDate;
                }

                //a time column travels as an invariant "c" formatted string (see SyncItemValue): it has
                //no dedicated SyncItemValueType and that is also how SQLite stores it
                if (column.DataType.Equals("time", StringComparison.OrdinalIgnoreCase) &&
                    value is string stringTime &&
                    TimeSpan.TryParse(stringTime, CultureInfo.InvariantCulture, out var parsedTime))
                {
                    return parsedTime;
                }
            }

            return value;
        }

        internal void SetupCommand(MySqlCommand cmd, ChangeType itemChangeType, Dictionary<string, SyncItemValue> syncItemValues)
        {
            var allColumnsExceptSkipColumns = Columns.Keys.Except(SkipColumns.Concat(SkipColumnsOnInsertOrUpdate)).ToArray();
            var valuesForValidColumns = syncItemValues
                .Where(value => allColumnsExceptSkipColumns.Any(_ => StringComparer.OrdinalIgnoreCase.Compare(_, value.Key) == 0))
                .ToList();

            switch (itemChangeType)
            {
                case ChangeType.Insert:
                    cmd.CommandText = $@"INSERT IGNORE INTO `{Name}` ({string.Join(", ", valuesForValidColumns.Select(_ => "`" + _.Key + "`"))})
VALUES ({string.Join(", ", valuesForValidColumns.Select((_, index) => $"@p{index}"))});";

                    foreach (var valueItem in valuesForValidColumns.Select((value, index) => (value, index)))
                    {
                        cmd.Parameters.Add(new MySqlParameter($"@p{valueItem.index}", ConvertValueForColumn(valueItem.value.Key, valueItem.value.Value.Value)));
                    }
                    break;

                case ChangeType.Update:
                    cmd.CommandText = $@"UPDATE `{Name}`
SET {string.Join(", ", valuesForValidColumns.Select((_, index) => $"`{_.Key}` = @p{index}"))}
WHERE `{Name}`.`{PrimaryColumnName}` = @PrimaryColumnParameter";

                    foreach (var valueItem in valuesForValidColumns.Select((value, index) => (value, index)))
                    {
                        cmd.Parameters.Add(new MySqlParameter($"@p{valueItem.index}", ConvertValueForColumn(valueItem.value.Key, valueItem.value.Value.Value)));
                    }

                    cmd.Parameters.Add(new MySqlParameter("@PrimaryColumnParameter", ConvertValueForColumn(PrimaryColumnName, syncItemValues[PrimaryColumnName].Value)));
                    break;

                case ChangeType.Delete:
                    cmd.CommandText = $@"DELETE FROM `{Name}`
WHERE `{Name}`.`{PrimaryColumnName}` = @PrimaryColumnParameter";

                    cmd.Parameters.Add(new MySqlParameter("@PrimaryColumnParameter", ConvertValueForColumn(PrimaryColumnName, syncItemValues[PrimaryColumnName].Value)));
                    break;
            }
        }
    }
}
