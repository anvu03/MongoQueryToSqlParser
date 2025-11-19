using MongoSqlParser;
using System.ComponentModel.DataAnnotations.Schema;

// Demo: AttributeMapper functionality
Console.WriteLine("=== AttributeMapper Demo ===\n");

// 1. Define a model with attributes
[Table("Orders")]
class OrderQueryModel
{
    [Column("o.order_id")]
    public int OrderId { get; set; }
    
    [Column("o.customer_name")]
    public string? CustomerName { get; set; }
    
    [Column("o.order_date")]
    public DateTime OrderDate { get; set; }
    
    [Column("o.total_amount")]
    public decimal TotalAmount { get; set; }
}

// 2. Extract the mapping
var fieldMap = AttributeMapper.ExtractFieldMapping<OrderQueryModel>();

Console.WriteLine("Extracted Field Mappings:");
foreach (var kvp in fieldMap)
{
    Console.WriteLine($"  {kvp.Key} -> {kvp.Value}");
}

// 3. Use it with MongoToSqlConverter
var converter = new MongoToSqlConverter(fieldMap);

string query = @"{
    ""CustomerName"": ""John Doe"",
    ""TotalAmount"": { ""$gte"": 100 },
    ""OrderDate"": { ""$gte"": ""2024-01-01"" }
}";

Console.WriteLine($"\nInput Query:\n{query}");

var result = converter.Parse(query);

Console.WriteLine($"\nGenerated SQL WHERE Clause:\n{result.WhereClause}");

Console.WriteLine($"\nParameters:");
foreach (var param in result.Parameters)
{
    Console.WriteLine($"  {param.Key} = {param.Value}");
}

Console.WriteLine("\n=== Demo Complete ===");
