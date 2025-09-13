namespace Coding4Fun.Etl.Common;

public class TableDefinition
{
    public required string StageTableName { get; set; }
    public required string TempTableName { get; set; }
    public required bool IsMain { get; set; }
    public required string SelectStatement { get; set; }
    public required string CreateTempTableStatement { get; set; }
    public required string SelectBatchStatement { get; set; }
    public required ColumnDefinition[] Columns { get; set; }
}
