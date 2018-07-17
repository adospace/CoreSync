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
            PrimaryKey = primaryKey;
        }

        public string Name { get; }
        public string Type { get; }
        public bool PrimaryKey { get; }
    }
}
