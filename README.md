# DynoMapper 🚀

**Zero-boilerplate, pure dynamic runtime data mapper for .NET 8.**

Write your own SQL — JOINs, WHERE, GROUP BY, stored procedures, anything.
Get back fully dynamic objects with **no DTO or model classes ever required**.
Works with `ISqlHelper` (SQL Server, PostgreSQL, MySQL) or your existing **EF Core DbContext**.

---

## 📦 Install

```bash
dotnet add package DynoMapper
```

---

## ⚡ Two Ways to Use DynoMapper

| | ISqlHelper Path | EF Core Path |
|---|---|---|
| Use when | No EF Core in project | Already using EF Core |
| Program.cs | `AddDynoMapper(...)` | `AddDbContext<T>(...)` |
| Inject | `ISqlHelper sql` | `AppDbContext context` |
| Method | `sql.QueryListAsync(...)` | `context.DynoQueryAsync(...)` |
| Dynamic result | ✅ Same `DynoResult` | ✅ Same `DynoResult` |
| No DTO needed | ✅ | ✅ |

---

## 🛠 Setup

### Path 1 — ISqlHelper (SQL Server / PostgreSQL / MySQL)

**appsettings.json:**
```json
{
  "DynoMapper": {
    "ConnectionString": "Server=localhost;Database=MyDb;Trusted_Connection=True;TrustServerCertificate=True;",
    "Provider": "SqlServer",
    "CommandTimeoutSeconds": 30,
    "Retry": {
      "MaxAttempts": 3,
      "DelayMs": 200,
      "UseJitter": true,
      "Enabled": true
    }
  }
}
```

**Program.cs — Option A (inline):**
```csharp
using DynoMapper.Core;
using DynoMapper.Extensions;

builder.Services.AddDynoMapper(options =>
{
    options.ConnectionString      = builder.Configuration["DynoMapper:ConnectionString"]!;
    options.Provider              = DbProvider.SqlServer; // SqlServer | PostgreSQL | MySQL
    options.CommandTimeoutSeconds = 30;
    options.Retry.MaxAttempts     = 3;
    options.Retry.DelayMs         = 200;
    options.Retry.UseJitter       = true;
});

builder.Services.AddControllers();
```

**Program.cs — Option B (full config from appsettings):**
```csharp
using DynoMapper.Extensions;

// reads entire DynoMapper section automatically — one line
builder.Services.AddDynoMapper(builder.Configuration);

builder.Services.AddControllers();
```

---

### Path 2 — EF Core

No `AddDynoMapper` needed. Just register your `DbContext` normally.

**Program.cs:**
```csharp
using DynoMapper.Extensions; // only this using needed

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration["DynoMapper:ConnectionString"]));

builder.Services.AddControllers();
```

**AppDbContext:**
```csharp
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // No DbSet needed for DynoMapper — it reads dynamically
    // You can still add DbSets for normal EF Core operations
}
```

---

## 💉 Inject

**ISqlHelper path:**
```csharp
public class UserRepository(ISqlHelper sql) { }
```

**EF Core path:**
```csharp
public class UserRepository(AppDbContext context) { }
```

---

## 📖 Complete API Reference

### 1. Query List — multiple rows, no model class

```csharp
// ISqlHelper
var result = await _sql.QueryListAsync(
    "SELECT Id, Name, Email FROM Users WHERE IsActive = @IsActive",
    new { IsActive = true });

// EF Core
var result = await _context.DynoQueryAsync(
    "SELECT Id, Name, Email FROM Users WHERE IsActive = @IsActive",
    new { IsActive = true });

foreach (var row in result.List)
    Console.WriteLine($"{row.Id} — {row.Name} — {row.Email}");
```

---

### 2. Query Single — one row

```csharp
// ISqlHelper
var result = await _sql.QuerySingleAsync(
    "SELECT * FROM Users WHERE Id = @Id", new { Id = 1 });

// EF Core
var result = await _context.DynoQueryAsync(
    "SELECT * FROM Users WHERE Id = @Id", new { Id = 1 });

var user = result.Single;
Console.WriteLine(user?.Name);
Console.WriteLine(user?.Email);
```

---

### 3. JOIN — all columns flattened into one dynamic object

This is the core power of DynoMapper — no matter how complex your JOIN,
every column from every table becomes a property on the dynamic object.
**No ViewModel class needed.**

