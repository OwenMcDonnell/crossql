﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using FutureState.AppCore.Data.Attributes;
using FutureState.AppCore.Data.Helpers;
using FutureState.AppCore.Data.Models;

namespace FutureState.AppCore.Data
{
    public static class DbProviderExtensions
    {
        /// <summary>
        ///     Create a new record based on a Model
        /// </summary>
        /// <typeparam name="TModel">Model Type</typeparam>
        /// <param name="dbProvider">Database Provider</param>
        /// <param name="model">Model Object</param>
        /// <returns>The uniqueidentifier (Guid) of the newly created record.</returns>
        public static void Create<TModel>(this IDbProvider dbProvider, TModel model) where TModel : class, new()
        {
            var mapper = new AutoMapper<TModel>();

            string tableName = GetTableName(typeof (TModel));
            List<string> fieldNameList = mapper.GetFieldNameList(model).Select(field => field).ToList();
            string parameters = "@" + string.Join(",@", fieldNameList);
            string fields = string.Join(",", fieldNameList);
            string commandText = string.Format(dbProvider.Dialect.InsertInto, tableName, fields, parameters);
            IDictionary<string, object> commandParams = mapper.BuildDbParametersFrom(model);

            dbProvider.ExecuteNonQuery(commandText, commandParams);

            // Update the Join Table
            UpdateJoins(dbProvider, model, tableName, mapper);
        }

        /// <summary>
        ///     Query the database based on a ManyToMany relationship.
        ///     `TModel` will be the Model that you're extracting.
        ///     `TJoin` will be the joined model.
        ///     Your query will be based the Id field of the `TJoin` model.
        /// </summary>
        /// <typeparam name="TModel">The model object you're returning</typeparam>
        /// <typeparam name="TJoinTo">The joined model</typeparam>
        /// <param name="dbProvider">The Database Provider</param>
        /// <param name="id">The record Id of the joined model</param>
        /// <returns>An Enumerable list of TModel</returns>
        public static IEnumerable<TModel> Query<TModel, TJoinTo>(this IDbProvider dbProvider, Guid id)
            where TModel : class, new()
        {
            var mapper = new AutoMapper<TModel>();
            string tableName = GetTableName(typeof (TModel)); // Users
            string tableNameSingular = tableName.Remove(tableName.Length - 1); // User
            string referenceTableName = GetTableName(typeof (TJoinTo)); // Roles
            string referenceTableNameSingular = referenceTableName.Remove(referenceTableName.Length - 1); // Role
            string joinTableName = GetJoinTableName(tableName, referenceTableName); // Roles_Users
            string joinText = string.Format(dbProvider.Dialect.InnerJoin, joinTableName, tableNameSingular, tableName, referenceTableName,
                                            referenceTableNameSingular);

            string whereExpression = string.Format("{0}.{1}Id = @{1}Id", joinTableName, referenceTableNameSingular);
            string whereClause = string.Format(dbProvider.Dialect.Where, whereExpression);
            string commandText = string.Format(dbProvider.Dialect.SelectFrom, tableName, joinText + " " + whereClause);

            string key = string.Format("@{0}Id", referenceTableNameSingular);
            var parameters = new Dictionary<string, object> {{key, id}};

            return dbProvider.ExecuteReader(commandText, parameters, mapper.BuildStackFrom);
        }

