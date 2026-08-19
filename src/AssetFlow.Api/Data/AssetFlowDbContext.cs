using AssetFlow.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace AssetFlow.Api.Data;

/// <summary>
/// Contexto de datos de la aplicacion.
/// </summary>
/// <remarks>
/// A diferencia del contexto anterior, aqui NO hay <c>OnConfiguring</c> con
/// una cadena de conexion escrita en el codigo. El proveedor y la cadena se
/// inyectan desde la configuracion en <c>Program.cs</c>, que es el unico sitio
/// que sabe si estamos sobre SQLite o SQL Server.
///
/// Todas las marcas de tiempo son <c>DateTime</c> en UTC, no
/// <c>DateTimeOffset</c>. El proveedor de SQLite persiste DateTimeOffset pero
/// no sabe traducir comparaciones ni ordenaciones sobre el, asi que consultas
/// tan basicas como "tokens caducados" fallan en tiempo de ejecucion. Como la
/// aplicacion trabaja siempre en UTC, el desplazamiento horario no aporta
/// informacion y su ausencia no se echa en falta.
/// </remarks>
public class AssetFlowDbContext : DbContext
{
    public AssetFlowDbContext(DbContextOptions<AssetFlowDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Constructor para los contextos derivados por proveedor. Es la forma
    /// documentada de compartir el modelo entre varios contextos: cada uno
    /// conserva su propio tipo de opciones, que es lo que EF usa para
    /// localizar el juego de migraciones que le corresponde.
    /// </summary>
    protected AssetFlowDbContext(DbContextOptions options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Material> Materials => Set<Material>();

    public DbSet<Loan> Loans => Set<Loan>();

    public DbSet<LoanLine> LoanLines => Set<LoanLine>();

    public DbSet<PasswordResetRequest> PasswordResetRequests => Set<PasswordResetRequest>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);

            e.Property(u => u.Username).HasMaxLength(50).IsRequired();
            e.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
            e.Property(u => u.LastName).HasMaxLength(100).IsRequired();
            e.Property(u => u.Email).HasMaxLength(255).IsRequired();
            e.Property(u => u.PhoneNumber).HasMaxLength(30);
            e.Property(u => u.PasswordHash).HasMaxLength(100).IsRequired();
            e.Property(u => u.Role).HasMaxLength(20).IsRequired();

            // El nombre de usuario es la credencial de acceso: la unicidad se
            // garantiza en la base de datos, no comprobando antes de insertar.
            // Con una comprobacion previa, dos altas simultaneas del mismo
            // nombre pasan las dos.
            e.HasIndex(u => u.Username).IsUnique();
            e.HasIndex(u => u.Email).IsUnique();

            e.Ignore(u => u.FullName);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.HasKey(t => t.Id);

            e.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
            e.Property(t => t.ReplacedByTokenHash).HasMaxLength(64);

            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => t.UserId);

