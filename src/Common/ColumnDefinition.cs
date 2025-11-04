namespace Coding4Fun.Etl.Common;


/// <summary>
/// Represents the definition of a column in a database table.
/// </summary>
/// <remarks>
/// This class is used to describe the structural properties of a database column,
/// such as name, data type, nullability, and size-related attributes like length, scale, and precision.
/// All properties are required and must be set when initializing the object.
/// </remarks>
public class ColumnDefinition
{
    /// <summary>
    /// Gets or sets the name of the column.
    /// </summary>
    /// <value>
    /// A string representing the column name. This property is required.
    /// </value>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the data type of the column as a string.
    /// </summary>
    /// <value>
    /// A string representing the SQL data type (e.g., "int", "varchar", "decimal"). This property is required.
    /// </value>
    public required string DataType { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the column allows null values.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if the column is nullable; otherwise, <see langword="false"/>. This property is required.
    /// </value>
    public required bool IsNullable { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the column uses the maximum size for variable-length data types.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if the column is defined with MAX size (e.g., varchar(MAX)); otherwise, <see langword="false"/>. This property is required.
    /// </value>
    public required bool IsMax { get; set; }

    /// <summary>
    /// Gets or sets the maximum length of the column for character or binary data types.
    /// </summary>
    /// <value>
    /// An integer representing the length of the column. For example, varchar(50) would have a length of 50.
    /// For MAX types (e.g., varchar(MAX)), this value may be ignored if <see cref="IsMax"/> is true.
    /// This property is required.
    /// </value>
    public required int Length { get; set; }

    /// <summary>
    /// Gets or sets the scale of the column for decimal or numeric data types.
    /// </summary>
    /// <value>
    /// An integer representing the number of digits to the right of the decimal point.
    /// This property is relevant only for decimal or numeric types.
    /// This property is required.
    /// </value>
    public required int Scale { get; set; }

    /// <summary>
    /// Gets or sets the precision of the column for decimal or numeric data types.
    /// </summary>
    /// <value>
    /// An integer representing the total number of digits that can be stored in the column.
    /// This property is relevant only for decimal or numeric types.
    /// This property is required.
    /// </value>
    public required int Precision { get; set; }
}