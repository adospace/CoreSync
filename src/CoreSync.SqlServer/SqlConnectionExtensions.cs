using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Text;
using System.Threading.Tasks;

namespace CoreSync.SqlServer
{
    internal static class SqlConnectionExtensions
    {
        public static async Task<bool> GetIsChangeTrakingEnabledAsync(this SqlConnection connection)
        {
            //            var cmdText = $@"SELECT COUNT(*) 
            //FROM sys.change_tracking_databases 
            //WHERE database_id=DB_ID('{databaseName}')";
            var cmdText = $@"SELECT COUNT(*) 
            FROM sys.change_tracking_databases 
            WHERE database_id=DB_ID()";
            using (var cmd = new SqlCommand(cmdText, connection))
            {
                return ((int) await cmd.ExecuteScalarAsync()) == 1;
            }
        }


        public static Task EnableChangeTrakingAsync(this SqlConnection connection, int days = 2, bool autoCleanup = true)
        {
            var cmdText = $@"ALTER DATABASE CURRENT
SET CHANGE_TRACKING = ON  
(CHANGE_RETENTION = {days} DAYS, AUTO_CLEANUP = {(autoCleanup ? "ON" : "OFF")})";
            using (var cmd = new SqlCommand(cmdText, connection))
            {
                return cmd.ExecuteNonQueryAsync();
            }
        }

        public static Task DisableChangeTrakingAsync(this SqlConnection connection)
        {
            var cmdText = $@"ALTER DATABASE CURRENT SET CHANGE_TRACKING = OFF";
            using (var cmd = new SqlCommand(cmdText, connection))
            {
                return cmd.ExecuteNonQueryAsync();
            }
        }

        public static async Task<bool> GetIsChangeTrakingEnabledAsync(this SqlConnection connection, SqlSyncTable table)
        {
            var cmdText = $@"SELECT COUNT(*)
FROM sys.change_tracking_tables
WHERE object_id = OBJECT_ID('{table.NameWithSchema}')";
            using (var cmd = new SqlCommand(cmdText, connection))
            {
                return (int)await cmd.ExecuteScalarAsync() == 1;
            }
        }

        public static Task EnableChangeTrakingAsync(this SqlConnection connection, SqlSyncTable table)
        {
            var cmdText = $@"ALTER TABLE {table.NameWithSchema} ENABLE CHANGE_TRACKING";
            using (var cmd = new SqlCommand(cmdText, connection))
            {
                return cmd.ExecuteNonQueryAsync();
            }
        }

        public static async Task<bool> SetSnapshotIsolationAsync(this SqlConnection connection)
        {
            var cmdText = $@"SELECT snapshot_isolation_state_desc from sys.databases where database_id=DB_ID()";
            using (var cmd = new SqlCommand(cmdText, connection))
            {
                return (string)(await cmd.ExecuteScalarAsync()) == "ON";
            }
        }

        public static Task SetSnapshotIsolationAsync(this SqlConnection connection, bool enabled)
        {
            var cmdText = $@"ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION {(enabled ? "ON" : "OFF")}";
            using (var cmd = new SqlCommand(cmdText, connection))
            {
                return cmd.ExecuteNonQueryAsync();
            }
        }

        public static async Task<string[]> GetTableNamesAsync(this SqlConnection connection)
        {
            var cmdText = $@"SELECT name FROM sys.Tables";
            using (var cmd = new SqlCommand(cmdText, connection))
            {
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    var listOfTableNames = new List<string>();
                    while (await reader.ReadAsync())
                    {
                        listOfTableNames.Add(reader.GetString(0));
                    }

                    return listOfTableNames.ToArray();
                }
            }
        }

        public static async Task<string[]> GetClusteredPrimaryKeyIndexesAsync(this SqlConnection connection, SqlSyncTable syncTable)
        {
            var cmdText = $@"SELECT
    name AS Index_Name
FROM
    sys.indexes
WHERE
    is_hypothetical = 0 AND
    index_id != 0 AND
    object_id = OBJECT_ID('{syncTable.NameWithSchema}') AND
	type_desc = 'CLUSTERED' AND
	is_primary_key = 1;  ";
            using (var cmd = new SqlCommand(cmdText, connection))
            {
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    var listOfIndexNames = new List<string>();
                    while (await reader.ReadAsync())
                    {
                        listOfIndexNames.Add(reader.GetString(0));
                    }

                    return listOfIndexNames.ToArray();
                }
            }
        }

        public static async Task<string[]> GetIndexColumnNamesAsync(this SqlConnection connection, SqlSyncTable syncTable, string indexName)
        {
            var cmdText = $@"SELECT
    COL_NAME(b.object_id,b.column_id) AS Column_Name 
FROM
    sys.indexes AS a  
INNER JOIN
    sys.index_columns AS b   
       ON a.object_id = b.object_id AND a.index_id = b.index_id  
WHERE
        a.is_hypothetical = 0 AND
    a.object_id = OBJECT_ID('{syncTable.NameWithSchema}') AND
	a.name = '{indexName}'";
            using (var cmd = new SqlCommand(cmdText, connection))
            {
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    var listOfColumnNames = new List<string>();
                    while (await reader.ReadAsync())
                    {
                        listOfColumnNames.Add(reader.GetString(0));
                    }

                    return listOfColumnNames.ToArray();
                }
            }
        }

        public static async Task<(string, SqlDbType)[]> GetTableColumnsAsync(this SqlConnection connection, SqlSyncTable syncTable)
        {
            var cmdText = $@"SELECT COLUMN_NAME, DATA_TYPE 
FROM INFORMATION_SCHEMA.COLUMNS
WHERE 
     TABLE_NAME = '{syncTable.Name}' AND TABLE_SCHEMA = '{syncTable.Schema}'";
            using (var cmd = new SqlCommand(cmdText, connection))
            {
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    var listOfColumnNames = new List<(string, SqlDbType)>();
                    while (await reader.ReadAsync())
                    {
                        listOfColumnNames.Add((reader.GetString(0), TryGetSqlDbTypeFromString(reader.GetString(1))));
                    }

                    return listOfColumnNames.ToArray();
                }
            }
        }

        private static SqlDbType TryGetSqlDbTypeFromString(string typeName)
        {
            //ref https://docs.microsoft.com/en-us/dotnet/framework/data/adonet/sql-server-data-type-mappings
            if (typeName == "sql_variant")
                return SqlDbType.Variant;
            if (typeName == "smalldatetime")
                return SqlDbType.DateTime;
            if (typeName == "rowversion")
                return SqlDbType.Timestamp;
            if (typeName == "numeric")
                return SqlDbType.Decimal;
            if (typeName == "image")
                return SqlDbType.Binary;
            if (typeName == "binary")
                return SqlDbType.VarBinary;


            return (SqlDbType)Enum.Parse(typeof(SqlDbType), typeName, true);
        }


    }
}
