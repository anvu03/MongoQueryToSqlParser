// Practical Example: Using $project with Dapper
// This example shows how to use the MongoDbQueryParser with $project operator in a real application

using Dapper;
using Microsoft.Data.SqlClient;
using MongoSqlParser;

// Example 1: Basic Query with Projection
var converter = new MongoToSqlConverter();

string mongoQuery = @"{
    ""$project"": { ""name"": 1, ""email"": 1, ""age"": 1 },
    ""status"": ""active"",
    ""$sort"": { ""name"": 1 },
    ""$limit"": 10
}";

var result = converter.Parse(mongoQuery);

string sql = $@"
    SELECT {result.SelectClause}
    FROM Users
    WHERE {result.WhereClause}
    {result.OrderByClause}
    {result.PaginationClause}
";

using var connection = new SqlConnection("YourConnectionString");
var users = await connection.QueryAsync(sql, result.Parameters);

// Generated SQL:
// SELECT [name], [email], [age]
// FROM Users
// WHERE [status] = @p0
// ORDER BY [name] ASC
// OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY

// -------------------------------------------------------------------------------------

// Example 2: Computed Fields - Order Summary Report
string orderQuery = @"{
    ""$project"": {
        ""orderId"": ""$id"",
        ""customerName"": { ""$concat"": [""$firstName"", "" "", ""$lastName""] },
        ""subtotal"": { ""$multiply"": [""$quantity"", ""$unitPrice""] },
        ""total"": { ""$add"": [""$subtotal"", ""$tax"", ""$shipping""] }
    },
    ""orderDate"": { ""$gte"": ""2024-01-01"" },
    ""status"": ""completed"",
    ""$sort"": { ""orderDate"": -1 }
}";

var orderResult = converter.Parse(orderQuery);

string orderSql = $@"
    SELECT {orderResult.SelectClause}
    FROM Orders o
    JOIN Customers c ON o.customer_id = c.id
    WHERE {orderResult.WhereClause}
    {orderResult.OrderByClause}
";

var orders = await connection.QueryAsync(orderSql, orderResult.Parameters);

// Generated SQL:
// SELECT 
//     [id] AS [orderId],
//     CONCAT([firstName], ' ', [lastName]) AS [customerName],
//     ([quantity] * [unitPrice]) AS [subtotal],
//     (([quantity] * [unitPrice]) + [tax] + [shipping]) AS [total]  // Note: nested expressions work!
// FROM Orders o
// JOIN Customers c ON o.customer_id = c.id
// WHERE ([orderDate] >= @p0 AND [status] = @p1)
// ORDER BY [orderDate] DESC

// -------------------------------------------------------------------------------------

// Example 3: Using Field Mapping with Projection
var fieldMap = new Dictionary<string, string>
{
    { "UserId", "u.user_id" },
    { "UserName", "u.username" },
    { "Email", "u.email" },
    { "FirstName", "u.first_name" },
    { "LastName", "u.last_name" },
    { "RegistrationDate", "u.created_at" },
    { "IsActive", "u.is_active" }
};

var mappedConverter = new MongoToSqlConverter(fieldMap);

string userQuery = @"{
    ""$project"": {
        ""UserId"": 1,
        ""FullName"": { ""$concat"": [""$FirstName"", "" "", ""$LastName""] },
        ""Email"": 1,
        ""DisplayStatus"": { ""$ifNull"": [""$UserName"", ""Anonymous""] }
    },
    ""IsActive"": true,
    ""RegistrationDate"": { ""$gte"": ""2023-01-01"" }
}";

var userResult = mappedConverter.Parse(userQuery);

string userSql = $@"
    SELECT {userResult.SelectClause}
    FROM Users u
    WHERE {userResult.WhereClause}
";

var activeUsers = await connection.QueryAsync(userSql, userResult.Parameters);

// Generated SQL:
// SELECT 
//     u.user_id,
//     CONCAT(u.first_name, ' ', u.last_name) AS [FullName],
//     u.email,
//     ISNULL(u.username, 'Anonymous') AS [DisplayStatus]
// FROM Users u
// WHERE (u.is_active = @p0 AND u.created_at >= @p1)

// -------------------------------------------------------------------------------------

// Example 4: API Endpoint with Dynamic Projection

public class UserController : ControllerBase
{
    private readonly IDbConnection _connection;
    private readonly MongoToSqlConverter _converter;

    public UserController(IDbConnection connection)
    {
        _connection = connection;
        
        var fieldMap = new Dictionary<string, string>
        {
            { "id", "u.id" },
            { "name", "u.full_name" },
            { "email", "u.email" },
            { "created", "u.created_at" }
        };
        
        _converter = new MongoToSqlConverter(fieldMap);
    }

