namespace CoreSync.Sqlite
{
    public class SqliteSyncTable : SyncTable
    {
        internal SqliteSyncTable(string name, bool bidirectional = true, string schema = "dbo") : base(name)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(name, nameof(name));
            Validate.NotNullOrEmptyOrWhiteSpace(schema, nameof(schema));

            Bidirectional = bidirectional;
            Schema = schema;
        }

        /// <summary>
        /// Bidirectional vs upload-only table synchronization (not supported yet)
        /// </summary>
        public bool Bidirectional { get; }

        /// <summary>
        /// Schema of table (Default: dbo)
        /// </summary>
        public string Schema { get; }
    }
}