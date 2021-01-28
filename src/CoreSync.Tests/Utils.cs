using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Query;


namespace CoreSync.Tests
{
    public static class Utils
    {
        public static string ToSql<TEntity>(this IQueryable<TEntity> query) where TEntity : class
        {
            using var enumerator = query.Provider.Execute<IEnumerable<TEntity>>(query.Expression).GetEnumerator();
            var relationalCommandCache = enumerator.Private("_relationalCommandCache");
            var selectExpression = relationalCommandCache.Private<SelectExpression>("_selectExpression");
            var factory = relationalCommandCache.Private<IQuerySqlGeneratorFactory>("_querySqlGeneratorFactory");

            var sqlGenerator = factory.Create();
            var command = sqlGenerator.GetCommand(selectExpression);

            string sql = command.CommandText;
            return sql;
        }

        public static string ToSql<TEntity>(this IQueryable<TEntity> query, params string[] parameters) where TEntity : class
        {
            if (parameters is null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            var sql = ToSql(query);
            foreach (var parameter in parameters)
            {
                sql = sql.Replace($"'{parameter}'", parameter);
            }
            return sql;
        }

        private static object Private(this object obj, string privateField) => obj?.GetType().GetField(privateField, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(obj);
        private static T Private<T>(this object obj, string privateField) => (T)obj?.GetType().GetField(privateField, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(obj);
    }
}


