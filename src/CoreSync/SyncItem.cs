using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CoreSync
{
    /// <summary>
    /// Represents a single row-level change (insert, update, or delete) for a specific table.
    /// </summary>
    public class SyncItem
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SyncItem"/> class for deserialization.
        /// </summary>
        public SyncItem()
        {

        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncItem"/> class.
        /// </summary>
        /// <param name="tableName">The name of the table this change applies to.</param>
        /// <param name="changeType">The type of change (insert, update, or delete).</param>
        /// <param name="values">A dictionary of column names to their values for this row.</param>
        /// <exception cref="ArgumentException"><paramref name="tableName"/> is <c>null</c>, empty, or whitespace.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="values"/> is <c>null</c>.</exception>
        public SyncItem(string tableName, ChangeType changeType, Dictionary<string, object?> values)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(tableName, nameof(tableName));
            Validate.NotNull(values, nameof(values));

            TableName = tableName;
            ChangeType = changeType;
            Values = values.ToDictionary(_ => _.Key, _ => new SyncItemValue(_.Value));
        }

        /// <summary>
        /// Gets or sets the name of the table this change applies to.
        /// </summary>
        public string TableName { get; set; } = default!;

        /// <summary>
        /// Gets or sets the type of change represented by this item.
        /// </summary>
        public ChangeType ChangeType { get; set; }

        /// <summary>
        /// Gets or sets the column values for this row, keyed by column name.
        /// </summary>
        public Dictionary<string, SyncItemValue> Values { get; set; } = default!;

        /// <inheritdoc />
        public override string ToString() => $"{ChangeType} on {TableName}: {{{GetValuesAsJson()}}}";

        private string GetValuesAsJson() => string.Join(", ", Values.OrderBy(_ => _.Key).Select(_ => $"\"{_.Key}\": {GetValueAsJson(_.Value)}"));

        private static string GetValueAsJson(SyncItemValue syncItemValue)
        {
            if (syncItemValue.Type == SyncItemValueType.Null ||
                syncItemValue.Value == null)
                return "null";
            if (syncItemValue.Type == SyncItemValueType.String)
                return $"\"{syncItemValue.Value}\"";

            return syncItemValue.Value.ToString();
        }
    }
}
