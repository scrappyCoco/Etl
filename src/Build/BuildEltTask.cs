using System.Diagnostics.Contracts;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antlr4.StringTemplate;
using Coding4Fun.Etl.Build.Services.Dacpac;
using Coding4Fun.Etl.Build.Services.IO;
using Coding4Fun.Etl.Common;
using Coding4Fun.TransactSql.Analyzers.Visitors;
using Microsoft.SqlServer.Dac.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using ColumnDefinition = Coding4Fun.Etl.Common.ColumnDefinition;
using TableDefinition = Coding4Fun.Etl.Common.TableDefinition;

namespace Coding4Fun.Etl.Build;

/// <summary>
/// MSBuild task for generating ETL configuration pipelines from SQL objects in DACPAC files.
/// </summary>
/// <remarks>
/// This task analyzes stored procedures in target DACPAC, extracts metadata using regex markers,
/// and generates ETL pipeline configurations with batch processing logic.
/// </remarks>
public class BuildEltTask : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// Regex pattern to identify main table reference markers in SQL code.
    /// Format: C4F.ETL.MainTable: [value]
    /// </summary>
    private readonly Regex MainTableMarkerRegex = new("C4F[.]ETL[.]MainTable:\\s*(?<value>\\S+)", RegexOptions.Compiled);

    /// <summary>
    /// Regex pattern to identify batch size markers in SQL code.
    /// Format: C4F.ETL.BatchSize: [value]
    /// </summary>
    private readonly Regex BatchSizeMarkerRegex = new("C4F[.]ETL[.]BatchSize:\\s*(?<value>\\S+)", RegexOptions.Compiled);

    /// <summary>
    /// Regex pattern to identify batch column markers in SQL code.
    /// </summary>
    private readonly Regex BatchColumnMarkerRegex = new("C4F[.]ETL[.]BatchColumns:\\s*(?<value>^[^\n]+)", RegexOptions.Compiled);

    /// <summary>
    /// Path to the source DACPAC file containing database schema definitions.
    /// </summary>
    /// <value>Must be a valid DACPAC file path</value>
    public string? C4FEtlGeneratorSourceDacPacPath { get; set; }

    /// <summary>
    /// Path to the target DACPAC file containing stored procedures to analyze.
    /// </summary>
    /// <value>Must be a valid DACPAC file path</value>
    public string? C4FEtlGeneratorTargetDacPacPath { get; set; }

    /// <summary>
    /// Output directory path for generated ETL configuration JSON files.
    /// </summary>
    /// <value>Directory will be created if it doesn't exist</value>
    public string? C4FEtlGeneratorOutputEtlConfig { get; set; }

    /// <summary>
    /// SQL model loader implementation for DACPAC file processing.
    /// </summary>
    internal ISqlModelLoader ModelLoader { get; set; } = new DacPacModelLoader();

    /// <summary>
    /// File system operations provider for directory and file manipulation.
    /// </summary>
    internal IFileSystemProvider FileSystemProvider { get; set; } = new DefaultFileSystemProvider();

    /// <summary>
    /// Executes the MSBuild task workflow.
    /// </summary>
    /// <returns>True if execution completed successfully, false otherwise</returns>
    public override bool Execute()
    {
        if (string.IsNullOrWhiteSpace(C4FEtlGeneratorSourceDacPacPath))
        {
            Log.LogError($"The parameter '{nameof(C4FEtlGeneratorSourceDacPacPath)}' is required.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(C4FEtlGeneratorTargetDacPacPath))
        {
            Log.LogError($"The parameter '{nameof(C4FEtlGeneratorTargetDacPacPath)}' is required.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(C4FEtlGeneratorOutputEtlConfig))
        {
            Log.LogError($"The parameter '{nameof(C4FEtlGeneratorOutputEtlConfig)}' is required.");
            return false;
        }

        FileSystemProvider.CreateDirectory(C4FEtlGeneratorOutputEtlConfig);

        Log.LogMessage($"{nameof(C4FEtlGeneratorSourceDacPacPath)}: \"{C4FEtlGeneratorSourceDacPacPath}\"");
        Log.LogMessage($"{nameof(C4FEtlGeneratorTargetDacPacPath)}: \"{C4FEtlGeneratorTargetDacPacPath}\"");
        Log.LogMessage($"{nameof(C4FEtlGeneratorOutputEtlConfig)}: \"{C4FEtlGeneratorOutputEtlConfig}\"");

        PipelineConfiguration[] pipelineConfigurations = Generate(C4FEtlGeneratorSourceDacPacPath, C4FEtlGeneratorTargetDacPacPath);
        JsonSerializerOptions options = new()
        {
            WriteIndented = true
        };

        foreach (PipelineConfiguration pipelineConfiguration in pipelineConfigurations)
        {
            string outputConfigFileName = pipelineConfiguration.ProcedureName + ".json";
            string outputJsonPath = Path.Combine(C4FEtlGeneratorOutputEtlConfig, outputConfigFileName);
            string json = JsonSerializer.Serialize(pipelineConfiguration, options);
            FileSystemProvider.WriteAllText(outputJsonPath, json);
        }

        return true;
    }

    /// <summary>
    /// Generates ETL pipeline configurations from target DACPAC stored procedures.
    /// </summary>
    /// <returns>Array of generated pipeline configurations</returns>
    private PipelineConfiguration[] Generate(
        string sourceDacPacPath,
        string targetDacPacPath
    )
    {
        List<PipelineConfiguration> pipelineConfigurations = [];
        Sql160ScriptGenerator generator = new();
        TSqlModel sourceModel = ModelLoader.Load(sourceDacPacPath);
        TSqlModel targetModel = ModelLoader.Load(targetDacPacPath);
        IEnumerable<TSqlObject> storedProcedures = targetModel.GetObjects(DacQueryScopes.UserDefined, ModelSchema.Procedure);
        foreach (TSqlObject procedure in storedProcedures)
        {
            string procedureSql = procedure.GetScript();

            using StringReader sqlStringReader = new(procedureSql);

            TSqlFragment? procedureAst = TSqlParser
                .CreateParser(SqlVersion.Sql160, false)
                .Parse(sqlStringReader, out IList<ParseError>? errors);

            // Searching for main table marker in SQL.
            string? mainTable = MainTableMarkerRegex.Match(procedureSql)?.Groups["value"].Value;
            if (string.IsNullOrWhiteSpace(mainTable))
            {
                Log.LogError($"Procedure \"{procedure.Name}\" doesn't have \"{nameof(MainTableMarkerRegex)}\".");
                continue;
            }

            // BatchSizeMarkerRegex
            string? batchSizeString = BatchSizeMarkerRegex.Match(procedureSql)?.Groups["value"].Value;
            if (string.IsNullOrWhiteSpace(mainTable))
            {
                Log.LogError($"Procedure \"{procedure.Name}\" doesn't have \"{nameof(BatchSizeMarkerRegex)}\".");
                continue;
            }
            int batchSize = int.Parse(batchSizeString!);

            string? batchColumns = BatchColumnMarkerRegex.Match(procedureSql)?.Groups["value"].Value;
            if (string.IsNullOrWhiteSpace(batchColumns))
            {
                Log.LogError("Procedure \"{procedure.Name}\" doesn't have \"{nameof(BatchColumnMarkerRegex)}\".");
                continue;
            }

            // Collection of batch column names for each table.
            Dictionary<string, string> batchColumnMap = [];
            string[] batchColumnDefinitions = batchColumns.Split(',');
            foreach (string batchColumnDefinition in batchColumnDefinitions)
            {
                string[] parts = batchColumnDefinition.Split('.');
                string tableName = parts[0].Trim();
                string columnName = parts[1].Trim();
                batchColumnMap.Add(tableName, columnName);

                TSqlObject? table = sourceModel.GetObject(
                    ModelSchema.Table,
                    new ObjectIdentifier(tableName),
                    DacQueryScopes.SameDatabase);

                if (table == null)
                {
                    Log.LogError($"Table \"{tableName}\" not found in source DACPAC.");
                    continue;
                }

                TSqlObject? batchColumn = table
                    .GetChildren(DacQueryScopes.SameDatabase)
                    .FirstOrDefault(c => c.ObjectType == ModelSchema.Column &&
                                         c.Name.Parts.Last() == columnName);

                if (batchColumn == null)
                {
                    Log.LogError($"Column \"{columnName}\" not found in table \"{tableName}\".");
                    continue;
                }
                batchColumnMap.Add(tableName, columnName);
            }

            TreePartSearcherVisitor planModifierVisitor = new();
            procedureAst.Accept(planModifierVisitor);

            List<TableDefinition> tableDefinitions = [];
            foreach (NamedTableReference tableReference in planModifierVisitor.TableReferences.Distinct(new TableReferenceComparer()))
            {
                string tempTableName = "#" + tableReference.SchemaObject.BaseIdentifier.Value;

                ObjectIdentifier objectIdentifier = new([
                    tableReference.SchemaObject.SchemaIdentifier.Value,
                    tableReference.SchemaObject.BaseIdentifier.Value]);

                List<ColumnDefinition> columnDefinitions = [];

                TSqlObject tableFromSource = sourceModel.GetObject(ModelSchema.Table, objectIdentifier, DacQueryScopes.UserDefined);
                TSqlObject[] columns = [.. tableFromSource.GetChildren().Where(c => c.ObjectType == ModelSchema.Column)];

                foreach (TSqlObject column in columns)
                {
                    ColumnDefinition columnDefinition = GetColumnDefinition(column) ?? throw new InvalidOperationException($"Unable to format sql data type for column {column.Name}");
                    columnDefinitions.Add(columnDefinition);
                }

                string tableName = tableReference.SchemaObject.SchemaIdentifier.Value + "."
                                   + tableReference.SchemaObject.BaseIdentifier.Value;

                bool isMainTable = mainTable == tableName;
                if (!batchColumnMap.TryGetValue(tableName, out string? batchColumn))
                {
                    Log.LogError($"Batch column not found for table \"{tableName}\". Example of configuration: \"-- C4F.ETL.BatchColumns: TableName.ColumnName\"");
                    continue;
                }

                string selectStatement = isMainTable
                    ? GenerateSelectStatementForMainTable(tableName, [.. columnDefinitions], batchSize, batchColumn)
                    : GenerateSelectStatementForSecondaryTable(tableName, [.. columnDefinitions], batchColumn);

                string tempTableDdl = GenerateCreateTableDefinition(tempTableName, [.. columnDefinitions]);
                string selectBatchStatement = GenerateSelectMinMaxStatement(tempTableName, columnDefinitions[0].Name);

                TableDefinition tableDefinition = new()
                {
                    StageTableName = tableName,
                    TempTableName = tempTableName,
                    IsMain = isMainTable,
                    SelectStatement = selectStatement,
                    CreateTempTableStatement = tempTableDdl,
                    SelectBatchStatement = selectBatchStatement
                };
                tableDefinitions.Add(tableDefinition);
            }

            foreach (NamedTableReference tableReference in planModifierVisitor.TableReferences)
            {

                ObjectIdentifier objectIdentifier = new([
                    tableReference.SchemaObject.SchemaIdentifier.Value,
                    tableReference.SchemaObject.BaseIdentifier.Value]);

                tableReference.SchemaObject.Identifiers.Clear();
                tableReference.SchemaObject.Identifiers.Add(new Identifier { Value = "#" + objectIdentifier.Parts.Last() });
            }

            var searchElementVisitor = new SearchElementVisitor<BeginEndBlockStatement>();
            procedureAst.Accept(searchElementVisitor);
            var beginEndBlock = searchElementVisitor.FoundElements.FirstOrDefault();

            generator.GenerateScript(beginEndBlock, out string patchedProcedure);
            PipelineConfiguration pipelineConfiguration = new()
            {
                ProcedureName = procedure.Name.Parts.Last(),
                TableDefinitions = tableDefinitions.ToArray(),
                EtlSql = patchedProcedure
            };
            pipelineConfigurations.Add(pipelineConfiguration);
        }
        return pipelineConfigurations.ToArray();
    }

    /// <summary>
    /// Generates a SELECT statement for main tables with TOP and TIES clause.
    /// </summary>
    /// <param name="tableName">Fully qualified table name (schema.table)</param>
    /// <param name="columns">Array of column definitions</param>
    /// <param name="batchSize">Number of records to select per batch</param>
    /// <returns>Formatted SQL SELECT statement</returns>
    static string GenerateSelectStatementForMainTable(string tableName, ColumnDefinition[] columns, int batchSize, string batchColumnName)
    {
        Template template = new("""
            SELECT TOP (<batchSize>) WITH TIES <columns: {col | <col>}; separator=", ">
            FROM <tableName> WHERE @Min \< <batchColumnName>
            ORDER BY <batchColumnName> ASC;
            """);

        string[] columnNames = [.. columns.Select(col => $"[{col.Name}]")];

        template.Add("batchSize", batchSize);
        template.Add("columns", columnNames);
        template.Add("tableName", tableName);
        template.Add("batchColumnName", batchColumnName);

        return template.Render();
    }

    /// <summary>
    /// Generates a SELECT statement for secondary tables with range filtering.
    /// </summary>
    /// <param name="tableName">Fully qualified table name (schema.table)</param>
    /// <param name="columns">Array of column definitions</param>
    /// <returns>Formatted SQL SELECT statement</returns>
    static string GenerateSelectStatementForSecondaryTable(string tableName, ColumnDefinition[] columns, string batchColumnName)
    {
        Template template = new(
            "SELECT <columns: {col | <col.name>}; separator=\", \"> FROM <tableName> WHERE <batchColumnName> BETWEEN @Min AND @Max;");

        var columnNames = columns.Select(col => new { name = $"[{col.Name}]" }).ToList();

        template.Add("columns", columnNames);
        template.Add("tableName", tableName);
        template.Add("batchColumnName", batchColumnName);

        return template.Render();
    }

    /// <summary>
    /// Generates CREATE TABLE statement for temporary tables.
    /// </summary>
    /// <param name="tempTableName">Name of the temporary table</param>
    /// <param name="columns">Array of column definitions</param>
    /// <returns>Formatted SQL CREATE TABLE statement</returns>
    static string GenerateCreateTableDefinition(string tempTableName, ColumnDefinition[] columns)
    {
        Template template = new(
            "CREATE TABLE <tempTableName> (<columns: {col | <col.line>}; separator=\", \">);"
        );

        var columnLines = columns.Select(col =>
            $"    [{col.Name}] {col.DataType}" +
            (col.IsMax ? "(MAX)" :
                col.Length > 0 ? $"({col.Length}{(col.Precision > 0 ? $", {col.Length}" : "")})" : "") +
            (col.IsNullable ? " NULL" : " NOT NULL")
        ).ToList();

        template.Add("tempTableName", tempTableName);
        template.Add("columns", columnLines.Select(line => new { line }).ToList());

        return template.Render();
    }

    /// <summary>
    /// Generates SELECT statement for retrieving min/max values from temporary tables.
    /// </summary>
    /// <param name="tempTableName">Name of the temporary table</param>
    /// <param name="columnName">Name of the column to analyze</param>
    /// <returns>Formatted SQL SELECT statement</returns>
    static string GenerateSelectMinMaxStatement(string tempTableName, string columnName) =>
        $"SELECT MIN({columnName}) AS MinValue, MAX({columnName}) AS MaxValue FROM {tempTableName}";

    /// <summary>
    /// Formats SQL type of a column.
    /// </summary>
    /// <param name="column">The column to format.</param>
    /// <returns>The formatted SQL type.</returns>
    [Pure]
    private static ColumnDefinition? GetColumnDefinition(TSqlObject column)
    {
        ModelRelationshipInstance? columnDataType = column
            .GetReferencedRelationshipInstances(Column.DataType, DacQueryScopes.All)
            .FirstOrDefault();

        if (columnDataType == null) return null;

        int length = (int)column.GetProperty(Column.Length);
        int precision = (int)column.GetProperty(Column.Precision);
        bool isMax = (bool)column.GetProperty(Column.IsMax);
        string typeName = columnDataType.ObjectName.Parts.Last();
        bool nullable = (bool)column.GetProperty(Column.Nullable);

        return new ColumnDefinition
        {
            Name = column.Name.Parts.Last(),
            DataType = typeName,
            Length = length,
            IsMax = isMax,
            IsNullable = nullable,
            Precision = precision
        };
    }

    /// <summary>
    /// Searches for table references with three parts: DB.SCHEMA.TABLE.
    /// </summary>
    public class TreePartSearcherVisitor : TSqlFragmentVisitor
    {
        private readonly List<NamedTableReference> _tableReferences = new();
        /// <summary>
        /// Found table references with three parts: DB.SCHEMA.TABLE.
        /// </summary>
        public IReadOnlyList<NamedTableReference> TableReferences => [.. _tableReferences];

        /// <inheritdoc />
        public override void Visit(NamedTableReference tableReference)
        {
            if (tableReference.SchemaObject.Identifiers.Count != 3) return;
            _tableReferences.Add(tableReference);
        }
    }

    /// <summary>
    /// Compares two <see cref="NamedTableReference"/> objects for equality based on their schema object identifiers.
    /// </summary>
    public class TableReferenceComparer : IEqualityComparer<NamedTableReference>
    {
        /// <inheritdoc />
        public bool Equals(NamedTableReference? x, NamedTableReference? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (ReferenceEquals(x, null)) return false;
            if (ReferenceEquals(y, null)) return false;

            if (x.SchemaObject.Identifiers.Count != y.SchemaObject.Identifiers.Count) return false;
            foreach (var (xId, yId) in x.SchemaObject.Identifiers.Zip(y.SchemaObject.Identifiers))
            {
                if (xId.Value != yId.Value) return false;
            }

            return true;
        }

        /// <inheritdoc />
        public int GetHashCode(NamedTableReference obj)
        {
            return string.Join(".", obj.SchemaObject.Identifiers.Select(i => i.Value)).GetHashCode();
        }
    }
}