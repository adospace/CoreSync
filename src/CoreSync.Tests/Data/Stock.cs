using SQLite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoreSync.Tests.Data;

public class Stock
{
    [PrimaryKey]
    public Guid Id { get; set; }
    public string Symbol { get; set; } = default!;
}
