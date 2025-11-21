using System.Text.Json;
using System.Text.Json.Nodes;

namespace MongoSqlParser;

public class MongoToSqlConverter
{
    // === Configuration and State ===

    // SECURITY: Strict Allow-list of operators. $where, $script are forbidden.
    private static readonly HashSet<string> _allowedOperators = new()
    {
        "$eq", "$ne", "$gt", "$gte", "$lt", "$lte",
        "$in", "$nin", "$or", "$and", "$not", "$regex", "$exists" // Added $exists
    };

    private static readonly HashSet<string> _allowedTopLevelOperators = new()
    {
        "$project", "$sort", "$limit", "$skip"
    };

    private static readonly Dictionary<string, string> _simpleOps = new()
    {
        { "$eq", "=" },
        { "$ne", "<>" },
        { "$gt", ">" },
        { "$gte", ">=" },
        { "$lt", "<" },
        { "$lte", "<=" }
    };

    private readonly Dictionary<string, string> _fieldMap;

    public MongoToSqlConverter(Dictionary<string, string>? fieldMap = null)
    {
        _fieldMap = fieldMap ?? new Dictionary<string, string>();
    }

    // === Main Entry Point ===

    public SqlQuery Parse(
        string jsonQuery,
        Func<string, string>? columnMapper = null)
    {
        var result = new SqlQuery();
        if (string.IsNullOrWhiteSpace(jsonQuery)) return result;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(jsonQuery);
        }
        catch (JsonException ex)
        {
            throw new InvalidQueryException($"Invalid JSON format: {ex.Message}");
        }

        if (root == null || root.GetValueKind() != JsonValueKind.Object)
            return result;

        var obj = root.AsObject();
        columnMapper ??= (field) => $"[{field}]";

        var context = new ParseContext(columnMapper, _fieldMap);

        // 1. Projection (SELECT clause)
        if (obj.TryGetPropertyValue("$project", out JsonNode? projectNode) && projectNode != null)
        {
            result.SelectClause = context.ParseProject(projectNode);
        }

        // 2. Filtering (WHERE clause)
        result.WhereClause = context.ParseObject(root, "AND");
        result.Parameters = context.Parameters;

        // 3. Sorting and Pagination (ORDER BY / OFFSET)
        if (obj.TryGetPropertyValue("$sort", out JsonNode? sortNode) && sortNode != null)
        {
            result.OrderByClause = context.ParseSort(sortNode);
        }
        result.PaginationClause = context.ParseLimitSkip(obj);

