namespace AssetFlow.Api.Dtos;

/// <summary>
/// Solicitud de recuperacion tal y como la ve un administrador.
/// </summary>
/// <remarks>
/// Lleva los datos que hacen falta para decidir —quien la pide y desde cuando
/// espera— y ni uno mas. En particular <b>no lleva el correo de la cuenta</b>:
/// el administrador ya puede consultarlo en la ficha del usuario si lo
/// necesita, y ponerlo aqui lo expondria en una pantalla que se deja abierta.
/// </remarks>
public record PasswordResetRequestDto
{
    public required int Id { get; init; }

    public required int UserId { get; init; }

    public required string Username { get; init; }

    /// <summary>Nombre y apellidos, para reconocer a la persona.</summary>
    public required string FullName { get; init; }

    public required DateTime RequestedAt { get; init; }

    /// <summary>«Pendiente», «Aprobada» o «Denegada».</summary>
    public required string Estado { get; init; }

    public DateTime? ResolvedAt { get; init; }

    public string? ResolvedByUsername { get; init; }

    public required bool EstaPendiente { get; init; }
}

/// <summary>
/// Resultado de aprobar una solicitud.
/// </summary>
/// <remarks>
/// Devuelve la contrasena provisional para que la interfaz pueda mostrarsela al
/// administrador y este se la comunique a la persona. No es un secreto que haya
/// que proteger: es deducible del nombre de usuario y solo sirve una vez, hasta
/// que la persona entra y elige la suya.
/// </remarks>
public record PasswordResetApprovalDto
{
    public required string Username { get; init; }

    public required string ContrasenaProvisional { get; init; }
}