        /// <summary>
        ///     Query the Database for ALL records.
        /// </summary>
        /// <typeparam name="TModel">Model Type</typeparam>
        /// <param name="dbProvider">Database Provider</param>
        /// <returns>IEnumerable model</returns>
        public static IDbQuery<TModel> Query<TModel>(this IDbProvider dbProvider) where TModel : class, new()
        {
            var query = new DbQuery<TModel>(dbProvider);
            return query;
        }

//        /// <summary>
//        ///     Query the Database based on an expression tree
//        /// </summary>
//        /// <typeparam name="TModel">Model type</typeparam>
//        /// <param name="dbProvider">Database Provider</param>
//        /// <param name="expression">The expression to query</param>
//        /// <returns>IEnumerable model</returns>
//        public static IEnumerable<TModel> Query<TModel>(this IDbProvider dbProvider, Expression<Func<TModel, object>> expression) where TModel : class, new()
//        {
//            var visitor = new WhereExpressionVisitor().Visit(expression);
//            var mapper = new AutoMapper<TModel>();
//
//            var tableName = GetTableName(typeof(TModel));
//            var whereClause = string.Format(dbProvider.Dialect.Where, visitor.WhereExpression);
//            var commandText = string.Format(dbProvider.Dialect.SelectFrom, tableName, whereClause);
//
//            return dbProvider.ExecuteReader(commandText, visitor.Parameters, mapper.BuildStackFrom);
//        }
//
//        /// <summary>
//        ///     Query the Database for ALL records.
//        /// </summary>
//        /// <typeparam name="TModel">Model Type</typeparam>
//        /// <param name="dbProvider">Database Provider</param>
//        /// <returns>IEnumerable model</returns>
//        public static IEnumerable<TModel> Query<TModel>(this IDbProvider dbProvider) where TModel : class, new()
//        {
//            var mapper = new AutoMapper<TModel>();
//
//            var tableName = GetTableName(typeof(TModel));
//            var commandText = string.Format(dbProvider.Dialect.SelectFrom, tableName, "");
//
//            return dbProvider.ExecuteReader(commandText, mapper.BuildStackFrom);
//        }

        /// <summary>
        ///     Update the Database Record of a specified model.
        /// </summary>
        /// <typeparam name="TModel">Model type</typeparam>
        /// <param name="dbProvider">Database Provider</param>
        /// <param name="model">Model Object to update</param>
        /// <remarks>We're using the Id field to check the update.</remarks>
        public static void Update<TModel>(this IDbProvider dbProvider, TModel model) where TModel : class, new()
        {
            var mapper = new AutoMapper<TModel>();
            List<string> dbFields = mapper.GetFieldNameList(model).Select(field => string.Format("[{0}] = @{0}", field)).ToList();

            string tableName = GetTableName(typeof (TModel));
            string whereClause = string.Format(dbProvider.Dialect.Where, "Id = @Id");
            string commandText = string.Format(dbProvider.Dialect.Update, tableName, string.Join(",", dbFields),
                                               whereClause);

            dbProvider.ExecuteNonQuery(commandText, mapper.BuildDbParametersFrom(model));

            // Update the Join Table
            UpdateJoins(dbProvider, model, tableName, mapper);
        }

        /// <summary>
        ///     Delete the Database Record based on an expression
        /// </summary>
        /// <typeparam name="TModel">Model Type</typeparam>
        /// <param name="dbProvider">Database Provider</param>
        /// <param name="expression">The expression to use for the query</param>
        /// <remarks>THIS IS A HARD DELETE. When you run this method, the record is GONE!</remarks>
        public static void Delete<TModel>(this IDbProvider dbProvider, Expression<Func<TModel, object>> expression)
            where TModel : class, new()
        {
            WhereExpressionVisitor visitor = new WhereExpressionVisitor().Visit(expression);

            // this is a hard delete. soft deletes will happen in the repository layer.
            string tableName = GetTableName(typeof (TModel));
            string whereClause = string.Format(dbProvider.Dialect.Where, visitor.WhereExpression);
            string commandText = string.Format(dbProvider.Dialect.DeleteFrom, tableName, whereClause);

            dbProvider.ExecuteNonQuery(commandText, visitor.Parameters);
        }

