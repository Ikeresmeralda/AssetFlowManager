using FluentAssertions;
using AssetFlow.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AssetFlow.Api.Tests;

/// <summary>
/// Comprobaciones sobre la propia bateria de pruebas.
/// </summary>
/// <remarks>
/// Un test que depende de como tenga configurada su maquina quien lo ejecuta no
/// comprueba el codigo: comprueba esa maquina. Lo que sigue vigila esa clase de
/// fuga.
/// </remarks>
public class ConfiguracionDePruebasTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;

    public ConfiguracionDePruebasTests(ApiFactory api) => _api = api;

    /// <summary>
    /// Los tests nunca deben enviar correo de verdad.
    /// </summary>
    /// <remarks>
    /// La bateria se ejecuta en el entorno de desarrollo, que carga los
    /// secretos de usuario de la API. Ahi es donde docs/configuration.md
    /// recomienda guardar las credenciales SMTP, asi que sin una anulacion
    /// explicita esta bateria acabaria enviando correo real desde el equipo de
    /// quien programa.
    ///
    /// Y no daria la cara: el fallo de envio se traga a proposito para que
    /// comparar respuestas no revele que cuentas existen, de modo que los tests
    /// seguirian en verde mientras mandan mensajes a direcciones ajenas.
    /// </remarks>
    [Fact]
    public void La_bateria_no_usa_un_servidor_de_correo_real()
    {
        using IServiceScope ambito = _api.Services.CreateScope();

        var emisor = ambito.ServiceProvider.GetRequiredService<IEmailSender>();

        emisor.Should().BeOfType<LoggingEmailSender>(
            "los tests deben anular Email:SmtpHost para no depender de los " +
            "secretos de usuario de quien los ejecute");
    }
}
