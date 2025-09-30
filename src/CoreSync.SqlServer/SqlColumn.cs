using Microsoft.Data.SqlClient;
using System.Data;

namespace CoreSync.SqlServer
{
    internal class SqlColumn
    {
        public SqlColumn(string name, SqlDbType dbType, byte? precision, byte? scale)
        {
            Name = name;
            DbType = dbType;
            Precision = precision;
            Scale = scale;
        }

        public string Name { get; }
        public SqlDbType DbType { get; }
        public byte? Precision { get; }
        public byte? Scale { get; }

        public SqlParameter CreateParameter(string parameterName, SyncItemValue value)
        {
            var parameter = new SqlParameter(parameterName, DbType)
            {
                Value = Utils.ConvertToSqlType(value, DbType)
            };

            if (DbType == SqlDbType.Decimal)
            {
                if (Precision != null)
                {
                    parameter.Precision = Precision.Value;
                }
                if (Scale != null)
                {
                    parameter.Scale = Scale.Value;
                }
            }

            return parameter;
        }
    }
}