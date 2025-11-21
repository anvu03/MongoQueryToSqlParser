using System.Collections.Generic;

public class SqlQuery
{
    public string SelectClause { get; set; } = "*";
    public string WhereClause { get; set; } = "1=1";
    public Dictionary<string, object> Parameters { get; set; } = new();
    public string OrderByClause { get; set; } = string.Empty;
    public string PaginationClause { get; set; } = string.Empty;
}