using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using MigSharp.Core;

namespace MigSharp.Providers
{
    [ProviderExport(Platform.Teradata, 12, InvariantName, MaximumDbObjectNameLength = 30, SupportsTransactions = false, ParameterExpression = "?", PrefixUnicodeLiterals = PrefixUnicodeLiterals)]
    [Supports(DbType.AnsiString, MaximumSize = 8000, CanBeUsedAsPrimaryKey = true)]
    [Supports(DbType.AnsiString, Warning = "Might require custom ADO.NET code as CLOB has unique restrictions.")]
    [Supports(DbType.Binary)]
    [Supports(DbType.Byte, CanBeUsedAsPrimaryKey = true, CanBeUsedAsIdentity = true, Warning = "Requires custom ADO.NET code to convert to/from an Int32 (using System.Convert).")]
    [Supports(DbType.Boolean, CanBeUsedAsPrimaryKey = true, Warning = "Requires custom ADO.NET code to convert to/from an Int32 (using System.Convert).")]
    [Supports(DbType.DateTime, CanBeUsedAsPrimaryKey = true)]
    [Supports(DbType.Decimal, MaximumSize = 22, MaximumScale = 18, CanBeUsedAsPrimaryKey = true)] // according to: http://forums.teradata.com/forum/training/number-data-type
    [Supports(DbType.Decimal, MaximumSize = 22, CanBeUsedAsPrimaryKey = true, CanBeUsedAsIdentity = true)] // according to: http://forums.teradata.com/forum/training/number-data-type
    [Supports(DbType.Double)]
    [Supports(DbType.Guid, CanBeUsedAsPrimaryKey = true, Warning = "Requires custom ADO.NET code to convert to/from a byte array (call Guid.ToByteArray(), Guid(byte[])) and the DbParameter.DbType must be set to DbType.Binary.")]
    [Supports(DbType.Int16, CanBeUsedAsPrimaryKey = true, CanBeUsedAsIdentity = true)]
    [Supports(DbType.Int32, CanBeUsedAsPrimaryKey = true, CanBeUsedAsIdentity = true)]
    [Supports(DbType.Int64, CanBeUsedAsPrimaryKey = true, CanBeUsedAsIdentity = true)]
    [Supports(DbType.String, MaximumSize = 8000, CanBeUsedAsPrimaryKey = true)]
    [Supports(DbType.String, Warning = "Might require custom ADO.NET code as CLOB has unique restrictions.")]
    internal class TeradataProvider : IProvider
    {
        public const string InvariantName = "Teradata.Client.Provider";
        protected const bool PrefixUnicodeLiterals = false;

        private const string Identation = "\t";
        private const string Identity = @"GENERATED ALWAYS AS IDENTITY
                (START WITH 1
                 INCREMENT BY 1
                 NO CYCLE)";

        public virtual string ExistsTable(string databaseName, TableName tableName)
        {
            return string.Format(CultureInfo.InvariantCulture, @"SELECT COUNT(*) FROM DBC.TABLES WHERE DATABASENAME='{0}' AND TABLENAME='{1}'", databaseName, tableName.Name);
        }