```csharp
// ISqlHelper
var result = await _sql.QueryListAsync(@"
    SELECT
        u.Id        AS UserId,
        u.Name,
        u.Email,
        r.RoleName,
        b.BranchName,
        d.DepartmentName
    FROM Users u
    JOIN Roles       r ON u.RoleId       = r.Id
    JOIN Branches    b ON u.BranchId     = b.Id
    JOIN Departments d ON u.DepartmentId = d.Id
    WHERE u.IsActive = @IsActive",
    new { IsActive = true });

// EF Core — exact same query, exact same result
var result = await _context.DynoQueryAsync(@"
    SELECT
        u.Id        AS UserId,
        u.Name,
        u.Email,
        r.RoleName,
        b.BranchName,
        d.DepartmentName
    FROM Users u
    JOIN Roles       r ON u.RoleId       = r.Id
    JOIN Branches    b ON u.BranchId     = b.Id
    JOIN Departments d ON u.DepartmentId = d.Id
    WHERE u.IsActive = @IsActive",
    new { IsActive = true });

// Access any column from any joined table — no class needed
foreach (var row in result.List)
{
    Console.WriteLine(row.UserId);         // from Users
    Console.WriteLine(row.Name);           // from Users
    Console.WriteLine(row.RoleName);       // from Roles
    Console.WriteLine(row.BranchName);     // from Branches
    Console.WriteLine(row.DepartmentName); // from Departments
}

return Ok(result.List); // serialize directly — no DTO
```

> ⚠️ Always alias duplicate column names — both tables may have `Id`:
> ```sql
> SELECT u.Id AS UserId, r.Id AS RoleId ...  -- ✅
> SELECT u.Id, r.Id ...                       -- ❌ second Id overwrites first
> ```

---

### 4. Scalar — COUNT, MAX, SUM

```csharp
// ISqlHelper
var result = await _sql.QueryScalarAsync(
    "SELECT COUNT(*) FROM Users WHERE IsActive = 1");
int count = result.Scalar<int>();

// EF Core — use DynoQueryAsync + access first row
var result = await _context.DynoQueryAsync(
    "SELECT COUNT(*) AS Total FROM Users WHERE IsActive = 1");
int count = (int)result.Single!.Total;
```

---

### 5. INSERT / UPDATE / DELETE

```csharp
// ISqlHelper
var result = await _sql.ExecuteAsync(
    "INSERT INTO Users (Name, Email) VALUES (@Name, @Email)",
    new { Name = "Ali", Email = "ali@example.com" });
Console.WriteLine(result.AffectedRows);

// EF Core
var result = await _context.DynoExecuteAsync(
    "INSERT INTO Users (Name, Email) VALUES (@Name, @Email)",
    new { Name = "Ali", Email = "ali@example.com" });
Console.WriteLine(result.AffectedRows);
```

---

### 6. Stored Procedures

```csharp
// ISqlHelper
var result = await _sql.QueryListSpAsync("sp_GetActiveUsers", new { BranchId = 5 });
var single = await _sql.QuerySingleSpAsync("sp_GetUserById", new { Id = 1 });
await _sql.ExecuteSpAsync("sp_DeactivateUser", new { UserId = 7 });

// EF Core
var result = await _context.DynoQuerySpAsync("sp_GetActiveUsers", new { BranchId = 5 });
var single = await _context.DynoQuerySpAsync("sp_GetUserById", new { Id = 1 });
await _context.DynoExecuteAsync("sp_DeactivateUser", new { UserId = 7 });
```

---

### 7. OUTPUT Clause — get inserted/updated rows back

```csharp
// ISqlHelper only
var result = await _sql.ExecuteWithOutputAsync(
    "INSERT INTO Users (Name, Email) OUTPUT INSERTED.Id, INSERTED.CreatedAt VALUES (@Name, @Email)",
    new { Name = "Ali", Email = "ali@example.com" });

var newUser = result.Single;
Console.WriteLine($"New Id: {newUser?.Id}, Created: {newUser?.CreatedAt}");
```

---

### 8. Bulk Insert

```csharp
// ISqlHelper only
var users = new[]
{
    new { Name = "Ali",   Email = "ali@x.com",   IsActive = true },
    new { Name = "Sara",  Email = "sara@x.com",  IsActive = true },
    new { Name = "Ahmed", Email = "ahmed@x.com", IsActive = false },
};

var result = await _sql.BulkInsertAsync("Users", users);
Console.WriteLine($"Inserted: {result.AffectedRows} rows");
```

