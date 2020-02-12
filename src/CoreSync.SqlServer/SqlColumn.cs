using System.Data;

namespace CoreSync.SqlServer
{
    internal class SqlColumn
    {
        public SqlColumn(string name, SqlDbType dbType)
        {
            Name = name;
            DbType = dbType;
        }

        public string Name { get; }
        public SqlDbType DbType { get; }
    }
}