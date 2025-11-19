using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;

namespace MongoSqlParser;

/// <summary>
/// Provides functionality to extract field-to-column mappings from model classes
/// annotated with Microsoft's TableAttribute and ColumnAttribute from System.ComponentModel.DataAnnotations.Schema.
/// </summary>
public static class AttributeMapper
{
    /// <summary>
    /// Extracts field-to-column mapping information from a model class.
    /// </summary>
    /// <typeparam name="T">The model type annotated with attributes.</typeparam>
    /// <returns>A dictionary mapping field names to SQL column identifiers.</returns>
    public static Dictionary<string, string> ExtractFieldMapping<T>() where T : class
    {
        return ExtractFieldMapping(typeof(T));
    }

    /// <summary>
    /// Extracts field-to-column mapping information from a model class.
    /// </summary>
    /// <param name="modelType">The model type annotated with attributes.</param>
    /// <returns>A dictionary mapping field names to SQL column identifiers.</returns>
    public static Dictionary<string, string> ExtractFieldMapping(Type modelType)
    {
        if (modelType == null)
            throw new ArgumentNullException(nameof(modelType));

        var mapping = new Dictionary<string, string>();

        // Get table information (optional, not directly used in mapping but available)
        var tableAttr = modelType.GetCustomAttribute<TableAttribute>();

        // Iterate through all public properties
        var properties = modelType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        
        foreach (var property in properties)
        {
            // Check if property has ColumnAttribute
            var columnAttr = property.GetCustomAttribute<ColumnAttribute>();
            
            if (columnAttr != null)
            {
                // Use property name as the key
                string fieldName = property.Name;
                
                // Use the column name from the attribute
                // Microsoft's ColumnAttribute.Name property contains the column name
                string columnName = columnAttr.Name ?? property.Name;
                
                mapping[fieldName] = columnName;
            }
        }

        return mapping;
    }
}
