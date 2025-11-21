# MongoDB $project Operator - MS SQL Translation Examples

This document provides comprehensive examples of how the `$project` operator in MongoDB queries is translated to MS SQL Server syntax.

## Quick Reference

The `$project` operator controls the SELECT clause in the generated SQL query, allowing you to:
- Include/exclude specific fields
- Rename fields (aliasing)
- Create computed fields using expressions
- Perform string concatenation and arithmetic operations
- Handle null values with defaults

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

### 1. Basic Field Selection

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

### 2. Field Aliasing

**MongoDB Query:**
```json
{
  "$project": { 
    "userName": "$name", 
    "userEmail": "$email" 
  }
}
```

**Result:**
- `SelectClause`: `[name] AS [userName], [email] AS [userEmail]`
- `WhereClause`: `1=1`

**Generated SQL:**
```sql
SELECT [name] AS [userName], [email] AS [userEmail]
FROM Users
WHERE 1=1
```

---

### 3. String Concatenation

**MongoDB Query:**
```json
{
  "$project": { 
    "fullName": { 
      "$concat": ["$firstName", " ", "$lastName"] 
    } 
  }
}
```

**Result:**
- `SelectClause`: `CONCAT([firstName], ' ', [lastName]) AS [fullName]`
- `WhereClause`: `1=1`

**Generated SQL:**
```sql
SELECT CONCAT([firstName], ' ', [lastName]) AS [fullName]
FROM Users
WHERE 1=1
```

---

### 4. Arithmetic Operations

#### Addition
**MongoDB Query:**
```json
{
  "$project": { 
    "total": { "$add": ["$price", "$tax"] } 
  }
}
```

**Generated SQL:**
```sql
SELECT ([price] + [tax]) AS [total]
FROM Products
WHERE 1=1
```

#### Subtraction
**MongoDB Query:**
```json
{
  "$project": { 
    "netPrice": { "$subtract": ["$price", "$discount"] } 
  }
}
```

**Generated SQL:**
```sql
SELECT ([price] - [discount]) AS [netPrice]
FROM Products
WHERE 1=1
```

#### Multiplication
**MongoDB Query:**
```json
{
  "$project": { 
    "lineTotal": { "$multiply": ["$quantity", "$price"] } 
  }
}
```

**Generated SQL:**
```sql
SELECT ([quantity] * [price]) AS [lineTotal]
FROM OrderItems
WHERE 1=1
```

#### Division
**MongoDB Query:**
```json
{
  "$project": { 
    "average": { "$divide": ["$sum", "$count"] } 
  }
}
```

**Generated SQL:**
```sql
SELECT ([sum] / [count]) AS [average]
FROM Statistics
WHERE 1=1
```

---

### 5. Null Handling with $ifNull

**MongoDB Query:**
```json
{
  "$project": { 
    "displayName": { "$ifNull": ["$nickname", "Anonymous"] } 
  }
}
```

**Result:**
- `SelectClause`: `ISNULL([nickname], 'Anonymous') AS [displayName]`

**Generated SQL:**
```sql
SELECT ISNULL([nickname], 'Anonymous') AS [displayName]
FROM Users
WHERE 1=1
```

---

### 6. Complex Query: Projection + Filtering + Sorting

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

### 7. Mixed Expressions

**MongoDB Query:**
```json
{
  "$project": { 
    "userName": "$name",
    "fullName": { "$concat": ["$firstName", " ", "$lastName"] },
    "totalPrice": { "$add": ["$price", "$tax"] },
    "displayStatus": { "$ifNull": ["$status", "pending"] }
  },
  "isActive": true
}
```

**Result:**
- `SelectClause`: `[name] AS [userName], CONCAT([firstName], ' ', [lastName]) AS [fullName], ([price] + [tax]) AS [totalPrice], ISNULL([status], 'pending') AS [displayStatus]`
- `WhereClause`: `[isActive] = @p0`

**Generated SQL:**
```sql
SELECT 
    [name] AS [userName], 
    CONCAT([firstName], ' ', [lastName]) AS [fullName], 
    ([price] + [tax]) AS [totalPrice], 
    ISNULL([status], 'pending') AS [displayStatus]
FROM Users
WHERE [isActive] = @p0
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

## Supported Expressions Summary

| MongoDB Expression | SQL Translation | Example |
|-------------------|-----------------|---------|
| `{ "field": 1 }` | `[field]` | Field inclusion |
| `{ "alias": "$field" }` | `[field] AS [alias]` | Aliasing |
| `{ "$concat": ["$a", "text", "$b"] }` | `CONCAT([a], 'text', [b])` | String concatenation |
| `{ "$add": ["$a", "$b"] }` | `([a] + [b])` | Addition |
| `{ "$subtract": ["$a", "$b"] }` | `([a] - [b])` | Subtraction |
| `{ "$multiply": ["$a", "$b"] }` | `([a] * [b])` | Multiplication |
| `{ "$divide": ["$a", "$b"] }` | `([a] / [b])` | Division |
| `{ "$ifNull": ["$field", "default"] }` | `ISNULL([field], 'default')` | Null coalescing |

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
3. **Computed Fields**: Expressions are evaluated at query time in SQL Server
4. **No Exclusion-Only**: If you specify `$project`, include the fields you want (exclusion-only mode like `{ "field": 0 }` is supported but typically used with inclusion)

---

## Testing

All projection features have been tested with the included test suite. Run the tests with:

```bash
dotnet run
```

Look for the section: `=== Starting $project Operator Tests ===`
