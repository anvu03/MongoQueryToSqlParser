## 📝 README.md Content

### MongoQueryToSqlParser

A robust and secure library for translating human-readable, MongoDB-style JSON query syntax into production-ready MS SQL Server clauses. Designed for C\# applications that need a flexible, dynamic query layer without exposing the underlying relational database structure.

### ✨ Features

  * **Secure Parameterization:** Automatically converts all values into Dapper-compatible parameters (`@p0`, `@p1`) to prevent SQL Injection.
  * **Complex Logic Translation:** Supports nested `$or`, `$and`, `$not`, `$in`, and all comparison operators (`$gt`, `$lte`, etc.).
  * **Projection Support:** MongoDB-style `$project` operator to control SELECT clause with field inclusion, aliasing, and computed expressions.
  * **Flexible Field Mapping:** Allows mapping public-facing field names (e.g., `"reg_date"`) to complex internal identifiers (e.g., `"u.registration_dt"`).
  * **Attribute-Based Mapping:** Automatically extract field-to-column mappings from model classes using Microsoft's `[Table]` and `[Column]` attributes.
  * **Pagination & Sorting:** Converts MongoDB's `$sort`, `$limit`, and `$skip` directly into SQL Server's `ORDER BY` and `OFFSET/FETCH` clauses.
  * **Security:** Enforces an operator **Allow-list** to immediately block dangerous injection vectors like `$where` and `$script`.
  * **Null Handling:** Correctly translates MongoDB's `null` and `$exists` checks into standard SQL `IS NULL`/`IS NOT NULL` statements.

### 🚀 Usage Example

The parser is initialized with a map that defines the entity's public fields and their corresponding internal SQL columns (including aliases or JSON paths).

```csharp
// 1. Define the Entity Map (Contextual for a specific table/entity)
var userMap = new Dictionary<string, string>
{
    { "user_id", "u.id" }, // Maps to a SQL alias (u.id)
    { "reg_date", "registration_dt" },
    { "email_info", "JSON_VALUE(UserData, '$.email')" } // Example JSON path
};

// 2. Instantiate the converter
var converter = new MongoToSqlConverter(userMap);

// 3. Define a complex query (Active users, sorted, limited)
string mongoQuery = @"{
    ""is_active"": true,
    ""reg_date"": { ""$gte"": ""2023-01-01"" },
    ""$sort"": { ""reg_date"": -1 },
    ""$limit"": 20
}";

// 4. Parse the query
var result = converter.Parse(mongoQuery);

// 5. Construct Final SQL (in your repository layer)
string finalSql = $@"
    SELECT * FROM Users u 
    WHERE {result.WhereClause}
    {result.OrderByClause}
    {result.PaginationClause};";

/* Expected SQL Output:
SELECT * FROM Users u 
WHERE (u.id = @p0 AND registration_dt >= @p1)
ORDER BY registration_dt DESC
OFFSET 0 ROWS FETCH NEXT 20 ROWS ONLY;
*/
```

### 📋 Attribute-Based Mapping

For even cleaner code, you can use Microsoft's `[Table]` and `[Column]` attributes to define the mapping directly on your model classes:

```csharp
using System.ComponentModel.DataAnnotations.Schema;

// 1. Define your model with attributes
[Table("Users")]
public class UserQueryModel
{
    [Column("u.id")]
    public int UserId { get; set; }
    
    [Column("u.registration_dt")]
    public DateTime RegistrationDate { get; set; }
    
    [Column("u.is_active")]
    public bool IsActive { get; set; }
}

// 2. Extract the mapping using AttributeMapper
var fieldMap = AttributeMapper.ExtractFieldMapping<UserQueryModel>();

// 3. Create converter with the extracted mapping
var converter = new MongoToSqlConverter(fieldMap);

// 4. Now you can use property names directly in your queries
string mongoQuery = @"{
    ""UserId"": 123,
    ""IsActive"": true,
    ""RegistrationDate"": { ""$gte"": ""2023-01-01"" }
}";

var result = converter.Parse(mongoQuery);

/* Generated SQL uses the mapped column names:
WHERE (u.id = @p0 AND u.is_active = @p1 AND u.registration_dt >= @p2)
*/
```

### 🎯 Projection with $project Operator

