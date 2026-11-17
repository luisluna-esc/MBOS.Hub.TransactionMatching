namespace TransactionMatching.Core.Entities.Reconciliation;

/// <summary>
/// Cuenta del banco que se concilia. Se crea sola la primera vez que se sube un extracto de esa
/// cuenta; aquí solo se le pone un nombre legible.
/// </summary>
public class BankAccount
{
    public long BankAccountId { get; set; }
    /// <summary>Banco del extracto (ej. "Banco Mercantil Santa Cruz").</summary>
    public string Bank { get; set; } = string.Empty;
    /// <summary>Número de cuenta tal como lo trae el extracto (ej. "1000055642").</summary>
    public string AccountNumber { get; set; } = string.Empty;
    /// <summary>Nombre para mostrar (ej. "BMSC 5642").</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Departamento (unidad o colegio) dueño de la cuenta: "Oficina Central MBOS", "Reid Shepard"...</summary>
    public string? Department { get; set; }
    /// <summary>Subcuenta de ASSINET, como la trae el Mayor (ej. "BMSC M/N 1000055642 Suc. 00").</summary>
    public string? LedgerSubaccount { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
