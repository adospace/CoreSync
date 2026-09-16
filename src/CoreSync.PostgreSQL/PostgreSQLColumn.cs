using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync.PostgreSQL
{
    internal class PostgreSQLColumn
    {
        public PostgreSQLColumn(string name, string type, bool primaryKey = false)
        {
            Name = name;
            Type = type;
            IsPrimaryKey = primaryKey;
        }

        public string Name { get; }
        public string Type { get; }
        public bool IsPrimaryKey { get; }

        /// <summary>
        /// True for the PostgreSQL types Npgsql can materialize as a <see cref="TimeSpan"/>.
        /// </summary>
        /// <remarks>
        /// <c>time with time zone</c> (<c>timetz</c>) is deliberately excluded: Npgsql materializes it
        /// as a <see cref="DateTimeOffset"/>, which has no <see cref="SyncItemValueType"/> and is not
        /// supported for synchronization.
        /// <para>
        /// An <c>interval</c> is included, but only one made of days/hours/minutes/seconds round-trips:
        /// an interval carrying months or years has no fixed length and Npgsql refuses to convert it to
        /// a <see cref="TimeSpan"/>.
        /// </para>
        /// </remarks>
        public bool IsTimeLike =>
            Type.Equals("time without time zone", StringComparison.OrdinalIgnoreCase) ||
            Type.Equals("time", StringComparison.OrdinalIgnoreCase) ||
            Type.Equals("interval", StringComparison.OrdinalIgnoreCase);
    }
} 