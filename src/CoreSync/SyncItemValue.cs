using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    public class SyncItemValue
    {
        public SyncItemValue()
        { }

        public SyncItemValue(object value)
        {
            Value = value;
            DetectTypeOfObject(value);
        }

        private void DetectTypeOfObject(object value)
        {
            if (value == null)
                Type = SyncItemValueType.Null;
            else if (value is string)
                Type = SyncItemValueType.String;
            else if (value is bool)
                Type = SyncItemValueType.Boolean;
            else if (value is byte[])
                Type = SyncItemValueType.ByteArray;
            else if (value is DateTime)
                Type = SyncItemValueType.DateTime;
            else if (value is double)
                Type = SyncItemValueType.Double;
            else if (value is int)
                Type = SyncItemValueType.Int32;
            else if (value is float)
                Type = SyncItemValueType.Float;
            else if (value is Guid)
                Type = SyncItemValueType.Guid;
            else if (value is long)
                Type = SyncItemValueType.Int64;
            else
                throw new NotSupportedException();
        }

        public object Value { get; set; }

        public SyncItemValueType Type { get; set; }
    }
}
