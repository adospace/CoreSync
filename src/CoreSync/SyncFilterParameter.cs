using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    public class SyncFilterParameter
    {
        public SyncFilterParameter(string name, object value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException($"'{nameof(name)}' cannot be null or whitespace", nameof(name));
            }

            Name = name;
            Value = value;
        }

        public string Name { get; }
        public object Value { get; }
    }
}