        /// <summary>
        ///     Updates all Join Tables based on the <see cref="ManyToManyAttribute" />
        /// </summary>
        /// <typeparam name="TModel">Object model Type</typeparam>
        /// <param name="dbProvider">DB Provider</param>
        /// <param name="model">Actual object model</param>
        /// <param name="tableName">The name of the table</param>
        /// <param name="mapper">
        ///     An instance of IAutoMapper
        /// </param>
        private static void UpdateJoins<TModel>(IDbProvider dbProvider, TModel model, string tableName,
                                                IAutoMapper<TModel> mapper) where TModel : class, new()
        {
            KeyValuePair<string, object> leftModel = mapper.BuildDbParametersFrom(model).FirstOrDefault(k => k.Key == "Id");
            string leftKey = typeof (TModel).Name.Replace("Model", string.Empty) + "Id";
            var parameters = new Dictionary<string, object> {{"@" + leftKey, leftModel.Value}};
            // loop through all the model properties, and grab ONLY the ones that are decorated with "[ManyToMany]"
            foreach (
                PropertyInfo collection in model.GetType()
                                                .GetProperties()
                                                .Where(
                                                    property =>
                                                    property.GetCustomAttributes(typeof (ManyToManyAttribute), true).Any()))
            {
                if (!IsGenericList(collection.PropertyType))
                    throw new ArgumentException("The property must be an ICollection<>");
                string joinTableName = GetJoinTableName(tableName, collection.Name);
                string deleteWhereClause = string.Format(dbProvider.Dialect.Where, string.Format("{0} = @{0}", leftKey));
                string deleteCommandText = string.Format(dbProvider.Dialect.DeleteFrom, joinTableName, deleteWhereClause);
                // Delete ALL records in the Join table associated with the `leftModel`
                dbProvider.ExecuteNonQuery(deleteCommandText, parameters);
                Type manyToManyCollection = collection.PropertyType.GetGenericArguments().FirstOrDefault();
                var listValues = (IEnumerable) collection.GetValue(model, null);
                if (listValues == null) continue;
                foreach (object value in listValues)
                {
                    if (manyToManyCollection == null)
                        throw new ArgumentException();
                    PropertyInfo[] rightProperties = manyToManyCollection.GetProperties();
                    string manyToManyCollectionName = manyToManyCollection.Name.Replace("Model", string.Empty);
                    foreach (PropertyInfo rightProperty in rightProperties)
                    {
                        string rightPropertyName = rightProperty.Name;
                        if (rightPropertyName != "Id")
                            continue; // short circuit the loop if we're not dealing with the primary key.
                        string rightKey = manyToManyCollectionName + rightPropertyName;
                        object rightValue = rightProperty.GetValue(value, null);
                        parameters.Add("@" + rightKey, rightValue);
                        string fieldsToInsert = string.Format(dbProvider.Dialect.JoinFields, leftKey, rightKey); // "[{0}], [{1}]"
                        string parametersToSet = string.Format(dbProvider.Dialect.JoinParameters, leftKey, rightKey); // "@{0}, @{1}"
                        string insertCommandText = string.Format(dbProvider.Dialect.InsertInto, joinTableName,
                                                                 fieldsToInsert,
                                                                 parametersToSet);
                        dbProvider.ExecuteNonQuery(insertCommandText, parameters);
                        // Remove the parameter for the next iteration.
                        parameters.Remove("@" + rightKey);
                    }
                }
            }
        }

        private static string GetTableName(MemberInfo type)
        {
            // TODO: Inflection Library here
            return type.Name.Replace("Model", "s");
        }

        private static string GetJoinTableName(string tableName, string joinTableName)
        {
            var names = new[] {tableName, joinTableName};
            Array.Sort(names, StringComparer.CurrentCulture);
            return string.Join("_", names);
        }

        private static bool IsGenericList(Type type)
        {
            if (type == null)
                throw new ArgumentNullException("type");
            return type.GetInterfaces()
                       .Where(i => i.IsGenericType)
                       .Any(i => i.GetGenericTypeDefinition() == typeof (ICollection<>));
        }
    }
}