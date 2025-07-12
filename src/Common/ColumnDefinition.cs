namespace Coding4Fun.Etl.Common;

public class ColumnDefinition
{
    public required string Name { get; set; }
    public required string DataType { get; set; }
    public required bool IsNullable { get; set; }
    public required bool IsMax { get; set; }
    public required int Length { get; set; }
    public required int Precision { get; set; }
}