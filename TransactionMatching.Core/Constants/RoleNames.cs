namespace TransactionMatching.Core.Constants;

public static class RoleNames
{
    /// <summary>Superusuario: el único rol que administra usuarios, roles y menús.</summary>
    public const string Admin = "M-BOS";
    /// <summary>Contabilidad: importa el extracto y el Mayor, coteja y cierra conciliaciones.</summary>
    public const string Accountant = "Contabilidad";
    /// <summary>Solo consulta y descarga reportes.</summary>
    public const string Viewer = "Consulta";

    /// <summary>Roles que registran y modifican datos; el resto solo visualiza.</summary>
    public const string Writers = Admin + "," + Accountant;

    /// <summary>Roles que pueden consultar la auditoría.</summary>
    public const string AuditViewers = Admin + "," + Accountant;
}
