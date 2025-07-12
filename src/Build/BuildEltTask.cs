using System.Diagnostics.Contracts;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Coding4Fun.Etl.Common;
using Coding4Fun.TransactSql.Analyzers.Visitors;
using Microsoft.SqlServer.Dac.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using ColumnDefinition = Coding4Fun.Etl.Common.ColumnDefinition;
using TableDefinition = Coding4Fun.Etl.Common.TableDefinition;

public class BuildEltTask : Microsoft.Build.Utilities.Task
{
    private readonly Regex MainTableMarkerRegex = new("C4F[.]ETL[.]MainTable:\\s*(?<value>\\S+)", RegexOptions.Compiled);
    private readonly Regex BatchSizeMarkerRegex = new("C4F[.]ETL[.]BatchSize:\\s*(?<value>\\S+)", RegexOptions.Compiled);
    public string? C4FEtlGeneratorSourceDacPacPath { get; set; }
    public string? C4FEtlGeneratorTargetDacPacPath { get; set; }
    public string? C4FEtlGeneratorOutputEtlConfig { get; set; }
    public string C4FEtlGeneratorSourceConnectionString { get; set; } = @"Server=localhost;Database=StageDb;Integrated Security=True;TrustServerCertificate=True";
    public string C4FEtlGeneratorTargetConnectionString { get; set; } = @"Server=localhost;Database=CoreDb;Integrated Security=True;TrustServerCertificate=True";

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

        if (!Directory.Exists(C4FEtlGeneratorOutputEtlConfig))
        {
            Directory.CreateDirectory(C4FEtlGeneratorOutputEtlConfig);
        }

        Log.LogMessage($"{nameof(C4FEtlGeneratorSourceDacPacPath)}: \"{C4FEtlGeneratorSourceDacPacPath}\"");
        Log.LogMessage($"{nameof(C4FEtlGeneratorTargetDacPacPath)}: \"{C4FEtlGeneratorTargetDacPacPath}\"");
        Log.LogMessage($"{nameof(C4FEtlGeneratorOutputEtlConfig)}: \"{C4FEtlGeneratorOutputEtlConfig}\"");

        PipelineConfiguration[] pipelineConfigurations = Generate();
        JsonSerializerOptions options = new()
        {
            WriteIndented = true
        };

        foreach (PipelineConfiguration pipelineConfiguration in pipelineConfigurations)
        {
            string outputConfigFileName = pipelineConfiguration.ProcedureName + ".json";
            string outputJsonPath = Path.Combine(C4FEtlGeneratorOutputEtlConfig, outputConfigFileName);
            string json = JsonSerializer.Serialize(pipelineConfiguration, options);
            File.WriteAllText(outputJsonPath, json);
        }

