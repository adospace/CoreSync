using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace CoreSync.SqlServer
{
    public class SqlSyncConfigurationBuilder
    {
        private readonly string _connectionString;

        //do not use dictionary because order is important
        private readonly List<SqlSyncTable> _tables = new List<SqlSyncTable>();

        private string _schema = "dbo";

        public SqlSyncConfigurationBuilder(string connectionString)
        {
            _connectionString = connectionString;
        }

        public SqlSyncConfigurationBuilder Schema(string schema)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(schema, nameof(schema));

            _schema = schema;
            return this;
        }

        public SqlSyncConfigurationBuilder Table([NotNull] string name, SyncDirection syncDirection = SyncDirection.UploadAndDownload, string schema = null, bool skipInitialSnapshot = false)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(name, nameof(name));

            name = name.Trim();

            var nameWithSchema = $"{(schema == null ? string.Empty : "[" + schema + "].")}[{name}]";

            if (_tables.Any(_ => string.CompareOrdinal(_.NameWithSchema, nameWithSchema) == 0))
                throw new InvalidOperationException($"Table with name '{nameWithSchema}' already added");

            _tables.Add(new SqlSyncTable(name, syncDirection, schema ?? _schema, skipInitialSnapshot));
            return this;
        }

        public SqlSyncConfigurationBuilder Table<T>(SyncDirection syncDirection = SyncDirection.UploadAndDownload, string schema = null, bool skipInitialSnapshot = false)
        {
            var name = typeof(T).Name;
            var tableAttribute = (TableAttribute)Attribute.GetCustomAttribute(typeof(T), typeof(TableAttribute));
            if (tableAttribute != null)
            {
                name = tableAttribute.Name;
                schema = tableAttribute.Schema;
            }

            var nameWithSchema = $"{(schema == null ? string.Empty : "[" + schema + "].")}[{name}]";

            if (_tables.Any(_ => string.CompareOrdinal(_.NameWithSchema, nameWithSchema) == 0))
                throw new InvalidOperationException($"Table with name '{nameWithSchema}' already added");

            return Table(name, syncDirection, schema ?? _schema, skipInitialSnapshot);
        }

        public SqlSyncConfiguration Build() => new SqlSyncConfiguration(_connectionString, _tables.ToArray());
    }
}