        return result;
    }

    // === Private Context Class (Thread-Safe State) ===

    private class ParseContext
    {
        public Dictionary<string, object> Parameters { get; } = new();
        private int _paramCounter = 0;
        private readonly Func<string, string> _columnMapper;
        private readonly Dictionary<string, string> _fieldMap;

        public ParseContext(Func<string, string> columnMapper, Dictionary<string, string> fieldMap)
        {
            _columnMapper = columnMapper;
            _fieldMap = fieldMap;
        }

        // --- Core Parsing Methods ---

        public string ParseObject(JsonNode node, string logicJoin)
        {
            var conditions = new List<string>();

            if (node is JsonObject obj)
            {
                foreach (var property in obj)
                {
                    string key = property.Key;
                    JsonNode? value = property.Value;

                    // Ignore pagination/sort/project operators from WHERE clause generation
                    if (_allowedTopLevelOperators.Contains(key)) continue;

                    if (value == null) continue;

                    if (key.StartsWith("$"))
                    {
                        if (!_allowedOperators.Contains(key))
                            throw new System.Security.SecurityException($"Operator '{key}' is not allowed.");

                        if (key == "$or") conditions.Add(ParseLogicArray(value, "OR"));
                        else if (key == "$and") conditions.Add(ParseLogicArray(value, "AND"));
                        else if (key == "$not") conditions.Add($"NOT ({ParseObject(value, "AND")})");
                        else throw new InvalidQueryException($"Operator '{key}' is not supported at this level.");
                    }
                    else
                    {
                        conditions.Add(ParseField(key, value));
                    }
                }
            }
            else
            {
                throw new InvalidQueryException("Root query must be a JSON object.");
            }

            if (conditions.Count == 0) return "1=1";
            if (conditions.Count == 1) return conditions[0];
            return $"({string.Join($" {logicJoin} ", conditions)})";
        }

        private string ParseLogicArray(JsonNode node, string logic)
        {
            if (node is JsonArray arr)
            {
                if (arr.Count == 0) return "1=1";
                var subConditions = arr.Select(item => ParseObject(item!, "AND")).ToList();
                return $"({string.Join($" {logic} ", subConditions)})";
            }
            throw new InvalidQueryException($"Value for logic operator must be an array.");
        }

        private string ParseField(string field, JsonNode value)
        {
            string sqlColumn = GetMappedSqlIdentifier(field);

            // 1. Handle Explicit NULL: { "field": null }
            if (value is null || (value is JsonValue jVal && jVal.GetValue<JsonElement>().ValueKind == JsonValueKind.Null))
            {
                return $"{sqlColumn} IS NULL";
            }

            // 2. Check if value is an object (Complex Operator) or Primitive (Equality)
            if (value is JsonObject valObj)
            {
                // If complex, check for the special case { "field": { "$ne": null } }
                if (valObj.TryGetPropertyValue("$ne", out JsonNode? neValue) && (neValue is null || neValue.GetValueKind() == JsonValueKind.Null))
                {
                    return $"{sqlColumn} IS NOT NULL";
                }

                var conditions = new List<string>();
                foreach (var op in valObj)
                {
                    conditions.Add(ParseOperator(sqlColumn, op.Key, op.Value));
                }
                return string.Join(" AND ", conditions);
            }
            else
            {
                // Implicit Equality: { "age": 25 }
                string paramName = AddParameter(GetValue(value));
                return $"{sqlColumn} = {paramName}";
            }
        }

        // --- Operator and Utility Methods ---

        private string ParseOperator(string column, string op, JsonNode? value)
        {
            if (!_allowedOperators.Contains(op))
                throw new System.Security.SecurityException($"Operator '{op}' is blocked.");

            if (value == null) return "1=0"; // Should be caught earlier, but safety check

            // Simple Comparison ($, $ne, $gt, etc.)
            if (_simpleOps.TryGetValue(op, out string? sqlOp))
            {
                string paramName = AddParameter(GetValue(value));
                return $"{column} {sqlOp} {paramName}";
            }

            switch (op)
            {
                case "$in":
                case "$nin":
                    return HandleIn(column, value, op == "$nin");
                case "$regex":
                    return HandleRegex(column, value);
                case "$exists":
                    bool exists = true;
                    if (value is JsonValue val && val.TryGetValue<bool>(out bool b)) exists = b;
                    return exists ? $"{column} IS NOT NULL" : $"{column} IS NULL";
                case "$not":
                    if (value is JsonObject subObj)
                    {
                        var subConditions = new List<string>();
                        foreach (var sub in subObj) subConditions.Add(ParseOperator(column, sub.Key, sub.Value));
                        return $"NOT ({string.Join(" AND ", subConditions)})";
                    }
                    throw new InvalidQueryException("$not must contain an operator object.");
                default:
                    throw new InvalidQueryException($"Operator '{op}' logic is not implemented.");
            }
        }

        // --- Projection Method ---

        public string ParseProject(JsonNode projectNode)
        {
            var selectedFields = new List<string>();

            if (projectNode is JsonObject projectObj)
            {
                foreach (var prop in projectObj)
                {
                    string fieldName = prop.Key;
                    JsonNode? value = prop.Value;

                    if (value == null) continue;

                    // Get the inclusion/exclusion value
                    bool includeField = true;
                    if (value is JsonValue val)
                    {
                        if (val.TryGetValue<int>(out int intValue))
                        {
                            includeField = intValue != 0;
                        }
                        else if (val.TryGetValue<bool>(out bool boolValue))
                        {
                            includeField = boolValue;
                        }
                    }

                    // Simple inclusion only - no aliasing or expressions
                    if (includeField)
                    {
                        string sqlColumn = GetMappedSqlIdentifier(fieldName);
                        selectedFields.Add(sqlColumn);
                    }
                }
            }

            if (selectedFields.Count == 0) return "*";
            return string.Join(", ", selectedFields);
        }



        // --- Sorting and Pagination Methods ---

        public string ParseSort(JsonNode sortNode)
        {
            var sortConditions = new List<string>();

            if (sortNode is JsonObject sortObj)
            {
                foreach (var prop in sortObj)
                {
                    string sqlColumn = GetMappedSqlIdentifier(prop.Key);
                    string dir = "ASC";

                    if (prop.Value is JsonValue val)
                    {
                        // Check for int (1 or -1)
                        if (val.TryGetValue<int>(out int direction))
                        {
                            dir = (direction == 1) ? "ASC" : "DESC";
                        }
                        // Check for string ("asc" or "desc")
                        else if (val.TryGetValue<string>(out string? strDir))
                        {
                            dir = strDir.Equals("desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";
                        }
                    }
                    sortConditions.Add($"{sqlColumn} {dir}");
                }
            }

            if (sortConditions.Count == 0) return string.Empty;
            return $"ORDER BY {string.Join(", ", sortConditions)}";
        }

        public string ParseLimitSkip(JsonObject root)
        {
            int skip = 0;
            int limit = 0;

            if (root.TryGetPropertyValue("$skip", out JsonNode? skipNode) && skipNode is JsonValue skipVal) skipVal.TryGetValue<int>(out skip);
            if (root.TryGetPropertyValue("$limit", out JsonNode? limitNode) && limitNode is JsonValue limitVal) limitVal.TryGetValue<int>(out limit);

            if (limit > 0 || skip > 0)
            {
                string clause = $"OFFSET {skip} ROWS";
                if (limit > 0)
                {
                    clause += $" FETCH NEXT {limit} ROWS ONLY";
                }
                return clause;
            }
            return string.Empty;
        }

        // --- Mapping and Parameter Helpers ---

        private string GetMappedSqlIdentifier(string field)
        {
            // 1. Resolve the Public Field Name to the SQL Identifier (may be aliased/qualified from _fieldMap)
            string sqlIdentifier = field;
            bool wasMapped = false;

            if (_fieldMap.TryGetValue(field, out string? mappedName))
            {
                sqlIdentifier = mappedName;
                wasMapped = true;
            }

            // 2. Determine Final SQL Column String
            // If the name was explicitly mapped AND contains dots/brackets, it's an alias (u.column) and we trust the map source.
            if (wasMapped && (sqlIdentifier.Contains('.') || sqlIdentifier.Contains('[')))
            {
                return sqlIdentifier;
            }

            // Otherwise (if it was NOT explicitly mapped OR it's a simple mapped name), 
            // we must apply the column mapper. This handles JSON paths (like 'profile.country') 
            // passed directly by the consumer.
            return _columnMapper(sqlIdentifier);
        }

        // (Helper methods HandleIn, HandleRegex, AddParameter, GetValue remain as previously defined)
        // Implement the other required placeholders (HandleIn, HandleRegex) fully.
        private string HandleIn(string column, JsonNode value, bool isNot)
        {
            if (value is JsonArray arr)
            {
                if (arr.Count == 0) return "1=0";

                var paramNames = new List<string>();
                foreach (var item in arr)
                {
                    paramNames.Add(AddParameter(GetValue(item!)));
                }
                string inClause = string.Join(", ", paramNames);
                return $"{column} {(isNot ? "NOT IN" : "IN")} ({inClause})";
            }
            throw new InvalidQueryException("$in value must be an array.");
        }
        private string HandleRegex(string column, JsonNode value)
        {
            string pattern = value.ToString();
            string sqlPattern;

            if (pattern.StartsWith("^")) sqlPattern = pattern.TrimStart('^') + "%";
            else if (pattern.EndsWith("$")) sqlPattern = "%" + pattern.TrimEnd('$');
            else sqlPattern = "%" + pattern + "%";

            string paramName = AddParameter(sqlPattern);

            return $"{column} LIKE {paramName}";
        }
        private string AddParameter(object value)
        {
            string name = $"@p{_paramCounter++}";
            Parameters[name] = value;
            return name;
        }
        private object GetValue(JsonNode node)
        {
            if (node is JsonValue val)
            {
                var element = val.GetValue<JsonElement>();
                switch (element.ValueKind)
                {
                    case JsonValueKind.String:
                        if (DateTime.TryParse(element.GetString(), out DateTime dt)) return dt;
                        return element.GetString()!;
                    case JsonValueKind.Number:
                        if (element.TryGetInt32(out int i)) return i;
                        if (element.TryGetInt64(out long l)) return l;
                        return element.GetDouble();
                    case JsonValueKind.True: return true;
                    case JsonValueKind.False: return false;
                    default: return element.ToString();
                }
            }
            return node.ToString();
        }
    }
}