        return true;
    }

    private PipelineConfiguration[] Generate()
    {
        List<PipelineConfiguration> pipelineConfigurations = [];
        Sql160ScriptGenerator generator = new();
        TSqlModel sourceModel = new(C4FEtlGeneratorSourceDacPacPath);
        TSqlModel targetModel = new(C4FEtlGeneratorTargetDacPacPath);
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
                TSqlObject[] columns = tableFromSource.GetChildren()
                    .Where(c => c.ObjectType == ModelSchema.Column)
                    .ToArray();

                foreach (TSqlObject column in columns)
                {
                    ColumnDefinition columnDefinition = GetColumnDefinition(column) ?? throw new InvalidOperationException($"Unable to format sql data type for column {column.Name}");
                    columnDefinitions.Add(columnDefinition);
                }

                string tableName = tableReference.SchemaObject.SchemaIdentifier.Value + "."
                                   + tableReference.SchemaObject.BaseIdentifier.Value;

                bool isMainTable = mainTable == tableName;
                string selectStatement = isMainTable
                    ? GenerateSelectStatementForMainTable(tableName, columnDefinitions.ToArray(), batchSize)
                    : GenerateSelectStatementForSecondaryTable(tableName, columnDefinitions.ToArray());

                string tempTableDdl = GenerateCreateTableDefinition(tempTableName, columnDefinitions.ToArray());
                string selectBatchStatement = GenerateSelectMinMaxStatement(tempTableName, columnDefinitions[0].Name);

                TableDefinition tableDefinition = new TableDefinition
                {
                    StageTableName = tableName,
                    TempTableName = tempTableName,
                    IsMain = isMainTable,
                    SelectStatement = selectStatement,
                    CreateTempTableStatement = tempTableDdl,
                    SelectBatchSTatement = selectBatchStatement,
                    Columns = [.. columnDefinitions]
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
                SourceConnectionString = C4FEtlGeneratorSourceConnectionString,
                TargetConnectionString = C4FEtlGeneratorTargetConnectionString,
                ProcedureName = procedure.Name.Parts.Last(),
                TableDefinitions = tableDefinitions.ToArray(),
                EtlSql = patchedProcedure
            };
            pipelineConfigurations.Add(pipelineConfiguration);
        }
        return pipelineConfigurations.ToArray();
    }

    static string GenerateSelectStatementForMainTable(string tableName, ColumnDefinition[] columns, int batchSize)
    {
        StringBuilder selectBuilder = new StringBuilder("SELECT TOP (").Append(batchSize).Append(") WITH TIES ").AppendLine();
        for (int i = 0; i < columns.Length; i++)
        {
            ColumnDefinition columnDefinition = columns[i];
            selectBuilder.Append($"[{columnDefinition.Name}]");
            if (i < columns.Length - 1) selectBuilder.Append(", ");
            else selectBuilder.AppendLine();
        }
        selectBuilder.Append("FROM ").Append(tableName).AppendLine();
        selectBuilder.Append("WHERE @Min < ").Append(columns[0].Name).AppendLine();
        selectBuilder.AppendFormat("ORDER BY {0} ASC", columns[0].Name);
        return selectBuilder.ToString();
    }

    static string GenerateSelectStatementForSecondaryTable(string tableName, ColumnDefinition[] columns)
    {
        StringBuilder selectBuilder = new StringBuilder("SELECT ").AppendLine();
        for (int i = 0; i < columns.Length; i++)
        {
            ColumnDefinition columnDefinition = columns[i];
            selectBuilder.Append($"[{columnDefinition.Name}]");
            if (i < columns.Length - 1) selectBuilder.Append(", ");
            else selectBuilder.AppendLine();
        }
        selectBuilder.Append("FROM ").Append(tableName).AppendLine();
        selectBuilder.Append("WHERE ").Append(columns[0].Name).Append(" BETWEEN @Min AND @Max;");
        return selectBuilder.ToString();
    }

    static string GenerateCreateTableDefinition(string tempTableName, ColumnDefinition[] columns)
    {
        StringBuilder stringBuilder = new();
        stringBuilder.AppendLine("CREATE TABLE " + tempTableName + " (");
        for (int i = 0; i < columns.Length; i++)
        {
            ColumnDefinition columnDefinition = columns[i];
            stringBuilder.Append($"    [{columnDefinition.Name}] {columnDefinition.DataType}");
            if (columnDefinition.IsMax) stringBuilder.Append($"(MAX)");
            else if (0 < columnDefinition.Length)
            {
                stringBuilder.Append('(');
                stringBuilder.Append(columnDefinition.Length);
                if (0 < columnDefinition.Precision) stringBuilder.Append(", ").Append(columnDefinition.Length);
                stringBuilder.Append(')');
            }
            if (columnDefinition.IsNullable) stringBuilder.Append(" NULL");
            else stringBuilder.Append(" NOT NULL");
            if (i < columns.Length - 1) stringBuilder.AppendLine(",");
            else stringBuilder.AppendLine();
        }
        stringBuilder.AppendLine(");");
        return stringBuilder.ToString();
    }

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

        public int GetHashCode(NamedTableReference obj)
        {
            return string.Join(".", obj.SchemaObject.Identifiers.Select(i => i.Value)).GetHashCode();
        }
    }
}