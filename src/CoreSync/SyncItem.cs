using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    public abstract class SyncItem
    {
        public SyncItem()
        { 
        
        }

        public SyncItem(string tableName, ChangeType changeType, Dictionary<string, object> values)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(tableName, nameof(tableName));
            Validate.NotNull(values, nameof(values));

            TableName = tableName;
            ChangeType = changeType;
            Values = values;
        }

        public string TableName { get; set; }
        public ChangeType ChangeType { get; set; }
        public Dictionary<string, object> Values { get; set; }

        public override string ToString()
        {
            return $"{ChangeType} on {TableName}";
        }
    }
}