---

### 9. Pagination

```csharp
// ISqlHelper only
var page = await _sql.QueryPagedAsync(
    "SELECT * FROM Users WHERE IsActive = 1 ORDER BY Id",
    pageNumber: 2,
    pageSize:   20);

foreach (var row in page.Data.List)
    Console.WriteLine(row.Name);

Console.WriteLine($"Page {page.CurrentPage} of {page.TotalPages}");
Console.WriteLine($"Total: {page.TotalCount}");
Console.WriteLine($"Has next: {page.HasNextPage}");
```

---

### 10. Multiple Result Sets

```csharp
// ISqlHelper
var sets = await _sql.QueryMultipleAsync(
    "sp_GetUserDashboard", new { UserId = 1 },
    CommandType.StoredProcedure);

// EF Core — same via DynoQuerySpAsync per result set
// or use ISqlHelper for multi-result sets

var userInfo = sets[0].Single;  // first SELECT
var roles    = sets[1].List;    // second SELECT
var branches = sets[2].List;    // third SELECT
```

---

### 11. Transactions

```csharp
// ISqlHelper only
await using var tx = await _sql.BeginTransactionAsync();
try
{
    await tx.ExecuteAsync(
        "INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total)",
        new { CustomerId = 1, Total = 500m });

    await tx.ExecuteSpAsync("sp_NotifyWarehouse", new { OrderId = 1 });

    await tx.CommitAsync(); // auto-rollback if never called
}
catch { throw; }

// EF Core — use EF Core's own transaction, DynoMapper enlists automatically
await using var tx = await _context.Database.BeginTransactionAsync();
try
{
    await _context.DynoExecuteAsync(
        "INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total)",
        new { CustomerId = 1, Total = 500m });

    await _context.DynoExecuteAsync(
        "INSERT INTO OrderItems (OrderId, ProductId) VALUES (@OrderId, @ProductId)",
        new { OrderId = 1, ProductId = 5 });

    await tx.CommitAsync();
}
catch { throw; }
```

---

### 12. Savepoints (ISqlHelper only)

```csharp
await using var tx = await _sql.BeginTransactionAsync();

await tx.ExecuteAsync("INSERT INTO AuditLog (Event) VALUES (@Event)", new { Event = "Start" });
await tx.SavepointAsync("after_audit");

try
{
    await tx.ExecuteAsync("INSERT INTO Orders ...", new { ... });
}
catch
{
    await tx.RollbackToSavepointAsync("after_audit"); // undo orders, keep audit log
}

await tx.CommitAsync();
```

---

### 13. Retry (ISqlHelper only)

Automatic — no extra code needed. Configured in appsettings or inline.

```json
"Retry": {
  "MaxAttempts": 3,
  "DelayMs": 200,
  "UseJitter": true,
  "Enabled": true
}
```

Retries on: deadlocks, timeouts, connection drops, Azure throttling.
Never retries on: syntax errors, constraint violations, auth failures.

---

### 14. Runtime Field Selection — Pick

```csharp
var result = await _sql.QueryListAsync("SELECT * FROM Users");
// or
var result = await _context.DynoQueryAsync("SELECT * FROM Users");

// Keep only these columns — drop everything else
var slim = result.Pick("Id", "Name", "Email");
return Ok(slim.List);

// From API query param: GET /users?fields=Id&fields=Name
if (fields is { Length: > 0 })
    result = result.Pick(fields);
return Ok(result.List);
```

---

### 15. Computed / Derived Fields

```csharp
var result = await _sql.QueryListAsync(
    "SELECT FirstName, LastName, DateOfBirth, Salary, Tax FROM Employees");
// or EF Core:
var result = await _context.DynoQueryAsync(
    "SELECT FirstName, LastName, DateOfBirth, Salary, Tax FROM Employees");

result
    .Compute("FullName",  row => $"{row.FirstName} {row.LastName}")
    .Compute("Age",       row => DateTime.Today.Year - ((DateTime)row.DateOfBirth).Year)
    .Compute("NetSalary", row => (decimal)row.Salary - (decimal)row.Tax);

return Ok(result.List);
```

---

### 16. Runtime Filter, Sort, Group

