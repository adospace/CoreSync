using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync.Sqlite
{
    internal class SqliteColumn
    {
        public SqliteColumn(string name, string type, bool primaryKey = false)
        {
            Name = name;
            Type = type;
            IsPrimaryKey = primaryKey;
        }

        public string Name { get; }
        public string Type { get; }
        public bool IsPrimaryKey { get; }
    }
}