        public string ConvertToSql(object value, DbType targetDbType)
        {
            if (targetDbType == DbType.DateTime)
            {
                return string.Format(CultureInfo.InvariantCulture, "TIMESTAMP '{0}'",
                                     ((DateTime)value).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            }
            return SqlScriptingHelper.ToSql(value, targetDbType, PrefixUnicodeLiterals);
        }

        public IEnumerable<string> CreateTable(TableName tableName, IEnumerable<CreatedColumn> columns, string primaryKeyConstraintName)
        {
            if (columns.Any(c => c.IsRowVersion))
            {
                ThrowRowVersionNotSupportedException();
            }

            string commandText = string.Empty;
            var primaryKeyColumns = new List<string>();
            commandText += string.Format(@"{0}({1}", CreateTable(tableName.Name), Environment.NewLine);
            bool columnDelimiterIsNeeded = false;
            foreach (CreatedColumn column in columns)
            {
                if (columnDelimiterIsNeeded) commandText += string.Format(",{0}", Environment.NewLine);

                if (column.IsPrimaryKey)
                {
                    primaryKeyColumns.Add(column.Name);
                }

                commandText += GetColumnString(column, column.IsIdentity);

                columnDelimiterIsNeeded = true;
            }

            if (primaryKeyColumns.Count > 0)
            {
                // FEATURE: support clustering
                commandText += string.Format("){0} UNIQUE PRIMARY INDEX \"{1}\" {2}",
                                             Environment.NewLine,
                                             primaryKeyConstraintName,
                                             Environment.NewLine);
                commandText += string.Format("({0}", Environment.NewLine);

                columnDelimiterIsNeeded = false;
                foreach (string column in primaryKeyColumns)
                {
                    if (columnDelimiterIsNeeded) commandText += string.Format(", {0}", Environment.NewLine);

                    // FEATURE: make sort order configurable
                    commandText += string.Format("{0} {1} ", Identation, Escape(column));

                    columnDelimiterIsNeeded = true;
                }
            }
            foreach (var uniqueColumns in columns
                .Where(c => !string.IsNullOrEmpty(c.UniqueConstraint))
                .GroupBy(c => c.UniqueConstraint))
            {
                commandText += string.Format(") {0} UNIQUE INDEX {1} {2}",
                                             Environment.NewLine,
                                             Escape(uniqueColumns.Key),
                                             Environment.NewLine);
                commandText += string.Format("({0}", Environment.NewLine);

                columnDelimiterIsNeeded = false;
                foreach (string column in uniqueColumns.Select(c => c.Name))
                {
                    if (columnDelimiterIsNeeded) commandText += string.Format(",{0}", Environment.NewLine);
                    commandText += string.Format("{0}{1}", Identation, Escape(column));
                    columnDelimiterIsNeeded = true;
                }
                commandText += string.Format("{0}", Environment.NewLine);
            }

            commandText += string.Format(");{0}", Environment.NewLine);
            yield return commandText;
        }

        private static void ThrowRowVersionNotSupportedException()
        {
            throw new NotSupportedException("Teradata does not have a unique auto-increment row-version concept.");
        }

        public IEnumerable<string> DropTable(TableName tableName, bool checkIfExists)
        {
            if (checkIfExists)
            {
                // FEATURE: probably we could do a 
                // IF EXISTS(SELECT 1 FROM dbc.tables WHERE databasename = db_name AND tablename = table_name) THEN
                //   CALL DBC.SysExecSQL('DROP TABLE ' || db_name ||'.'|| table_name);
                // END IF;
                // as suggested on http://forums.teradata.com/forum/enterprise/drop-table-if-exists-0 but for this
                // we would need to have a databaseName parameter for this method as well. So, for the time being we:
                throw new NotSupportedException("The DropIfExists method is not supported by the current Teradata provider implementation.");
            }
            yield return string.Format("DROP TABLE {0}", Escape(tableName.Name));
        }

        public IEnumerable<string> AddColumn(TableName tableName, Column column)
        {
            if (column.IsRowVersion)
            {
                ThrowRowVersionNotSupportedException();
            }

            // assemble ALTER TABLE statements
            string commandText = string.Format(@"{0} ADD ", AlterTable(tableName.Name));
            commandText += GetColumnString(column, false);
            yield return commandText;
        }

        public IEnumerable<string> RenameTable(TableName oldName, string newName)
        {
            yield return string.Format("RENAME TABLE {0} TO {1};", Escape(oldName.Name), Escape(newName));
        }

        public IEnumerable<string> RenameColumn(TableName tableName, string oldName, string newName)
        {
            yield return string.Format("{0} RENAME {1} TO {2}", AlterTable(tableName.Name), Escape(oldName), Escape(newName));
        }

        public IEnumerable<string> DropColumn(TableName tableName, string columnName)
        {
            yield return string.Format("{0} DROP {1}", AlterTable(tableName.Name), Escape(columnName));
        }

        public IEnumerable<string> AlterColumn(TableName tableName, Column column)
        {
            yield return string.Format("{0} ADD {1}", AlterTable(tableName.Name), GetColumnString(column, false));
        }

        private static IEnumerable<string> DropConstraint(string tableName, string constraintName)
        {
            yield return string.Format("{0} DROP CONSTRAINT {1}", AlterTable(tableName), Escape(constraintName));
        }

        public IEnumerable<string> AddIndex(TableName tableName, IEnumerable<string> columnNames, string indexName)
        {
            yield return string.Format(CultureInfo.InvariantCulture, "CREATE INDEX {0} ({1}) ON {2}", Escape(indexName), GetCsList(columnNames), Escape(tableName.Name));
        }

        public IEnumerable<string> DropIndex(TableName tableName, string indexName)
        {
            yield return string.Format(CultureInfo.InvariantCulture, "DROP INDEX {0} ON {1}", Escape(indexName), Escape(tableName.Name));
        }

        public IEnumerable<string> AddForeignKey(TableName tableName, TableName referencedTableName, IEnumerable<ColumnReference> columnNames, string constraintName, bool cascadeOnDelete)
        {
            if (cascadeOnDelete)
            {
                throw new NotSupportedException("Teradata does not have an update action concept.");
            }

            string sourceCols = String.Empty;
            string targetCols = String.Empty;

            foreach (ColumnReference cr in columnNames)
            {
                sourceCols += Escape(cr.ColumnName) + ",";
                targetCols += Escape(cr.ReferencedColumnName) + ",";
            }
            sourceCols = sourceCols.TrimEnd(',');
            targetCols = targetCols.TrimEnd(',');

            yield return string.Format("{0} ADD CONSTRAINT {1} FOREIGN KEY ({2}) REFERENCES WITH CHECK OPTION {3}({4})", AlterTable(tableName.Name), Escape(constraintName), sourceCols, referencedTableName, targetCols);
        }

        public IEnumerable<string> DropForeignKey(TableName tableName, string constraintName)
        {
            return DropConstraint(tableName.Name, constraintName);
        }

        public IEnumerable<string> AddPrimaryKey(TableName tableName, IEnumerable<string> columnNames, string constraintName)
        {
            throw new NotSupportedException("Teradata always automatically generates a 'Primary Index' when creating a table which cannot be removed retrospectively. If you need a different primary key, you need to recreate the table with the right primary key and copy the contents from the old table.");
            //yield return string.Format("{0} ADD CONSTRAINT {1} PRIMARY KEY ({2})", AlterTable(tableName), Escape(constraintName), GetCsList(columnNames));
        }

        public IEnumerable<string> RenamePrimaryKey(TableName tableName, string oldName, string newName)
        {
            throw new NotSupportedException("Teradata always automatically generates a 'Primary Index' when creating a table which cannot be removed retrospectively. If you need a different primary key, you need to recreate the table with the right primary key and copy the contents from the old table.");
        }

        public IEnumerable<string> DropPrimaryKey(TableName tableName, string constraintName)
        {
            throw new NotSupportedException("Teradata always automatically generates a 'Primary Index' when creating a table which cannot be removed retrospectively. If you need a different primary key, you need to recreate the table with the right primary key and copy the contents from the old table.");
            //return DropConstraint(tableName, constraintName);
        }

        public IEnumerable<string> AddUniqueConstraint(TableName tableName, IEnumerable<string> columnNames, string constraintName)
        {
            string columns = columnNames.Aggregate(String.Empty, (current, column) => current + (Escape(column) + ","));
            columns = columns.TrimEnd(',');

            yield return string.Format("CREATE UNIQUE INDEX {0} ({1}) ON {2}", Escape(constraintName), columns, tableName);
        }

        public IEnumerable<string> DropUniqueConstraint(TableName tableName, string constraintName)
        {
            yield return string.Format("DROP UNIQUE INDEX {0} ON {1}", Escape(constraintName), tableName);
        }

        public IEnumerable<string> DropDefault(TableName tableName, Column column)
        {
            Debug.Assert(column.DefaultValue == null, "The DefaultValue must be null as we are going to call AlterColumn with it.");
            return AlterColumn(tableName, column);
        }

        public IEnumerable<string> CreateSchema(string schemaName)
        {
            throw new NotSupportedException("Schemata are not supported.");
        }

        public IEnumerable<string> DropSchema(string schemaName)
        {
            throw new NotSupportedException("Schemata are not supported.");
        }

        private static string CreateTable(string tableName)
        {
            return string.Format(CultureInfo.InvariantCulture, "CREATE TABLE {0}", Escape(tableName));
        }

        private static string AlterTable(string tableName)
        {
            return string.Format(CultureInfo.InvariantCulture, "ALTER TABLE {0}", Escape(tableName));
        }

        private static string Escape(string name)
        {
            return string.Format(CultureInfo.InvariantCulture, "\"{0}\"", name);
        }

        private static string GetTypeSpecifier(DataType type)
        {
            switch (type.DbType)
            {
                case DbType.Binary:
                    return "BLOB";
                case DbType.Byte:
                    return "DECIMAL(3)"; // note: BYTEINT is signed and therefore cannot be used
                case DbType.Boolean:
                    return "DECIMAL(1)";
                    //case DbType.Currency:
                    //    break;
                    //case DbType.Date:
                    //    break;
                case DbType.DateTime:
                    return "TIMESTAMP"; // note: DATE only denotes dates without a time component and therefore cannot be used
                case DbType.Decimal:
                    return "DECIMAL(" + type.Size + "," + (type.Scale ?? 0) + ")";
                case DbType.Double:
                    return "DOUBLE PRECISION";
                case DbType.Guid:
                    return "BYTE(16)";
                case DbType.Int16:
                    return "SMALLINT";
                case DbType.Int32:
                    return "INTEGER";
                case DbType.Int64:
                    return "BIGINT";
                    //case DbType.Object:
                    //    break;
                    //case DbType.SByte:
                    //    break;
                    //case DbType.Single:
                    //    break;
                case DbType.AnsiString:
                case DbType.String:
                    if (type.Size.HasValue)
                    {
                        return string.Format(CultureInfo.InvariantCulture, "VARCHAR ({0})", type.Size);
                    }
                    return "CLOB";
                    //case DbType.Time:
                    //    break;
                    //case DbType.UInt16:
                    //    break;
                    //case DbType.UInt32:
                    //    break;
                    //case DbType.UInt64:
                    //    break;
                    //case DbType.VarNumeric:
                    //    break;
                    //case DbType.AnsiStringFixedLength:
                    //    break;
                    //case DbType.StringFixedLength:
                    //    break;
                    //case DbType.Xml:
                    //    break;
                    //case DbType.DateTime2:
                    //    break;
                    //case DbType.DateTimeOffset:
                    //    break;
                default:
                    throw new ArgumentOutOfRangeException("type");
            }
        }

        private string GetColumnString(Column column, bool isIdentity)
        {
            string defaultConstraintClause = string.Empty;
            string commandText = string.Empty;

            if (!isIdentity && !GetTypeSpecifier(column.DataType).EndsWith("LOB", StringComparison.OrdinalIgnoreCase))
            {
                string defaultValue = column.DefaultValue == null ? "NULL" : GetDefaultValueAsString(column.DefaultValue, column.DataType);

                defaultConstraintClause = string.Format(CultureInfo.InvariantCulture, " DEFAULT {0}", defaultValue);
            }
            else if (column.DefaultValue != null)
            {
                throw new InvalidOperationException("Cannot add default values to identity columns or LOB columns."); // CLEAN: add validation for these cases
            }

            commandText += string.Format(CultureInfo.InvariantCulture, "{0} {1} {3} {4} {2}NULL",
                                         Escape(column.Name),
                                         GetTypeSpecifier(column.DataType),
                                         column.IsNullable ? string.Empty : "NOT ",
                                         defaultConstraintClause,
                                         isIdentity ? Identity : string.Empty);

            return commandText;
        }

        private string GetDefaultValueAsString(object value, DataType dataType)
        {
            if (value is SpecialDefaultValue)
            {
                switch ((SpecialDefaultValue)value)
                {
                    case SpecialDefaultValue.CurrentDateTime:
                    case SpecialDefaultValue.CurrentUtcDateTime: // note: for the UTC more work needs to be done (see: https://forums.teradata.com/forum/database/how-to-convert-current-timestamp-into-utc-timestamp)
                        return "current_timestamp(0)";
                    default:
                        throw new ArgumentOutOfRangeException("value");
                }
            }
            else
            {
                return ConvertToSql(value, dataType.DbType);
            }
        }

        private static string GetCsList(IEnumerable<string> columnNames)
        {
            string columns = String.Empty;
            foreach (var column in columnNames)
            {
                columns += Escape(column) + ",";
            }
            columns = columns.TrimEnd(',');

            return columns;
        }
    }
}