The `$project` operator allows you to control which fields appear in the SELECT clause, similar to MongoDB's aggregation pipeline. It supports field inclusion, exclusion, aliasing, and computed expressions.

#### Basic Field Inclusion

```csharp
string mongoQuery = @"{
    ""$project"": { ""name"": 1, ""email"": 1, ""age"": 1 }
}";

var result = converter.Parse(mongoQuery);

/* Generated SQL:
SELECT [name], [email], [age] FROM ...
*/
```

#### Field Aliasing

Rename fields in the output by referencing them with `$`:

```csharp
string mongoQuery = @"{
    ""$project"": { 
        ""userName"": ""$name"", 
        ""userEmail"": ""$email"" 
    }
}";

var result = converter.Parse(mongoQuery);

/* Generated SQL:
SELECT [name] AS [userName], [email] AS [userEmail] FROM ...
*/
```

#### String Concatenation

Combine multiple fields into one:

```csharp
string mongoQuery = @"{
    ""$project"": { 
        ""fullName"": { 
            ""$concat"": [""$firstName"", "" "", ""$lastName""] 
        } 
    }
}";

var result = converter.Parse(mongoQuery);

/* Generated SQL (secure parameterized):
SELECT CONCAT([firstName], @p0, [lastName]) AS [fullName] FROM ...
Parameters: @p0 = " "
*/
```

#### Arithmetic Operations

Perform calculations on numeric fields:

```csharp
// Addition
string mongoQuery = @"{
    ""$project"": { 
        ""total"": { ""$add"": [""$price"", ""$tax""] } 
    }
}";

/* Generated SQL:
SELECT ([price] + [tax]) AS [total] FROM ...
*/

// Subtraction
string mongoQuery = @"{
    ""$project"": { 
        ""discount"": { ""$subtract"": [""$price"", ""$discount_amount""] } 
    }
}";

/* Generated SQL:
SELECT ([price] - [discount_amount]) AS [discount] FROM ...
*/

// Multiplication
string mongoQuery = @"{
    ""$project"": { 
        ""lineTotal"": { ""$multiply"": [""$quantity"", ""$price""] } 
    }
}";

/* Generated SQL:
SELECT ([quantity] * [price]) AS [lineTotal] FROM ...
*/

// Division
string mongoQuery = @"{
    ""$project"": { 
        ""average"": { ""$divide"": [""$sum"", ""$count""] } 
    }
}";

/* Generated SQL:
SELECT ([sum] / [count]) AS [average] FROM ...
*/
```

#### Null Handling with $ifNull

Provide default values for null fields:

```csharp
string mongoQuery = @"{
    ""$project"": { 
        ""displayName"": { ""$ifNull"": [""$nickname"", ""Unknown""] } 
    }
}";

var result = converter.Parse(mongoQuery);

/* Generated SQL (secure parameterized):
SELECT ISNULL([nickname], @p0) AS [displayName] FROM ...
Parameters: @p0 = "Unknown"
*/
```

#### Complex Query: Projection + Filter + Sort

Combine multiple operators for powerful queries:

```csharp
string mongoQuery = @"{
    ""$project"": { ""name"": 1, ""email"": 1, ""age"": 1 },
    ""age"": { ""$gt"": 18 },
    ""status"": ""active"",
    ""$sort"": { ""name"": 1 },
    ""$limit"": 10
}";

var result = converter.Parse(mongoQuery);

/* Generated SQL components:
SELECT: [name], [email], [age]
WHERE: ([age] > @p0 AND [status] = @p1)
ORDER BY: [name] ASC
PAGINATION: OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY
*/
```

### 📊 Supported $project Expressions

| Expression | Description | SQL Translation |
|------------|-------------|-----------------|
| `{ "field": 1 }` | Include field | `[field]` |
| `{ "field": 0 }` | Exclude field | (omitted) |
| `{ "alias": "$field" }` | Field aliasing | `[field] AS [alias]` |
| `{ "$concat": [...] }` | String concatenation | `CONCAT(...)` |
| `{ "$add": [...] }` | Addition | `(a + b)` |
| `{ "$subtract": [...] }` | Subtraction | `(a - b)` |
| `{ "$multiply": [...] }` | Multiplication | `(a * b)` |
| `{ "$divide": [...] }` | Division | `(a / b)` |
| `{ "$ifNull": [field, default] }` | Null coalescing | `ISNULL(field, default)` |