using SQLite;
using System;

namespace CoreSync.Tests.Data;

public class Valuation
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    [Indexed]
    public Guid StockId { get; set; }
    public DateTime Time { get; set; }
    public decimal Price { get; set; }
    [Ignore]
    public string IgnoreField { get; set; } = default!;
}