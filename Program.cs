using MongoSqlParser;
using System.ComponentModel.DataAnnotations.Schema;

Console.WriteLine("=== Starting MongoToSqlConverter Tests ===\n");

var converter = new MongoToSqlConverter();

// 1. Basic Equality
// Clean: No parens needed for single simple statements
RunTest(converter, "Basic Equality",
    json: @"{ ""status"": ""active"", ""age"": 25 }",
    expectedSql: "([status] = @p0 AND [age] = @p1)",
    expectedParams: new Dictionary<string, object> { { "@p0", "active" }, { "@p1", 25 } }
);

// 2. Greater Than
// Clean: Removed outer parens for single condition
RunTest(converter, "Greater Than",
    json: @"{ ""age"": { ""$gt"": 18 } }",
    expectedSql: "[age] > @p0",
    expectedParams: new Dictionary<string, object> { { "@p0", 18 } }
);

// 3. Logical OR
// Clean: Items inside OR don't need individual parens if they are simple equalities
RunTest(converter, "Logical OR",
    json: @"{ ""$or"": [ { ""role"": ""admin"" }, { ""role"": ""moderator"" } ] }",
    expectedSql: "([role] = @p0 OR [role] = @p1)",
    expectedParams: new Dictionary<string, object> { { "@p0", "admin" }, { "@p1", "moderator" } }
);

// 4. IN Operator
RunTest(converter, "IN Operator",
    json: @"{ ""id"": { ""$in"": [ 101, 102, 103 ] } }",
    expectedSql: "[id] IN (@p0, @p1, @p2)",
    expectedParams: new Dictionary<string, object> { { "@p0", 101 }, { "@p1", 102 }, { "@p2", 103 } }
);

// 5. Regex (Starts With)
RunTest(converter, "Regex Starts With",
    json: @"{ ""name"": { ""$regex"": ""^Smith"" } }",
    expectedSql: "[name] LIKE @p0",
    expectedParams: new Dictionary<string, object> { { "@p0", "Smith%" } }
);

// 6. Complex Nested Logic
// Clean: Logic is maintained, but noise is reduced
RunTest(converter, "Complex Nested Logic",
    json: @"{ ""$or"": [ { ""age"": 18 }, { ""joined"": { ""$gte"": ""2022-01-01"" } } ] }",
    expectedSql: "([age] = @p0 OR [joined] >= @p1)",
    expectedParams: new Dictionary<string, object> { { "@p0", 18 }, { "@p1", DateTime.Parse("2022-01-01") } }
);

// 7. Custom JSON Column Strategy
RunTest(converter, "JSON_VALUE Strategy",
    json: @"{ ""profile.country"": ""USA"" }",
    expectedSql: "JSON_VALUE(Data, '$.profile.country') = @p0",
    expectedParams: new Dictionary<string, object> { { "@p0", "USA" } },
    mapper: (s) => $"JSON_VALUE(Data, '$.{s}')"
);

// 8. Security Test (Should Fail)
RunSecurityTest(converter, "Block $where Injection",
    json: @"{ ""$where"": ""this.age > 10"" }"
);

// 9. Security Test (Should Fail)
RunSecurityTest(converter, "Block Unknown Operator",
    json: @"{ ""age"": { ""$unknownOp"": 5 } }"
);

// 10. Real World Complex Scenario
// Finds Active users, Registered recently, who are (High Spenders OR VIPs), excluding Test accounts.
RunTest(converter, "Real World Complex Query",
    json: @"
        {
            ""status"": ""Active"",
            ""registered_date"": { ""$gte"": ""2023-01-01"" },
            ""$or"": [
                { ""total_spend"": { ""$gt"": 500 } },
                { ""tier"": ""VIP"" }
            ],
            ""email"": { 
                ""$not"": { ""$regex"": ""@test.com$"" } 
            }
        }",
    expectedSql: "([status] = @p0 AND [registered_date] >= @p1 AND ([total_spend] > @p2 OR [tier] = @p3) AND NOT ([email] LIKE @p4))",
    expectedParams: new Dictionary<string, object>
    {
            { "@p0", "Active" },
            { "@p1", DateTime.Parse("2023-01-01") },
            { "@p2", 500 },
            { "@p3", "VIP" },
            { "@p4", "%@test.com" } // Parser converts regex end anchor '$' to SQL wildcard '%' at the start
    }
);

Console.WriteLine("\n=== Tests Completed ===");

// === AttributeMapper Tests ===
Console.WriteLine("\n=== Starting AttributeMapper Tests ===\n");

// Test 1: Basic field mapping extraction
RunAttributeMapperTest("Extract Basic Field Mapping",
    () => {
        var mapping = AttributeMapper.ExtractFieldMapping<UserModel>();
        return mapping.Count == 4 &&
               mapping["UserId"] == "u.id" &&
               mapping["Email"] == "u.email" &&
               mapping["RegistrationDate"] == "u.registration_dt" &&
               mapping["IsActive"] == "u.is_active";
    }
);

// Test 2: Model with complex SQL expressions
RunAttributeMapperTest("Extract Complex SQL Expressions",
    () => {
        var mapping = AttributeMapper.ExtractFieldMapping<ProductModel>();
        return mapping.Count == 3 &&
               mapping["ProductId"] == "p.id" &&
               mapping["Name"] == "p.product_name" &&
               mapping["Metadata"] == "JSON_VALUE(p.data, '$.metadata')";
    }
);

