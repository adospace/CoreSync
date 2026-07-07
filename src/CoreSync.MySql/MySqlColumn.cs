using System;

namespace CoreSync.MySql
{
    internal class MySqlColumn
    {
        public MySqlColumn(string name, string dataType, string columnType, bool primaryKey = false)
        {
            Name = name;
            DataType = dataType;
            ColumnType = columnType;
            IsPrimaryKey = primaryKey;
        }

        public string Name { get; }
        public string DataType { get; }
        public string ColumnType { get; }
        public bool IsPrimaryKey { get; }

        public bool IsGuidLike =>
            (DataType.Equals("char", StringComparison.OrdinalIgnoreCase) ||
             DataType.Equals("varchar", StringComparison.OrdinalIgnoreCase)) &&
            (ColumnType.Equals("char(36)", StringComparison.OrdinalIgnoreCase) ||
             ColumnType.Equals("varchar(36)", StringComparison.OrdinalIgnoreCase));
    }
}
