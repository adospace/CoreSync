using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    /// <summary>
    /// Specifies the type of change tracked for a synchronized row.
    /// </summary>
    public enum ChangeType
    {
        /// <summary>
        /// A new row was inserted.
        /// </summary>
        Insert = 0,

        /// <summary>
        /// An existing row was updated.
        /// </summary>
        Update = 1,

        /// <summary>
        /// An existing row was deleted.
        /// </summary>
        Delete = 2
    }
}