            // Si se elimina un usuario, sus sesiones desaparecen con el.
            e.HasOne(t => t.User)
             .WithMany(u => u.RefreshTokens)
             .HasForeignKey(t => t.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.Ignore(t => t.IsActive);
        });

        b.Entity<Material>(e =>
        {
            e.HasKey(m => m.Id);

            e.Property(m => m.Name).HasMaxLength(200).IsRequired();
            e.Property(m => m.Type).HasMaxLength(100).IsRequired();
            e.Property(m => m.Publisher).HasMaxLength(200);

            e.Property(m => m.Version).IsConcurrencyToken();

            e.HasIndex(m => m.Name);
        });

        b.Entity<Loan>(e =>
        {
            e.HasKey(l => l.Id);

            e.Property(l => l.Reason).HasMaxLength(255);
            e.Property(l => l.DecisionNote).HasMaxLength(255);
            e.Property(l => l.ReturnDecisionNote).HasMaxLength(255);
            e.Property(l => l.Status).HasConversion<int>();

            e.HasIndex(l => l.UserId);
            e.HasIndex(l => l.Status);

            // El panel de administracion pregunta constantemente "que hay
            // pendiente y de quien": el indice compuesto evita recorrer la
            // tabla entera en cada refresco.
            e.HasIndex(l => new { l.Status, l.UserId });

            // Restrict, no Cascade: borrar un usuario no debe hacer
            // desaparecer el historial de prestamos en silencio. Por eso los
            // usuarios se desactivan en lugar de borrarse.
            e.HasOne(l => l.User)
             .WithMany(u => u.Loans)
             .HasForeignKey(l => l.UserId)
             .OnDelete(DeleteBehavior.Restrict);

            // Quien decidio. Sin cascada y sin coleccion inversa: es un dato
            // de auditoria, no una relacion de negocio, y borrar a un
            // administrador no puede llevarse por delante el historial de sus
            // decisiones.
            e.HasOne(l => l.DecidedBy)
             .WithMany()
             .HasForeignKey(l => l.DecidedByUserId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(l => l.ReturnDecidedBy)
             .WithMany()
             .HasForeignKey(l => l.ReturnDecidedByUserId)
             .OnDelete(DeleteBehavior.Restrict);

            e.Ignore(l => l.IsOverdue);
            e.Ignore(l => l.EstaFuera);
            e.Ignore(l => l.EstaReservado);
        });

        b.Entity<PasswordResetRequest>(e =>
        {
            e.HasKey(s => s.Id);

            e.Property(s => s.ResolvedByUsername).HasMaxLength(50);

            // La bandeja del administrador consulta por estado y ordena por
            // fecha; es la unica lectura frecuente de esta tabla.
            e.HasIndex(s => s.Status);
            e.HasIndex(s => new { s.UserId, s.Status });

            // Si se elimina la cuenta, sus solicitudes se van con ella: una
            // solicitud pendiente de un usuario que ya no existe no se puede
            // aprobar y solo ensuciaria la bandeja.
            e.HasOne(s => s.User)
             .WithMany()
             .HasForeignKey(s => s.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.Ignore(s => s.EstaPendiente);
        });

        b.Entity<AuditEntry>(e =>
        {
            e.HasKey(a => a.Id);

            e.Property(a => a.Action).HasMaxLength(60).IsRequired();
            e.Property(a => a.ActorUsername).HasMaxLength(50).IsRequired();
            e.Property(a => a.EntityType).HasMaxLength(30);
            e.Property(a => a.Details).HasMaxLength(500);

            e.HasIndex(a => a.OccurredAt);
            e.HasIndex(a => new { a.EntityType, a.EntityId });
            e.HasIndex(a => a.ActorUserId);

            // Sin clave ajena hacia Users a proposito: el registro debe
            // sobrevivir a la eliminacion de la cuenta que lo genero, y por
            // eso el nombre del actor se guarda copiado en la propia fila.
        });

        b.Entity<LoanLine>(e =>
        {
            e.HasKey(l => l.Id);

            // Las lineas no existen sin su prestamo.
            e.HasOne(l => l.Loan)
             .WithMany(l => l.Lines)
             .HasForeignKey(l => l.LoanId)
             .OnDelete(DeleteBehavior.Cascade);

            // Restrict: un articulo con prestamos vivos no puede borrarse.
            // Es la barrera que impide dejar lineas apuntando a la nada.
            e.HasOne(l => l.Material)
             .WithMany(m => m.LoanLines)
             .HasForeignKey(l => l.MaterialId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(l => new { l.LoanId, l.MaterialId }).IsUnique();
        });
    }

    public override int SaveChanges()
    {
        TocarMateriales();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        TocarMateriales();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Actualiza la marca de tiempo y el testigo de concurrencia de los
    /// articulos modificados. Hacerlo aqui evita depender de que cada
    /// controlador se acuerde.
    /// </summary>
    private void TocarMateriales()
    {
        foreach (var entrada in ChangeTracker.Entries<Material>())
        {
            if (entrada.State != EntityState.Modified)
            {
                continue;
            }

            entrada.Entity.UpdatedAt = DateTime.UtcNow;
            entrada.Entity.Version = Guid.NewGuid();
        }
    }
}
