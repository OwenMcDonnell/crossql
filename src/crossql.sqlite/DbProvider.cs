using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using crossql.Config;
using crossql.Extensions;
using crossql.Helpers;
using Microsoft.Data.Sqlite;

namespace crossql.sqlite
{
    public class DbProvider : SqliteDbProviderBase
    {
        private readonly IDbConnectionProvider _connectionProvider;
        private readonly bool _enforceForeignKeys = true;
        private readonly string _sqliteDatabasePath;

        public DbProvider(string databaseName, Action<DbConfiguration> config) : base(config)
        {
            DatabaseName = databaseName;
            _sqliteDatabasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), databaseName);
            _connectionProvider = new DbConnectionProvider(_sqliteDatabasePath);
        }

        public DbProvider(string databaseName, SqliteSettings settings, Action<DbConfiguration> config) : base(config)
        {
            DatabaseName = databaseName;
            _enforceForeignKeys = settings.EnforceForeignKeys;
            _sqliteDatabasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), databaseName);
            _connectionProvider = new DbConnectionProvider(_sqliteDatabasePath, settings);
        }

        public DbProvider(string databaseName)
        {
            DatabaseName = databaseName;
            _sqliteDatabasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), databaseName);
            _connectionProvider = new DbConnectionProvider(_sqliteDatabasePath);
        }

        public DbProvider(string databaseName, SqliteSettings settings)
        {
            DatabaseName = databaseName;
            _enforceForeignKeys = settings.EnforceForeignKeys;
            _sqliteDatabasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), databaseName);
            _connectionProvider = new DbConnectionProvider(_sqliteDatabasePath, settings);
        }

        public override Task<bool> CheckIfDatabaseExists()
        {
            var exists = File.Exists(_sqliteDatabasePath);
            return Task.FromResult(exists);
        }

        public override Task CreateDatabase()
        {
            var file = File.Create(_sqliteDatabasePath);
            file.Close();
            return Task.CompletedTask;
        }

        public override Task DropDatabase()
        {
            File.Delete(_sqliteDatabasePath);
            return Task.CompletedTask;
        }

        public override async Task<bool> CheckIfTableExists(string tableName)
        {
            var count = await ExecuteScalar<int>(string.Format(Dialect.CheckTableExists, tableName)).ConfigureAwait(false);
            return count > 0;
        }

        public override async Task<bool> CheckIfTableColumnExists(string tableName, string columnName)
        {
            var columnSql = await ExecuteScalar<string>(string.Format(Dialect.CheckTableColumnExists, tableName)).ConfigureAwait(false);
            return columnSql.Contains($"[{columnName}]");
        }

        private void EnableForeignKeys(IDbCommand command)
        {
            if (_enforceForeignKeys)
            {
                command.CommandText = "PRAGMA foreign_keys=ON";
                command.ExecuteNonQuery();
            }
        }

        #region ExecuteReader

        public override async Task<TResult> ExecuteReader<TResult>(string commandText, IDictionary<string, object> parameters, Func<IDataReader, TResult> readerMapper)
        {
            using (var connection = await _connectionProvider.GetOpenConnectionAsync().ConfigureAwait(false))
            using (var command = (SqliteCommand)connection.CreateCommand())
            {
                command.CommandType = CommandType.Text;
                EnableForeignKeys(command);
                command.CommandText = commandText;
                parameters.ForEach(
                    parameter =>
                        command.Parameters.Add(new SqliteParameter(parameter.Key,
                            parameter.Value ?? DBNull.Value)));

                using (var reader = await command.ExecuteReaderAsync())
                {
                    return readerMapper(reader);
                }
            }
        }

        #endregion

        #region ExecuteNonQuery

        public override async Task ExecuteNonQuery(string commandText, IDictionary<string, object> parameters)
        {
            using (var connection = await _connectionProvider.GetOpenConnectionAsync().ConfigureAwait(false))
            using (var command = (SqliteCommand)connection.CreateCommand())
            {
                command.CommandType = CommandType.Text;
                EnableForeignKeys(command);
                command.CommandText = commandText;
                parameters.ForEach(
                    parameter =>
                        command.Parameters.Add(new SqliteParameter(parameter.Key,
                            parameter.Value ?? DBNull.Value)));

                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        #endregion

        #region ExecuteScalar

        public override async Task<TKey> ExecuteScalar<TKey>(string commandText, IDictionary<string, object> parameters)
        {
            using (var connection = await _connectionProvider.GetOpenConnectionAsync().ConfigureAwait(false))
            using (var command = (SqliteCommand)connection.CreateCommand())
            {
                command.CommandType = CommandType.Text;
                EnableForeignKeys(command);
                command.CommandText = commandText;
                parameters.ForEach(
                    parameter =>
                        command.Parameters.Add(new SqliteParameter(parameter.Key,
                            parameter.Value ?? DBNull.Value)));

                var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
                if (typeof(TKey) == typeof(Guid))
                {
                    return (TKey)(object)new Guid((byte[])result);
                }

                if (typeof(TKey) == typeof(int))
                {
                    if (int.TryParse(result?.ToString(), out var intResult)) return (TKey) (object) intResult;
                    return (TKey)(object)0;
                }

                if (typeof(TKey) == typeof(DateTime))
                {
                    if (DateTime.TryParse(result.ToString(), out var dateTimeResult)) return (TKey) (object) dateTimeResult;
                    return (TKey)(object)DateTimeHelper.MinSqlValue;
                }

                return (TKey)result;
            }
        }

        public override async Task RunInTransaction(Func<IDataModifier,Task> dbChange)
        {

            //var transaction = new SQLiteTransactionBuilder(Dialect);
            //dbChange(transaction);
            //using (var connection = await _connectionProvider.GetOpenConnectionAsync().ConfigureAwait(false))
            //using (var trans = connection.BeginTransaction())
            //using (var command = connection.CreateCommand())
            //{
            //    command.Transaction = trans;
            //    try
            //    {
            //        command.CommandType = CommandType.Text;
            //        foreach (var cmd in transaction.Commands)
            //        {
            //            command.CommandText = cmd.CommandText.ToString();
            //            cmd.CommandParameters.ForEach(parameter =>
            //            {
            //                command.Parameters.Add(new SqliteParameter(parameter.Key, parameter.Value ?? DBNull.Value));
            //            });

            //            command.ExecuteNonQuery();
            //            command.Parameters.Clear();
            //        }
            //        trans.Commit();
            //    }
            //    catch
            //    {
            //        trans?.Rollback();
            //        throw;
            //    }
            //}
        }

        #endregion
    }
}