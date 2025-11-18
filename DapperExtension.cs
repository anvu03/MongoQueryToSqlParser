using Dapper;

public static class DapperExtensions
{
    /// <summary>
    /// Converts the generic parameter Dictionary into Dapper's DynamicParameters, 
    /// ensuring strings are marked as AnsiString (VARCHAR) for performance/indexing 
    /// if the database columns are not NVARCHAR.
    /// </summary>
    public static DynamicParameters ToDynamicParameters(this Dictionary<string, object> source)
    {
        var parameters = new DynamicParameters();

        foreach (var kvp in source)
        {
            if (kvp.Value is string strVal)
            {
                // Use DbString to force VARCHAR type, which is often faster/correct for indexing
                parameters.Add(kvp.Key, new DbString { Value = strVal, IsAnsi = true, IsFixedLength = false });
            }
            else
            {
                // Add all other types (int, DateTime, bool, etc.) normally
                parameters.Add(kvp.Key, kvp.Value);
            }
        }
        return parameters;
    }
}