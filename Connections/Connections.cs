using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace BelegOCR.Connections;

public class Connections
{
    private readonly IConfiguration _cfg;

    public Connections(IConfiguration cfg)
    {
        _cfg = cfg;
    }
   
    public SqlConnection Conn => new(_cfg.GetConnectionString("Default"));
    
    public SqliteConnection ConnSqlLite => new(_cfg.GetConnectionString("SqlLite"));
}