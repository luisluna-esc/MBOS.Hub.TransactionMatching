using System.Net;
using Microsoft.EntityFrameworkCore;
using TransactionMatching.Application.Dtos.Reconciliation;
using TransactionMatching.Application.Dtos.Reports;
using TransactionMatching.Application.Interfaces.Reconciliation;
using TransactionMatching.Application.Interfaces.Security;
using TransactionMatching.Application.Matching;
using TransactionMatching.Core.Configuration;
using TransactionMatching.Core.Entities.Reconciliation;
using TransactionMatching.Core.Entities.ResponseApi.DisplayFormat;
using TransactionMatching.Core.Entities.Security;
using TransactionMatching.Core.Interfaces;
using TransactionMatching.Core.QueryFilters.Pagination;
using TransactionMatching.Core.QueryFilters.Reconciliation;
using ReconciliationEntity = TransactionMatching.Core.Entities.Reconciliation.Reconciliation;

namespace TransactionMatching.Application.Services;

public class ReconciliationService : IReconciliationService
{
    private const int MaxFileBytes = 10 * 1024 * 1024;

    private readonly IUnitOfWork _unitOfWork;
    private readonly PaginationOptions _paginationOptions;
    private readonly ICurrentUserService _currentUserService;

    public ReconciliationService(IUnitOfWork unitOfWork, PaginationOptions paginationOptions, ICurrentUserService currentUserService)
    {
        _unitOfWork = unitOfWork;
        _paginationOptions = paginationOptions;
        _currentUserService = currentUserService;
    }

    public async Task<ResponseGetObject> GetAll(PaginationQueryFilter paginationQueryFilter, ReconciliationQueryFilter filter)
    {
        var pageSize = paginationQueryFilter.PageSize > 0 ? paginationQueryFilter.PageSize : _paginationOptions.InitialPageSize;
        var pageNumber = paginationQueryFilter.PageNumber > 0 ? paginationQueryFilter.PageNumber : _paginationOptions.InitialPageNumber;

        var query = _unitOfWork.Repository<ReconciliationEntity>().Query().Include(r => r.BankAccount).AsQueryable();
        if (filter.BankAccountId.HasValue)
        {
            query = query.Where(r => r.BankAccountId == filter.BankAccountId.Value);
        }
        if (filter.Year.HasValue)
        {
            query = query.Where(r => r.PeriodStart.Year == filter.Year.Value);
        }
        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            query = query.Where(r => r.Status == filter.Status);
        }
        if (!string.IsNullOrWhiteSpace(filter.Scope))
        {
            query = query.Where(r => r.Scope == filter.Scope);
        }
        if (!string.IsNullOrWhiteSpace(paginationQueryFilter.SearchCriteria))
        {
            var search = paginationQueryFilter.SearchCriteria;
            query = query.Where(r => r.BankAccount!.Name.Contains(search) || r.BankAccount.AccountNumber.Contains(search)
                || (r.BankAccount.Department != null && r.BankAccount.Department.Contains(search)));
        }

        var totalRecords = await query.CountAsync();
        var page = await query
            .OrderByDescending(r => r.PeriodStart).ThenBy(r => r.BankAccount!.Name)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        var ids = page.Select(r => r.ReconciliationId).ToList();
        var counts = await CountsByReconciliation(ids);
        var users = await UserNames(page.Select(r => r.CreatedById));

        var items = page.Select(r =>
        {
            var c = counts.GetValueOrDefault(r.ReconciliationId);
            return new ReconciliationListItemDto
            {
                ReconciliationId = r.ReconciliationId,
                BankAccountId = r.BankAccountId,
                BankAccountName = r.BankAccount!.Name,
                Department = r.BankAccount.Department,
                AccountNumber = r.BankAccount.AccountNumber,
                PeriodStart = r.PeriodStart,
                PeriodEnd = r.PeriodEnd,
                Status = r.Status,
                Scope = r.Scope,
                MatchedCount = c.Matched,
                ToReviewCount = c.ToReview,
                MissingInLedgerCount = c.Bank - c.Matched - c.ToReview,
                MissingInBankCount = c.Ledger - c.Matched - c.ToReview,
                CreatedByName = r.CreatedById is null ? null : users.GetValueOrDefault(r.CreatedById.Value),
                CreatedAt = r.CreatedAt,
            };
        });

