using DynoMapper.SqlLayer;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class UsersController(ISqlHelper sql) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await sql.QueryListAsync("SELECT * FROM [User] WHERE IsActive = 1");
        return Ok(result.List);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var result = await sql.QuerySingleAsync(
            "SELECT * FROM [User] WHERE Id = @Id", new { Id = id });
        return Ok(result.Single);
    }
}
