using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    public abstract class SyncTable
    {
        protected SyncTable(string name)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(name, nameof(name));

            Name = name;
        }

        /// <summary>
        /// Name of the table/collection
        /// </summary>
        public string Name { get; }
    }
}