        return Responses.Ok(new PagedResult<ReconciliationListItemDto>
        {
            Items = items.ToList(),
            TotalRecords = totalRecords,
            PageNumber = pageNumber,
            PageSize = pageSize,
        });
    }

    public async Task<ResponseGetObject> Get(long id)
    {
        var data = await Load(id);
        if (data is null)
        {
            return Responses.GetError(HttpStatusCode.NotFound, "No se encontró la conciliación.");
        }

        var (reconciliation, bank, ledger, matches) = data.Value;
        var lines = ReconciliationLines.Build(bank, ledger, matches);
        var income = ReconciliationLines.Summary(lines, ReconciliationSide.Income);
        var expense = ReconciliationLines.Summary(lines, ReconciliationSide.Expense);
        // El cuadre de saldos solo tiene sentido con las dos partes: con solo ingresos o solo egresos
        // los saldos del banco y del Mayor incluyen la otra parte, que no se cargó.
        var reconciled = reconciliation.Scope == ReconciliationScope.All
            ? ReconciliationLines.ReconciledBalance(reconciliation.LedgerClosingBalance, income, expense)
            : null;
        var users = await UserNames([reconciliation.CreatedById]);

        return Responses.Ok(new ReconciliationDetailDto
        {
            ReconciliationId = reconciliation.ReconciliationId,
            BankAccount = ToDto(reconciliation.BankAccount!),
            PeriodStart = reconciliation.PeriodStart,
            PeriodEnd = reconciliation.PeriodEnd,
            Status = reconciliation.Status,
            Scope = reconciliation.Scope,
            BankFileName = reconciliation.BankFileName,
            LedgerFileName = reconciliation.LedgerFileName,
            BankOpeningBalance = reconciliation.BankOpeningBalance,
            BankClosingBalance = reconciliation.BankClosingBalance,
            LedgerOpeningBalance = reconciliation.LedgerOpeningBalance,
            LedgerClosingBalance = reconciliation.LedgerClosingBalance,
            ReconciledBalance = reconciled,
            UnexplainedDifference = reconciled is null || reconciliation.BankClosingBalance is null ? null : reconciliation.BankClosingBalance - reconciled,
            Notes = reconciliation.Notes,
            CreatedByName = reconciliation.CreatedById is null ? null : users.GetValueOrDefault(reconciliation.CreatedById.Value),
            CreatedAt = reconciliation.CreatedAt,
            ClosedAt = reconciliation.ClosedAt,
            Income = income,
            Expense = expense,
            Lines = lines.Select(ToDto).ToList(),
        });
    }

    public async Task<ResponsePost> Create(byte[] bankContent, string bankFileName, byte[] ledgerContent, string ledgerFileName, string? scope)
    {
        scope = string.IsNullOrWhiteSpace(scope) ? ReconciliationScope.All : scope;
        if (!ReconciliationScope.Values.Contains(scope))
        {
            return Responses.PostError(HttpStatusCode.BadRequest, "Elige qué conciliar: ingresos, egresos o ambos.");
        }
        if (bankContent.Length == 0 || ledgerContent.Length == 0)
        {
            return Responses.PostError(HttpStatusCode.BadRequest, "Sube los dos archivos: el extracto del banco y el Mayor de ASSINET.");
        }
        if (bankContent.Length > MaxFileBytes || ledgerContent.Length > MaxFileBytes)
        {
            return Responses.PostError(HttpStatusCode.BadRequest, "Cada archivo puede pesar hasta 10 MB.");
        }

        BankStatement statement;
        Ledger ledger;
        try
        {
            statement = BankStatementReader.Read(bankContent);
        }
        catch (Exception ex) when (ex is MatchingFileException or ExcelDataReader.Exceptions.ExcelReaderException or InvalidDataException
                                       or NotSupportedException or UglyToad.PdfPig.Core.PdfDocumentFormatException)
        {
            return Responses.PostError(HttpStatusCode.BadRequest, $"Extracto del banco: {Message(ex)}");
        }
        try
        {
            ledger = LedgerReader.Read(ledgerContent);
        }
        catch (Exception ex) when (ex is MatchingFileException or ExcelDataReader.Exceptions.ExcelReaderException or InvalidDataException or NotSupportedException)
        {
            return Responses.PostError(HttpStatusCode.BadRequest, $"Mayor de ASSINET: {Message(ex)}");
        }

        // Cada banco escribe el número a su manera: se comparan con AccountNumbers.Same (son pocas cuentas).
        var accounts = await _unitOfWork.Repository<BankAccount>().Query().ToListAsync();
        var account = accounts.FirstOrDefault(a => AccountNumbers.Same(a.AccountNumber, statement.AccountNumber));
        if (ledger.AccountNumber is not null && !AccountNumbers.Same(ledger.AccountNumber, statement.AccountNumber))
        {
            var ledgerAccount = accounts.FirstOrDefault(a => AccountNumbers.Same(a.AccountNumber, ledger.AccountNumber));
            return Responses.PostError(HttpStatusCode.BadRequest,
                $"El extracto es de {Describe(account, statement.AccountNumber)} y el Mayor de {Describe(ledgerAccount, ledger.AccountNumber)}. Tienen que ser de la misma cuenta.");
        }
        if (account is not null)
        {
            // Un mes puede tener una de ingresos y otra de egresos, o una de las dos juntas (no ambas cosas).
            var existing = await _unitOfWork.Repository<ReconciliationEntity>().Query()
                .Where(r => r.BankAccountId == account.BankAccountId && r.PeriodStart == statement.PeriodStart)
                .Select(r => r.Scope).ToListAsync();
            var clash = existing.FirstOrDefault(s => s == scope || s == ReconciliationScope.All || scope == ReconciliationScope.All);
            if (clash is not null)
            {
                return Responses.PostError(HttpStatusCode.Conflict,
                    $"Ya existe la conciliación de {ScopeText(clash)} de {account.Department ?? account.Name} ({account.Name}) del {statement.PeriodStart:dd/MM/yyyy} al {statement.PeriodEnd:dd/MM/yyyy}. Elimínala para volver a subirla.");
            }
        }

        // Solo la parte elegida. El banco y la contabilidad lo ven al revés: un ingreso es Crédito en el
        // banco y Débito en ASSINET; un egreso, Débito en el banco y Crédito en ASSINET.
        var movements = statement.Movements.Where(m => scope == ReconciliationScope.All || (scope == ReconciliationScope.Income ? m.Credit > 0 : m.Debit > 0)).ToList();
        var entries = ledger.Entries.Where(e => scope == ReconciliationScope.All || (scope == ReconciliationScope.Income ? e.Debit > 0 : e.Credit > 0)).ToList();
        if (movements.Count == 0 && entries.Count == 0)
        {
            return Responses.PostError(HttpStatusCode.BadRequest, $"Ni el extracto ni el Mayor tienen {ScopeText(scope)} en ese período.");
        }

        var userId = _currentUserService.AppUserId;
        var reconciliation = new ReconciliationEntity
        {
            PeriodStart = statement.PeriodStart,
            PeriodEnd = statement.PeriodEnd,
            Status = ReconciliationStatus.Open,
            Scope = scope,
            BankFileName = Truncate(bankFileName, 255),
            LedgerFileName = Truncate(ledgerFileName, 255),
            BankOpeningBalance = statement.OpeningBalance,
            BankClosingBalance = statement.ClosingBalance,
            LedgerOpeningBalance = ledger.OpeningBalance,
            LedgerClosingBalance = ledger.ClosingBalance,
            CreatedById = userId,
            CreatedAt = DateTime.UtcNow,
        };

        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (account is null)
            {
                account = new BankAccount
                {
                    Bank = Truncate(statement.Bank, 100),
                    AccountNumber = AccountNumbers.Digits(statement.AccountNumber),
                    Name = DefaultName(statement.Bank, statement.AccountNumber),
                    LedgerSubaccount = ledger.Subaccount,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                };
                await _unitOfWork.Repository<BankAccount>().AddAsync(account);
                await _unitOfWork.SaveChangesAsync();
            }
            else if (account.LedgerSubaccount is null && ledger.Subaccount is not null)
            {
                account.LedgerSubaccount = ledger.Subaccount;
                _unitOfWork.Repository<BankAccount>().Update(account);
            }

            reconciliation.BankAccountId = account.BankAccountId;
            await _unitOfWork.Repository<ReconciliationEntity>().AddAsync(reconciliation);
            await _unitOfWork.SaveChangesAsync();

            foreach (var movement in movements)
            {
                movement.ReconciliationId = reconciliation.ReconciliationId;
                await _unitOfWork.Repository<BankMovement>().AddAsync(movement);
            }
            foreach (var entry in entries)
            {
                entry.ReconciliationId = reconciliation.ReconciliationId;
                await _unitOfWork.Repository<LedgerEntry>().AddAsync(entry);
            }
            await _unitOfWork.SaveChangesAsync();

            await AddProposals(reconciliation.ReconciliationId, movements, entries);
            await _unitOfWork.SaveChangesAsync();
        });

        return Responses.Saved(reconciliation.ReconciliationId, "Conciliación creada y cotejada.", HttpStatusCode.Created);
    }

    public async Task<ResponsePost> Delete(long id)
    {
        var reconciliation = await _unitOfWork.Repository<ReconciliationEntity>().GetByIdAsync(id);
        if (reconciliation is null)
        {
            return NotFound();
        }
        if (reconciliation.Status == ReconciliationStatus.Closed)
        {
            return Responses.PostError(HttpStatusCode.BadRequest, "La conciliación está cerrada: ábrela antes de eliminarla.");
        }
        _unitOfWork.Repository<ReconciliationEntity>().Remove(reconciliation);
        await _unitOfWork.SaveChangesAsync();
        return Responses.Saved(id, "Conciliación eliminada.");
    }

    public async Task<ResponsePost> Rematch(long id)
    {
        var check = await OpenReconciliation(id);
        if (check.Error is not null)
        {
            return check.Error;
        }

        var matchRepository = _unitOfWork.Repository<ReconciliationMatch>();
        var matches = await matchRepository.Query().Where(m => m.ReconciliationId == id).ToListAsync();
        var kept = matches.Where(m => MatchStatus.Kept.Contains(m.Status)).ToList();

        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            foreach (var match in matches.Except(kept))
            {
                matchRepository.Remove(match);
            }
            await _unitOfWork.SaveChangesAsync();

            var keptBank = kept.Select(m => m.BankMovementId).ToHashSet();
            var keptLedger = kept.Select(m => m.LedgerEntryId).ToHashSet();
            var bank = await _unitOfWork.Repository<BankMovement>().Query()
                .Where(b => b.ReconciliationId == id && !keptBank.Contains(b.BankMovementId)).OrderBy(b => b.LineNumber).ToListAsync();
            var ledger = await _unitOfWork.Repository<LedgerEntry>().Query()
                .Where(l => l.ReconciliationId == id && !keptLedger.Contains(l.LedgerEntryId)).OrderBy(l => l.LineNumber).ToListAsync();

            // La descripción se vuelve a leer: así una conciliación cargada antes de una mejora del
            // lector (ej. fechas "01/10/26") se beneficia sin borrarla ni volver a subir los archivos.
            foreach (var entry in ledger)
            {
                var (type, date, name) = (entry.MovementType, entry.MovementDate, entry.CounterpartyName);
                LedgerReader.ParseDescription(entry);
                if (entry.MovementType != type || entry.MovementDate != date || entry.CounterpartyName != name)
                {
                    _unitOfWork.Repository<LedgerEntry>().Update(entry);
                }
            }
            await _unitOfWork.SaveChangesAsync();

            await AddProposals(id, bank, ledger);
            await _unitOfWork.SaveChangesAsync();
        });

        return Responses.Saved(id, "Cotejo actualizado.");
    }

    public async Task<ResponsePost> MatchManually(long id, ManualMatchDto request)
    {
        var check = await OpenReconciliation(id);
        if (check.Error is not null)
        {
            return check.Error;
        }

        var movement = await _unitOfWork.Repository<BankMovement>().Query()
            .FirstOrDefaultAsync(b => b.BankMovementId == request.BankMovementId && b.ReconciliationId == id);
        var entry = await _unitOfWork.Repository<LedgerEntry>().Query()
            .FirstOrDefaultAsync(l => l.LedgerEntryId == request.LedgerEntryId && l.ReconciliationId == id);
        if (movement is null || entry is null)
        {
            return Responses.PostError(HttpStatusCode.NotFound, "No se encontró el movimiento del banco o el registro de ASSINET en esta conciliación.");
        }
        if ((movement.Credit > 0) != (entry.Debit > 0))
        {
            return Responses.PostError(HttpStatusCode.BadRequest, "Un ingreso del banco solo se coteja con un ingreso de ASSINET (Débito), y un egreso con un egreso (Crédito).");
        }

        var matchRepository = _unitOfWork.Repository<ReconciliationMatch>();
        if (await matchRepository.Query().AnyAsync(m => m.BankMovementId == movement.BankMovementId || m.LedgerEntryId == entry.LedgerEntryId))
        {
            return Responses.PostError(HttpStatusCode.BadRequest, "Alguno de los dos ya está cotejado: deshaz ese cotejo primero.");
        }

        var match = new ReconciliationMatch
        {
            ReconciliationId = id,
            BankMovementId = movement.BankMovementId,
            LedgerEntryId = entry.LedgerEntryId,
            Score = MatchEngine.Score(MatchEngine.DayGap(movement, entry),
                NameMatcher.Similarity(NameMatcher.BankTokens(movement), NameMatcher.Tokens(entry.CounterpartyName))),
            Status = MatchStatus.Manual,
            Note = Clean(request.Note, 500),
            CreatedById = _currentUserService.AppUserId,
            CreatedAt = DateTime.UtcNow,
        };
        await matchRepository.AddAsync(match);
        await _unitOfWork.SaveChangesAsync();
        return Responses.Saved(match.ReconciliationMatchId, "Cotejado.");
    }

    public async Task<ResponsePost> ConfirmMatch(long id, long matchId)
    {
        var check = await OpenReconciliation(id);
        if (check.Error is not null)
        {
            return check.Error;
        }
        var match = await _unitOfWork.Repository<ReconciliationMatch>().Query()
            .FirstOrDefaultAsync(m => m.ReconciliationMatchId == matchId && m.ReconciliationId == id);
        if (match is null)
        {
            return Responses.PostError(HttpStatusCode.NotFound, "No se encontró el cotejo.");
        }
        if (match.Status == MatchStatus.Suggested)
        {
            match.Status = MatchStatus.Confirmed;
            _unitOfWork.Repository<ReconciliationMatch>().Update(match);
            await _unitOfWork.SaveChangesAsync();
        }
        return Responses.Saved(matchId, "Cotejo confirmado.");
    }

    public async Task<ResponsePost> ConfirmAllSuggested(long id)
    {
        var check = await OpenReconciliation(id);
        if (check.Error is not null)
        {
            return check.Error;
        }
        var suggested = await _unitOfWork.Repository<ReconciliationMatch>().Query()
            .Where(m => m.ReconciliationId == id && m.Status == MatchStatus.Suggested).ToListAsync();
        foreach (var match in suggested)
        {
            match.Status = MatchStatus.Confirmed;
            _unitOfWork.Repository<ReconciliationMatch>().Update(match);
        }
        await _unitOfWork.SaveChangesAsync();
        return Responses.Saved(id, $"{suggested.Count} cotejos confirmados.");
    }

    public async Task<ResponsePost> Unmatch(long id, long matchId)
    {
        var check = await OpenReconciliation(id);
        if (check.Error is not null)
        {
            return check.Error;
        }
        var repository = _unitOfWork.Repository<ReconciliationMatch>();
        var match = await repository.Query().FirstOrDefaultAsync(m => m.ReconciliationMatchId == matchId && m.ReconciliationId == id);
        if (match is null)
        {
            return Responses.PostError(HttpStatusCode.NotFound, "No se encontró el cotejo.");
        }
        repository.Remove(match);
        await _unitOfWork.SaveChangesAsync();
        return Responses.Saved(matchId, "Cotejo deshecho.");
    }

    public async Task<ResponsePost> SetBankMovementNote(long id, long bankMovementId, NoteDto request)
    {
        var check = await OpenReconciliation(id);
        if (check.Error is not null)
        {
            return check.Error;
        }
        var movement = await _unitOfWork.Repository<BankMovement>().Query()
            .FirstOrDefaultAsync(b => b.BankMovementId == bankMovementId && b.ReconciliationId == id);
        if (movement is null)
        {
            return Responses.PostError(HttpStatusCode.NotFound, "No se encontró el movimiento del banco.");
        }
        movement.Note = Clean(request.Note, 500);
        _unitOfWork.Repository<BankMovement>().Update(movement);
        await _unitOfWork.SaveChangesAsync();
        return Responses.Saved(bankMovementId, "Observación guardada.");
    }

    public async Task<ResponsePost> SetLedgerEntryNote(long id, long ledgerEntryId, NoteDto request)
    {
        var check = await OpenReconciliation(id);
        if (check.Error is not null)
        {
            return check.Error;
        }
        var entry = await _unitOfWork.Repository<LedgerEntry>().Query()
            .FirstOrDefaultAsync(l => l.LedgerEntryId == ledgerEntryId && l.ReconciliationId == id);
        if (entry is null)
        {
            return Responses.PostError(HttpStatusCode.NotFound, "No se encontró el registro de ASSINET.");
        }
        entry.Note = Clean(request.Note, 500);
        _unitOfWork.Repository<LedgerEntry>().Update(entry);
        await _unitOfWork.SaveChangesAsync();
        return Responses.Saved(ledgerEntryId, "Observación guardada.");
    }

    public async Task<ResponsePost> SetNotes(long id, NoteDto request)
    {
        var check = await OpenReconciliation(id);
        if (check.Error is not null)
        {
            return check.Error;
        }
        check.Reconciliation!.Notes = Clean(request.Note, 1000);
        await _unitOfWork.SaveChangesAsync();
        return Responses.Saved(id, "Observaciones guardadas.");
    }

    public async Task<ResponsePost> SetClosed(long id, bool closed)
    {
        var reconciliation = await _unitOfWork.Repository<ReconciliationEntity>().GetByIdAsync(id);
        if (reconciliation is null)
        {
            return NotFound();
        }
        if (closed && await _unitOfWork.Repository<ReconciliationMatch>().Query()
                .AnyAsync(m => m.ReconciliationId == id && m.Status == MatchStatus.Suggested))
        {
            return Responses.PostError(HttpStatusCode.BadRequest, "Quedan cotejos por revisar: confírmalos o deshazlos antes de cerrar.");
        }
        reconciliation.Status = closed ? ReconciliationStatus.Closed : ReconciliationStatus.Open;
        reconciliation.ClosedAt = closed ? DateTime.UtcNow : null;
        reconciliation.ClosedById = closed ? _currentUserService.AppUserId : null;
        await _unitOfWork.SaveChangesAsync();
        return Responses.Saved(id, closed ? "Conciliación cerrada." : "Conciliación abierta de nuevo.");
    }

    public async Task<ReportResult> Export(long id, string? side, string? status)
    {
        var data = await Load(id);
        if (data is null)
        {
            return new ReportResult(null, Responses.GetError(HttpStatusCode.NotFound, "No se encontró la conciliación."));
        }
        var (reconciliation, bank, ledger, matches) = data.Value;
        var lines = ReconciliationLines.Build(bank, ledger, matches);
        var users = await UserNames([_currentUserService.AppUserId]);
        var generatedBy = _currentUserService.AppUserId is { } userId ? users.GetValueOrDefault(userId) : null;

        // "Exportar lo que veo": solo un lado y/o un estado (ej. los ingresos que faltan en ASSINET).
        var filtered = side is not null || status is not null;
        if (filtered)
        {
            lines = lines.Where(l => (side is null || l.Side == side) && LineFilter.Matches(l.Status, status)).ToList();
        }
        var content = ReconciliationExcel.Build(reconciliation, lines, generatedBy, filtered ? LineFilter.Title(side, status) : null);
        var account = reconciliation.BankAccount!;
        var suffix = filtered ? $" {LineFilter.FileSuffix(side, status)}" : "";
        var fileName = $"Conciliación {(account.Department is null ? "" : account.Department + " ")}{account.Name} {reconciliation.PeriodStart:yyyy-MM}{suffix}.xlsx";
        return new ReportResult(new ReportFile(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName), null);
    }

    public async Task<ReportResult> AnnualReport(int year, long? bankAccountId)
    {
        if (year < 2000 || year > 2100)
        {
            return new ReportResult(null, Responses.GetError(HttpStatusCode.BadRequest, "Año no válido."));
        }

        var reconciliations = await _unitOfWork.Repository<ReconciliationEntity>().Query().Include(r => r.BankAccount)
            .Where(r => r.PeriodStart.Year == year && (bankAccountId == null || r.BankAccountId == bankAccountId))
            .ToListAsync();
        var ids = reconciliations.Select(r => r.ReconciliationId).ToList();

        var matches = _unitOfWork.Repository<ReconciliationMatch>().Query();
        var unidentified = await _unitOfWork.Repository<BankMovement>().Query()
            .Where(b => ids.Contains(b.ReconciliationId) && b.Credit > 0 && !matches.Any(m => m.BankMovementId == b.BankMovementId))
            .ToListAsync();
        var notDeposited = await _unitOfWork.Repository<LedgerEntry>().Query()
            .Where(l => ids.Contains(l.ReconciliationId) && l.Debit > 0 && !matches.Any(m => m.LedgerEntryId == l.LedgerEntryId))
            .ToListAsync();

        var accounts = await _unitOfWork.Repository<BankAccount>().Query()
            .Where(a => bankAccountId == null ? a.IsActive : a.BankAccountId == bankAccountId)
            .OrderBy(a => a.Department).ThenBy(a => a.Bank).ThenBy(a => a.Name)
            .ToListAsync();

        var users = await UserNames([_currentUserService.AppUserId]);
        var generatedBy = _currentUserService.AppUserId is { } userId ? users.GetValueOrDefault(userId) : null;
        var content = ReconciliationExcel.AnnualReport(year, accounts, reconciliations, unidentified, notDeposited, generatedBy);
        var suffix = bankAccountId is null || accounts.Count == 0 ? "" : $" {accounts[0].Department ?? accounts[0].Name}";
        return new ReportResult(new ReportFile(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Reporte anual {year}{suffix}.xlsx"), null);
    }

    // ---------------------------------------------------------------------------------------

    private async Task AddProposals(long reconciliationId, IReadOnlyList<BankMovement> bank, IReadOnlyList<LedgerEntry> ledger)
    {
        foreach (var proposal in MatchEngine.Match(bank, ledger))
        {
            await _unitOfWork.Repository<ReconciliationMatch>().AddAsync(new ReconciliationMatch
            {
                ReconciliationId = reconciliationId,
                BankMovementId = bank[proposal.BankIndex].BankMovementId,
                LedgerEntryId = ledger[proposal.LedgerIndex].LedgerEntryId,
                Score = proposal.Score,
                Status = proposal.Status,
                CreatedAt = DateTime.UtcNow,
            });
        }
    }

    private async Task<(ReconciliationEntity Reconciliation, List<BankMovement> Bank, List<LedgerEntry> Ledger, List<ReconciliationMatch> Matches)?> Load(long id)
    {
        var reconciliation = await _unitOfWork.Repository<ReconciliationEntity>().Query()
            .Include(r => r.BankAccount).FirstOrDefaultAsync(r => r.ReconciliationId == id);
        if (reconciliation is null)
        {
            return null;
        }
        var bank = await _unitOfWork.Repository<BankMovement>().Query().Where(b => b.ReconciliationId == id).OrderBy(b => b.LineNumber).ToListAsync();
        var ledger = await _unitOfWork.Repository<LedgerEntry>().Query().Where(l => l.ReconciliationId == id).OrderBy(l => l.LineNumber).ToListAsync();
        var matches = await _unitOfWork.Repository<ReconciliationMatch>().Query().Where(m => m.ReconciliationId == id).ToListAsync();
        return (reconciliation, bank, ledger, matches);
    }

    /// <summary>La conciliación, o el error si no existe o ya está cerrada (cerrada no se toca).</summary>
    private async Task<(ReconciliationEntity? Reconciliation, ResponsePost? Error)> OpenReconciliation(long id)
    {
        var reconciliation = await _unitOfWork.Repository<ReconciliationEntity>().GetByIdAsync(id);
        if (reconciliation is null)
        {
            return (null, NotFound());
        }
        if (reconciliation.Status == ReconciliationStatus.Closed)
        {
            return (null, Responses.PostError(HttpStatusCode.BadRequest, "La conciliación está cerrada: ábrela para hacer cambios."));
        }
        return (reconciliation, null);
    }

    private async Task<Dictionary<long, (int Bank, int Ledger, int Matched, int ToReview)>> CountsByReconciliation(List<long> ids)
    {
        var bank = await _unitOfWork.Repository<BankMovement>().Query().Where(b => ids.Contains(b.ReconciliationId))
            .GroupBy(b => b.ReconciliationId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        var ledger = await _unitOfWork.Repository<LedgerEntry>().Query().Where(l => ids.Contains(l.ReconciliationId))
            .GroupBy(l => l.ReconciliationId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        var matches = await _unitOfWork.Repository<ReconciliationMatch>().Query().Where(m => ids.Contains(m.ReconciliationId))
            .GroupBy(m => m.ReconciliationId)
            .Select(g => new { g.Key, Matched = g.Count(m => m.Status != MatchStatus.Suggested), ToReview = g.Count(m => m.Status == MatchStatus.Suggested) })
            .ToDictionaryAsync(x => x.Key, x => (x.Matched, x.ToReview));
        return ids.ToDictionary(id => id, id =>
        {
            var (matched, toReview) = matches.GetValueOrDefault(id);
            return (bank.GetValueOrDefault(id), ledger.GetValueOrDefault(id), matched, toReview);
        });
    }

    private async Task<Dictionary<long, string>> UserNames(IEnumerable<long?> ids)
    {
        var list = ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        return await _unitOfWork.Repository<AppUser>().Query().Where(u => list.Contains(u.AppUserId))
            .ToDictionaryAsync(u => u.AppUserId, u => u.FullName);
    }

    private static ReconciliationLineDto ToDto(ReconLine line) => new()
    {
        Side = line.Side,
        Status = line.Status,
        MatchId = line.Match?.ReconciliationMatchId,
        Score = line.Match?.Score,
        MatchNote = line.Match?.Note,
        Difference = line.Bank is null || line.Ledger is null ? 0 : line.Difference,
        Bank = line.Bank is null ? null : new BankMovementDto
        {
            BankMovementId = line.Bank.BankMovementId,
            LineNumber = line.Bank.LineNumber,
            Date = line.Bank.Date,
            Time = line.Bank.Time,
            BankCode = line.Bank.BankCode,
            Name = FirstText(line.Bank.DepositorName, line.Bank.OriginatorAch, line.Bank.RecipientName, line.Bank.Glosa),
            Document = line.Bank.DepositorDocument,
            Description = line.Bank.Description,
            Glosa = line.Bank.Glosa,
            Amount = line.BankAmount,
            Note = line.Bank.Note,
        },
        Ledger = line.Ledger is null ? null : new LedgerEntryDto
        {
            LedgerEntryId = line.Ledger.LedgerEntryId,
            LineNumber = line.Ledger.LineNumber,
            AccountingDate = line.Ledger.AccountingDate,
            MovementDate = line.Ledger.MovementDate,
            Batch = line.Ledger.Batch,
            Description = line.Ledger.Description,
            Amount = line.LedgerAmount,
            Note = line.Ledger.Note,
        },
    };

    private static BankAccountDto ToDto(BankAccount account) => new()
    {
        BankAccountId = account.BankAccountId,
        Bank = account.Bank,
        AccountNumber = account.AccountNumber,
        Name = account.Name,
        Department = account.Department,
        LedgerSubaccount = account.LedgerSubaccount,
        IsActive = account.IsActive,
    };

    /// <summary>"BMSC 5642" para el Banco Mercantil Santa Cruz; si no, la primera palabra y los últimos 4 dígitos.</summary>
    private static string DefaultName(string bank, string accountNumber)
    {
        var key = ExcelRows.Key(bank);
        var shortName = key.Contains("mercantil") ? "BMSC" : key.Contains("union") ? "BUSA" : key.Contains("nacional") ? "BNB"
            : bank.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "Banco";
        return Truncate($"{shortName} {accountNumber[^Math.Min(4, accountNumber.Length)..]}", 100);
    }

    /// <summary>"Reid Shepard (BMSC 5412)" si la cuenta está en el catálogo; si no, "la cuenta 1001285412".</summary>
    private static string Describe(BankAccount? account, string number) =>
        account is null ? $"la cuenta {number}"
        : account.Department is null ? $"la cuenta {account.Name} ({number})"
        : $"{account.Department} ({account.Name}, cuenta {number})";

    private static string ScopeText(string scope) => scope switch
    {
        ReconciliationScope.Income => "ingresos",
        ReconciliationScope.Expense => "egresos",
        _ => "ingresos y egresos",
    };

    private static string? FirstText(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string? Clean(string? text, int max)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : Truncate(trimmed, max);
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    private static string Message(Exception ex) =>
        ex is MatchingFileException ? ex.Message : "no se pudo leer el archivo. ¿Es el Excel o el PDF tal como lo descarga el banco?";

    private static ResponsePost NotFound() => Responses.PostError(HttpStatusCode.NotFound, "No se encontró la conciliación.");
}