```csharp
// Filter
var active = result.Where("Status", "Active");

// Sort
var sorted = result.OrderBy("CreatedAt", descending: true);

// Group
var grouped = result.GroupBy("DepartmentId");
foreach (var (deptId, rows) in grouped)
    Console.WriteLine($"Dept {deptId}: {rows.RowCount} employees");
```

---

### 17. Column Projection

```csharp
// Get all Ids as flat list
var ids = result.Select("Id"); // → [1, 2, 3, ...]
```

---

### 18. Introspection & Dictionary Access

```csharp
// See all column names
Console.WriteLine(string.Join(", ", result.Columns));
// → Id, Name, Email, RoleName, BranchName

// Dictionary access
var dicts = result.ToDictionaryList();
foreach (var row in dicts)
    Console.WriteLine(row["Name"]);
```

---

### 19. Return Directly from Controller — zero DTO

```csharp
// ISqlHelper
[ApiController]
[Route("api/[controller]")]
public class UsersController(ISqlHelper sql) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await sql.QueryListAsync("SELECT * FROM Users WHERE IsActive = 1");
        return Ok(result.List); // dynamic JSON — no DTO
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var result = await sql.QuerySingleAsync(
            "SELECT * FROM Users WHERE Id = @Id", new { Id = id });
        return Ok(result.Single);
    }
}

// EF Core
[ApiController]
[Route("api/[controller]")]
public class UsersController(AppDbContext context) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await context.DynoQueryAsync("SELECT * FROM Users WHERE IsActive = 1");
        return Ok(result.List); // dynamic JSON — no DTO
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var result = await context.DynoQueryAsync(
            "SELECT * FROM Users WHERE Id = @Id", new { Id = id });
        return Ok(result.Single);
    }
}
```

---

## 📁 Package Structure

```
DynoMapper/
├── Core/
│   ├── DynoOptions.cs        ← Config: ConnectionString, Provider, Timeout, Retry
│   ├── DynoResult.cs         ← Universal result: .List .Single .Scalar .Pick .Compute .OrderBy .GroupBy
│   ├── PagedDynoResult.cs    ← Pagination metadata: TotalCount, TotalPages, HasNextPage
│   └── RetryPolicy.cs        ← Transient detection + exponential backoff + jitter
├── Abstractions/
│   └── IDbConnectionFactory  ← SqlServer / PostgreSQL / MySQL connection switching
├── SqlLayer/
│   ├── SqlHelper.cs          ← ISqlHelper: all query, execute, bulk, paged, multi, transaction methods
│   └── DynoTransaction.cs    ← Full transaction: raw SQL + SP + OUTPUT + savepoints + auto-rollback
├── Mapper/
│   └── DynoReader.cs         ← Core engine: DbDataReader → ExpandoObject (dynamic)
└── Extensions/
    ├── ServiceCollectionExtensions.cs  ← AddDynoMapper() for Program.cs
    └── EFCoreDynoExtensions.cs         ← DynoQueryAsync() / DynoExecuteAsync() on DbContext
```

---

## ✅ Full Feature Matrix

| Feature | ISqlHelper | EF Core |
|---|---|---|
| Query list (raw SQL) | ✅ | ✅ |
| Query single (raw SQL) | ✅ | ✅ |
| INSERT / UPDATE / DELETE | ✅ | ✅ |
| Stored Procedures | ✅ | ✅ |
| JOINs — dynamic flattened result | ✅ | ✅ |
| No DTO / model class needed | ✅ | ✅ |
| Runtime field selection (.Pick) | ✅ | ✅ |
| Computed fields (.Compute) | ✅ | ✅ |
| Runtime filter / sort / group | ✅ | ✅ |
| Multiple Result Sets | ✅ | ➖ use ISqlHelper |
| OUTPUT clause capture | ✅ | ➖ use ISqlHelper |
| Bulk Insert | ✅ | ➖ use ISqlHelper |
| Pagination (OFFSET/FETCH) | ✅ | ➖ use ISqlHelper |
| Transactions + Savepoints | ✅ | ✅ (EF Core tx) |
| Retry on transient failures | ✅ | ➖ use ISqlHelper |
| SQL Server | ✅ | ✅ |
| PostgreSQL | ✅ | ✅ |
| MySQL | ✅ | ✅ |
| NuGet-ready, .NET 8 | ✅ | ✅ |

---

## 🏗 Built for enterprise .NET — write your own SQL, get dynamic results, zero DTO classes.