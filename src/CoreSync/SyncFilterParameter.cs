using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    /// <summary>
    /// Represents a named parameter used to filter the set of changes returned during synchronization.
    /// </summary>
    public class SyncFilterParameter
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SyncFilterParameter"/> class.
        /// </summary>
        /// <param name="name">The parameter name (used in custom incremental or snapshot queries).</param>
        /// <param name="value">The parameter value.</param>
        /// <exception cref="ArgumentException"><paramref name="name"/> is <c>null</c>, empty, or whitespace.</exception>
        public SyncFilterParameter(string name, object value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException($"'{nameof(name)}' cannot be null or whitespace", nameof(name));
            }

            Name = name;
            Value = value;
        }

        /// <summary>
        /// Gets the parameter name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the parameter value.
        /// </summary>
        public object Value { get; }
    }
}
