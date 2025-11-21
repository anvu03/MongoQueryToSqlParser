# MongoDB $project Operator - MS SQL Translation Examples

This document provides examples of how the `$project` operator in MongoDB queries is translated to MS SQL Server syntax.

## Quick Reference

The `$project` operator controls the SELECT clause in the generated SQL query, allowing you to:
- Include/exclude specific fields using simple 1/0 or true/false values

**Note:** Advanced features such as field aliasing, computed fields, expressions, string concatenation, arithmetic operations, and null handling are NOT supported.

## Usage in SqlQuery Result

After parsing a MongoDB query with `$project`, the result includes a `SelectClause` property:

```csharp
var converter = new MongoToSqlConverter();
var result = converter.Parse(mongoQuery);

// Build complete SQL query
string sql = $@"
    SELECT {result.SelectClause}
    FROM YourTable
    WHERE {result.WhereClause}
    {result.OrderByClause}
    {result.PaginationClause}
";
```

## Examples

### 1. Basic Field Inclusion

**MongoDB Query:**
```json
{
  "$project": { "name": 1, "email": 1, "age": 1 }
}
```

**Result:**
- `SelectClause`: `[name], [email], [age]`
- `WhereClause`: `1=1`

**Generated SQL:**
```sql
SELECT [name], [email], [age]
FROM Users
WHERE 1=1
```

---

### 2. Field Exclusion (0 or false)

**MongoDB Query:**
```json
{
  "$project": { "name": 1, "email": 1, "age": 0 }
}
```

**Result:**
- `SelectClause`: `[name], [email]`
- `WhereClause`: `1=1`

**Generated SQL:**
```sql
SELECT [name], [email]
FROM Users
WHERE 1=1
```

---

### 3. Using Boolean Values

**MongoDB Query:**
```json
{
  "$project": { "name": true, "email": false }
}
```

**Result:**
- `SelectClause`: `[name]`
- `WhereClause`: `1=1`

**Generated SQL:**
```sql
SELECT [name]
FROM Users
WHERE 1=1
```

---

## Unsupported Features

The following MongoDB `$project` features are **NOT supported**:

### Field Aliasing (Removed)
```json
// NOT SUPPORTED
{
  "$project": { 
    "userName": "$name", 
    "userEmail": "$email" 
  }
}
```

### String Concatenation (Removed)
```json
// NOT SUPPORTED
{
  "$project": { 
    "fullName": { 
      "$concat": ["$firstName", " ", "$lastName"] 
    } 
  }
}
```

### Arithmetic Operations (Removed)
```json
// NOT SUPPORTED
{
  "$project": { 
    "total": { "$add": ["$price", "$tax"] }
  }
}
```

### Null Handling (Removed)
```json
// NOT SUPPORTED
{
  "$project": { 
    "displayName": { "$ifNull": ["$nickname", "Anonymous"] } 
  }
}
```

### Computed Fields and Expressions (Removed)

All expression operators including `$concat`, `$add`, `$subtract`, `$multiply`, `$divide`, `$ifNull`, `$cond`, and `$literal` are not supported.

---

## Complete Example with Filtering and Sorting

**MongoDB Query:**
```json
{
  "$project": { "name": 1, "email": 1, "age": 1 },
  "status": "active",
  "age": { "$gt": 18 },
  "$sort": { "name": 1 },
  "$limit": 10,
  "$skip": 5
}
```

**Result:**
- `SelectClause`: `[name], [email], [age]`
- `WhereClause`: `([status] = @p0 AND [age] > @p1)`
- `OrderByClause`: `ORDER BY [name] ASC`
- `PaginationClause`: `OFFSET 5 ROWS FETCH NEXT 10 ROWS ONLY`
- `Parameters`: `{ "@p0": "active", "@p1": 18 }`

**Generated SQL:**
```sql
SELECT [name], [email], [age]
FROM Users
WHERE ([status] = @p0 AND [age] > @p1)
ORDER BY [name] ASC
OFFSET 5 ROWS FETCH NEXT 10 ROWS ONLY
```

---

## Integration with Field Mapping

The `$project` operator respects field mappings defined in the converter:

```csharp
var fieldMap = new Dictionary<string, string>
{
    { "UserId", "u.id" },
    { "Email", "u.email" },
    { "RegistrationDate", "u.registration_dt" }
};

var converter = new MongoToSqlConverter(fieldMap);

string mongoQuery = @"{
    ""$project"": { ""UserId"": 1, ""Email"": 1 }
}";

var result = converter.Parse(mongoQuery);
// SelectClause: "u.id, u.email"
```

---

## Default Behavior

If no `$project` operator is specified, the parser defaults to `SELECT *`:

```csharp
string mongoQuery = @"{ ""status"": ""active"" }";
var result = converter.Parse(mongoQuery);

// SelectClause: "*"
// WhereClause: "[status] = @p0"
```

**Generated SQL:**
```sql
SELECT *
FROM Users
WHERE [status] = @p0
```

---

## Notes

1. **Security**: The `$project` operator respects the same security constraints as other operators
2. **Field Mapping**: All field names in `$project` go through the field mapping system
3. **Simple Inclusion Only**: Only basic field inclusion (1/true) and exclusion (0/false) are supported
4. **No Advanced Features**: Field aliasing, computed expressions, and aggregation operators are not supported

---

## Testing

All projection features have been tested with the included test suite. Run the tests with:

```bash
dotnet run
```

Look for the section: `=== Starting $project Operator Tests ===`

