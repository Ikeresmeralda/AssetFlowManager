namespace AssetFlow.Api.Security;

/// <summary>
/// Roles del sistema. Solo hay dos, y anadir un tercero deberia ser una
/// decision consciente, no un valor suelto escrito en un atributo.
/// </summary>
public static class Roles
{
    public const string Admin = "Admin";

    public const string User = "User";

    public static bool IsValid(string? role) => role is Admin or User;
}
