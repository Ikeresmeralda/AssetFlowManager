namespace AssetFlow.Api.Tests;

/// <summary>
/// API con un administrador y un usuario normal ya creados.
/// </summary>
/// <remarks>
/// El alta va aqui y no en un IAsyncLifetime de la clase de test porque xUnit
/// construye una instancia nueva de la clase por cada [Fact]: el usuario se
/// crearia una vez por test, chocando con el nombre ya existente y gastando un
/// acceso del presupuesto del limitador en cada uno. El fixture, en cambio, se
/// construye una sola vez para toda la clase.
/// </remarks>
public class EntornoConUsuarioNormal : ApiFactory
{
    public HttpClient Admin { get; private set; } = null!;

    public HttpClient Normal { get; private set; } = null!;

    public int IdNormal { get; private set; }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        Admin = await ClienteAdminAsync();
        (IdNormal, Normal) = await CrearCuentaAsync("usuario.normal");
    }
}
