using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AssetFlow.Api.Data.Providers;

/// <summary>
/// Contexto especializado para SQLite.
/// </summary>
/// <remarks>
/// Existen dos contextos derivados, uno por proveedor, porque las migraciones
/// de EF Core son especificas del motor: los tipos de columna, la sintaxis de
/// las claves autoincrementales y el tratamiento de fechas no coinciden entre
/// SQLite y SQL Server. Aplicar la migracion de uno sobre el otro produce un
/// esquema mal formado.
///
/// Es la forma documentada por Microsoft de mantener dos juegos de migraciones
/// en un mismo proyecto. El modelo se define una sola vez en
/// <see cref="AssetFlowDbContext"/>; aqui solo cambia el destino. Cada clase
/// derivada debe conservar su propio <c>DbContextOptions&lt;T&gt;</c>: es por
/// ese tipo por el que EF decide que migraciones aplicar.
/// </remarks>
public class SqliteAssetFlowDbContext : AssetFlowDbContext
{
    public SqliteAssetFlowDbContext(DbContextOptions<SqliteAssetFlowDbContext> options)
        : base(options)
    {
    }
}

/// <summary>Contexto especializado para SQL Server.</summary>
public class SqlServerAssetFlowDbContext : AssetFlowDbContext
{
    public SqlServerAssetFlowDbContext(DbContextOptions<SqlServerAssetFlowDbContext> options)
        : base(options)
    {
    }
}

/// <summary>
/// Fabrica usada por 'dotnet ef' para crear el contexto SQLite en tiempo de
/// diseno, sin arrancar la aplicacion.
/// </summary>
public class SqliteContextFactory : IDesignTimeDbContextFactory<SqliteAssetFlowDbContext>
{
    public SqliteAssetFlowDbContext CreateDbContext(string[] args)
    {
        var constructor = new DbContextOptionsBuilder<SqliteAssetFlowDbContext>();
        constructor.UseSqlite("Data Source=assetflow.db");

        return new SqliteAssetFlowDbContext(constructor.Options);
    }
}

/// <summary>Equivalente para SQL Server.</summary>
public class SqlServerContextFactory : IDesignTimeDbContextFactory<SqlServerAssetFlowDbContext>
{
    public SqlServerAssetFlowDbContext CreateDbContext(string[] args)
    {
        var constructor = new DbContextOptionsBuilder<SqlServerAssetFlowDbContext>();

        // Solo se usa para generar migraciones: nunca se conecta de verdad,
        // asi que no hace falta una cadena real ni un secreto.
        constructor.UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Database=AssetFlow;Trusted_Connection=True");

        return new SqlServerAssetFlowDbContext(constructor.Options);
    }
}