    [HttpPost("api/users/query")]
    public async Task<IActionResult> QueryUsers([FromBody] string mongoQuery)
    {
        try
        {
            // Parse the MongoDB-style query
            var result = _converter.Parse(mongoQuery);

            // Build the SQL query
            string sql = $@"
                SELECT {result.SelectClause}
                FROM Users u
                WHERE {result.WhereClause}
                {result.OrderByClause}
                {result.PaginationClause}
            ";

            // Execute with Dapper
            var users = await _connection.QueryAsync(sql, result.Parameters);

            return Ok(new 
            {
                data = users,
                count = users.Count(),
                query = new 
                {
                    select = result.SelectClause,
                    where = result.WhereClause,
                    orderBy = result.OrderByClause,
                    pagination = result.PaginationClause
                }
            });
        }
        catch (InvalidQueryException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (System.Security.SecurityException ex)
        {
            return BadRequest(new { error = "Query contains forbidden operators", details = ex.Message });
        }
    }
}

// Client request:
// POST /api/users/query
// {
//   "$project": { "name": 1, "email": 1 },
//   "created": { "$gte": "2024-01-01" },
//   "$sort": { "name": 1 },
//   "$limit": 20
// }

// Response:
// {
//   "data": [
//     { "name": "John Doe", "email": "john@example.com" },
//     { "name": "Jane Smith", "email": "jane@example.com" }
//   ],
//   "count": 2,
//   "query": {
//     "select": "[name], [email]",
//     "where": "[created] >= @p0",
//     "orderBy": "ORDER BY [name] ASC",
//     "pagination": "OFFSET 0 ROWS FETCH NEXT 20 ROWS ONLY"
//   }
// }

// -------------------------------------------------------------------------------------

// Example 5: Product Catalog with Discount Calculation

string productQuery = @"{
    ""$project"": {
        ""productName"": ""$name"",
        ""originalPrice"": ""$price"",
        ""discountAmount"": { ""$multiply"": [""$price"", ""$discountPercent""] },
        ""finalPrice"": { 
            ""$subtract"": [
                ""$price"", 
                { ""$multiply"": [""$price"", ""$discountPercent""] }
            ] 
        },
        ""stock"": 1
    },
    ""category"": ""Electronics"",
    ""stock"": { ""$gt"": 0 },
    ""$sort"": { ""finalPrice"": 1 }
}";

// Note: Nested expressions in $project are supported!
// The parser will correctly generate nested arithmetic operations

var productResult = converter.Parse(productQuery);

string productSql = $@"
    SELECT {productResult.SelectClause}
    FROM Products
    WHERE {productResult.WhereClause}
    {productResult.OrderByClause}
";

var products = await connection.QueryAsync(productSql, productResult.Parameters);

// -------------------------------------------------------------------------------------

// Example 6: Attribute-Based Mapping with Projection

using System.ComponentModel.DataAnnotations.Schema;

[Table("Users")]
public class UserModel
{
    [Column("u.id")]
    public int UserId { get; set; }
    
    [Column("u.first_name")]
    public string FirstName { get; set; }
    
    [Column("u.last_name")]
    public string LastName { get; set; }
    
    [Column("u.email")]
    public string Email { get; set; }
    
    [Column("u.created_at")]
    public DateTime CreatedAt { get; set; }
}

// Extract mapping from attributes
var fieldMap = AttributeMapper.ExtractFieldMapping<UserModel>();
var attributeConverter = new MongoToSqlConverter(fieldMap);

string attributeQuery = @"{
    ""$project"": {
        ""UserId"": 1,
        ""FullName"": { ""$concat"": [""$FirstName"", "" "", ""$LastName""] },
        ""Email"": 1
    },
    ""CreatedAt"": { ""$gte"": ""2024-01-01"" }
}";

var attributeResult = attributeConverter.Parse(attributeQuery);

// Generated SQL uses the mapped column names:
// SELECT u.id, CONCAT(u.first_name, ' ', u.last_name) AS [FullName], u.email
// FROM Users u
// WHERE u.created_at >= @p0

// -------------------------------------------------------------------------------------

// Tips and Best Practices:

// 1. Always use parameterized queries (automatically handled by the parser)
// 2. Define field mappings to hide internal database structure
// 3. Use projection to reduce data transfer (select only needed fields)
// 4. Combine $project with $sort and $limit for efficient queries
// 5. Use computed fields ($concat, $add, etc.) to reduce client-side processing
// 6. Leverage $ifNull for default values instead of null checks in application
// 7. Use attribute-based mapping for type-safe field references
