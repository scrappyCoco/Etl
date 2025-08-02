namespace Coding4Fun.Etl.Common;

public class PipelineConfiguration
{
    public required string ProcedureName { get; set; }
    public required string EtlSql { get; set; }
    public required TableDefinition[] TableDefinitions { get; set; }
}