// Test 3: Integration test - Using AttributeMapper with MongoToSqlConverter
RunAttributeMapperTest("Integration with MongoToSqlConverter",
    () => {
        var fieldMap = AttributeMapper.ExtractFieldMapping<UserModel>();
        var converterWithMapping = new MongoToSqlConverter(fieldMap);
        
        string query = @"{ ""UserId"": 123, ""IsActive"": true }";
        var result = converterWithMapping.Parse(query);
        
        // Should map UserId to u.id and IsActive to u.is_active
        return result.WhereClause == "(u.id = @p0 AND u.is_active = @p1)" &&
               result.Parameters.Count == 2;
    }
);

// Test 4: Model without any attributes
RunAttributeMapperTest("Model Without Attributes Returns Empty Dictionary",
    () => {
        var mapping = AttributeMapper.ExtractFieldMapping<PlainModel>();
        return mapping.Count == 0;
    }
);

// Test 5: Model with partial attributes
RunAttributeMapperTest("Model With Partial Attributes",
    () => {
        var mapping = AttributeMapper.ExtractFieldMapping<PartialModel>();
        return mapping.Count == 2 &&
               mapping["Id"] == "id" &&
               mapping["Name"] == "name";
        // Status property should not be in the mapping since it lacks ColumnAttribute
    }
);

Console.WriteLine("\n=== AttributeMapper Tests Completed ===");
Console.ReadLine();

// ---------------------------------------------------------
// Simple Custom Test Harness
// ---------------------------------------------------------
static void RunTest(
    MongoToSqlConverter converter,
    string testName,
    string json,
    string expectedSql,
    Dictionary<string, object> expectedParams,
    Func<string, string>? mapper = null)
{
    try
    {
        var result = converter.Parse(json, mapper);

        // 1. Validate SQL
        bool sqlMatch = result.WhereClause == expectedSql;

        // 2. Validate Parameters
        bool paramsMatch = CompareParams(result.Parameters, expectedParams);

        if (sqlMatch && paramsMatch)
        {
            PrintResult(testName, true);
        }
        else
        {
            PrintResult(testName, false);
            Console.WriteLine($"   Expected SQL: {expectedSql}");
            Console.WriteLine($"   Actual SQL:   {result.WhereClause}");
            if (!paramsMatch) Console.WriteLine("   Parameter mismatch.");
        }
    }
    catch (Exception ex)
    {
        PrintResult(testName, false);
        Console.WriteLine($"   Exception: {ex.Message}");
    }
}

static void RunSecurityTest(MongoToSqlConverter converter, string testName, string json)
{
    try
    {
        converter.Parse(json);
        // If we get here, it failed to throw
        PrintResult(testName, false);
        Console.WriteLine("   Expected SecurityException, but none was thrown.");
    }
    catch (System.Security.SecurityException)
    {
        // Success, we caught the hacker
        PrintResult(testName, true);
    }
    catch (Exception ex)
    {
        PrintResult(testName, false);
        Console.WriteLine($"   Wrong exception type: {ex.GetType().Name} - {ex.Message}");
    }
}

static bool CompareParams(Dictionary<string, object> actual, Dictionary<string, object> expected)
{
    if (actual.Count != expected.Count) return false;
    foreach (var kvp in expected)
    {
        if (!actual.ContainsKey(kvp.Key)) return false;

        // Simple string comparison for values to handle different number types (Int32 vs Int64)
        if (actual[kvp.Key].ToString() != kvp.Value.ToString()) return false;
    }
    return true;
}

static void PrintResult(string name, bool pass)
{
    var color = pass ? ConsoleColor.Green : ConsoleColor.Red;
    Console.ForegroundColor = color;
    Console.Write(pass ? "[PASS] " : "[FAIL] ");
    Console.ResetColor();
    Console.WriteLine(name);
}

static void RunAttributeMapperTest(string testName, Func<bool> testFunc)
{
    try
    {
        bool result = testFunc();
        PrintResult(testName, result);
        if (!result)
        {
            Console.WriteLine("   Test assertion failed.");
        }
    }
    catch (Exception ex)
    {
        PrintResult(testName, false);
        Console.WriteLine($"   Exception: {ex.Message}");
    }
}

// === Test Model Classes ===

[Table("users")]
class UserModel
{
    [Column("u.id")]
    public int UserId { get; set; }
    
    [Column("u.email")]
    public string? Email { get; set; }
    
    [Column("u.registration_dt")]
    public DateTime RegistrationDate { get; set; }
    
    [Column("u.is_active")]
    public bool IsActive { get; set; }
}

[Table("products")]
class ProductModel
{
    [Column("p.id")]
    public int ProductId { get; set; }
    
    [Column("p.product_name")]
    public string? Name { get; set; }
    
    [Column("JSON_VALUE(p.data, '$.metadata')")]
    public string? Metadata { get; set; }
}

class PlainModel
{
    public int Id { get; set; }
    public string? Name { get; set; }
}

class PartialModel
{
    [Column("id")]
    public int Id { get; set; }
    
    [Column("name")]
    public string? Name { get; set; }
    
    // No attribute on this property
    public string? Status { get; set; }
}