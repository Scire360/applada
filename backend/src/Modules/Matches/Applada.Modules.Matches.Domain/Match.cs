using Applada.BuildingBlocks.Domain;
using Applada.Modules.Matches.Domain.Events;

namespace Applada.Modules.Matches.Domain;

/// <summary>
/// Agregado da partida. Mantém os contadores (<see cref="ConfirmedCount"/>, <see cref="WaitlistCount"/>) e a fila
/// (<see cref="NextQueueSeq"/>) coerentes com as inscrições, que chegam carregadas pelo chamador sob o lock da partida.
/// <see cref="Version"/> muda só nas edições do organizador (edição e cancelamento), nunca por inscrição.
/// Todo horário vem do parâmetro <c>now</c>; o domínio não lê o relógio.
/// </summary>
public sealed class Match
{
    private const string PublicVisibility = "public";

    private readonly List<IDomainEvent> _domainEvents = [];

    // Materialização pelo EF Core.
    private Match()
    {
        Title = string.Empty;
        TimeZone = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid OrganizerId { get; private set; }

    public Guid? GroupId { get; private set; }

    public Guid? VenueId { get; private set; }

    public string Title { get; private set; }

    public string? LocationText { get; private set; }

    /// <summary>Início da partida, sempre em UTC.</summary>
    public DateTimeOffset StartsAt { get; private set; }

    /// <summary>Fuso IANA de exibição.</summary>
    public string TimeZone { get; private set; }

    public int DurationMinutes { get; private set; }

    public int Capacity { get; private set; }

    public MatchVisibility Visibility { get; private set; }

    public MatchStatus Status { get; private set; }

    public int ConfirmedCount { get; private set; }

    public int WaitlistCount { get; private set; }

    /// <summary>Próxima posição de fila a atribuir; começa em 1 e só cresce.</summary>
    public long NextQueueSeq { get; private set; } = 1;

    public int Version { get; private set; } = 1;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    public void ClearDomainEvents() => _domainEvents.Clear();

    public static Result<Match> Create(NewMatch data, Guid organizerId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);

        var errors = new ValidationErrors();
        var title = errors.Title(data.Title);
        var locationText = errors.LocationText(data.LocationText);
        errors.Capacity(data.Capacity);

        if (data.StartsAt <= now || data.StartsAt > now.AddDays(MatchLimits.MaxDaysAhead))
        {
            errors.Add("startsAt", $"O início deve estar no futuro, até {MatchLimits.MaxDaysAhead} dias a partir de agora.");
        }

        if (string.IsNullOrWhiteSpace(data.TimeZone) || !TimeZoneInfo.TryFindSystemTimeZoneById(data.TimeZone, out _))
        {
            errors.Add("timeZone", "Informe um fuso horário IANA válido (ex.: America/Sao_Paulo).");
        }

        if (data.DurationMinutes is < MatchLimits.DurationMin or > MatchLimits.DurationMax)
        {
            errors.Add("durationMinutes", $"A duração deve ficar entre {MatchLimits.DurationMin} e {MatchLimits.DurationMax} minutos.");
        }

        if (!string.Equals(data.Visibility, PublicVisibility, StringComparison.Ordinal))
        {
            errors.Add("visibility", "A visibilidade deve ser 'public'.");
        }

        if (errors.Any)
        {
            return errors.ToError();
        }

        var match = new Match
        {
            Id = Guid.CreateVersion7(now),
            OrganizerId = organizerId,
            GroupId = data.GroupId,
            VenueId = data.VenueId,
            Title = title!,
            LocationText = locationText,
            StartsAt = data.StartsAt.ToUniversalTime(),
            TimeZone = data.TimeZone,
            DurationMinutes = data.DurationMinutes,
            Capacity = data.Capacity,
            Visibility = MatchVisibility.Public,
            Status = MatchStatus.Scheduled,
            CreatedAt = now.ToUniversalTime(),
            UpdatedAt = now.ToUniversalTime(),
        };

        match.Raise(new MatchCreated(match.Id, match.OrganizerId, match.StartsAt, match.Capacity, match.Visibility));
        return match;
    }

    /// <summary>
    /// Edição do organizador. <paramref name="title"/> e <paramref name="capacity"/> nulos não alteram;
    /// <paramref name="locationText"/> nulo não altera e vazio limpa. <paramref name="waitlistHead"/> traz o início da
    /// lista de espera em ordem de <see cref="Registration.QueueSeq"/>, com pelo menos as inscrições que a nova
    /// capacidade vai promover.
    /// </summary>
    public Result ChangeDetails(
        Guid actorId,
        int expectedVersion,
        string? title,
        string? locationText,
        int? capacity,
        IReadOnlyList<Registration> waitlistHead,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(waitlistHead);

        if (actorId != OrganizerId)
        {
            return MatchErrors.NotOrganizer;
        }

        if (CheckOpen(now) is { } closed)
        {
            return closed;
        }

        if (expectedVersion != Version)
        {
            return MatchErrors.VersionMismatch;
        }

        var errors = new ValidationErrors();
        var newTitle = title is null ? Title : errors.Title(title);
        var newLocationText = locationText is null ? LocationText : errors.LocationText(locationText);
        if (capacity is { } requested)
        {
            errors.Capacity(requested);
        }

        if (errors.Any)
        {
            return errors.ToError();
        }

        var newCapacity = capacity ?? Capacity;
        if (newCapacity < ConfirmedCount)
        {
            return MatchErrors.CapacityBelowConfirmed;
        }

        var toPromote = Math.Min(newCapacity - ConfirmedCount, WaitlistCount);
        if (waitlistHead.Count < toPromote)
        {
            throw new ArgumentException(
                $"A nova capacidade promove {toPromote} inscrição(ões) da espera, mas só {waitlistHead.Count} foram carregadas.",
                nameof(waitlistHead));
        }

        // Erros de programação são detectados antes de qualquer mutação.
        for (var i = 0; i < toPromote; i++)
        {
            EnsurePromotable(waitlistHead[i], nameof(waitlistHead));
            if (i > 0 && waitlistHead[i].QueueSeq <= waitlistHead[i - 1].QueueSeq)
            {
                throw new ArgumentException("O início da lista de espera deve vir em ordem crescente de QueueSeq.", nameof(waitlistHead));
            }
        }

        Title = newTitle!;
        LocationText = newLocationText;
        Capacity = newCapacity;
        for (var i = 0; i < toPromote; i++)
        {
            Promote(waitlistHead[i], now);
        }

        Version++;
        UpdatedAt = now.ToUniversalTime();
        return Result.Success();
    }

    public Result Cancel(Guid actorId, DateTimeOffset now)
    {
        if (actorId != OrganizerId)
        {
            return MatchErrors.NotOrganizer;
        }

        if (CheckOpen(now) is { } closed)
        {
            return closed;
        }

        Status = MatchStatus.Cancelled;
        CancelledAt = now.ToUniversalTime();
        UpdatedAt = now.ToUniversalTime();
        Version++;
        Raise(new MatchCancelled(Id));
        return Result.Success();
    }

    /// <summary>
    /// Inscreve <paramref name="userId"/>: vaga livre confirma; partida lotada põe no fim da espera. <paramref name="existing"/>
    /// é a inscrição do usuário nesta partida, se houver; ativa devolve o estado atual sem alterar nada, e
    /// <see cref="RegistrationStatus.Withdrawn"/> é reaproveitada com nova posição na fila.
    /// </summary>
    public Result<RegistrationOutcome> Register(Guid userId, Registration? existing, DateTimeOffset now)
    {
        if (existing is not null && (existing.MatchId != Id || existing.UserId != userId))
        {
            throw new ArgumentException("A inscrição existente deve ser deste usuário nesta partida.", nameof(existing));
        }

        if (CheckOpen(now) is { } closed)
        {
            return closed;
        }

        if (existing is { IsActive: true })
        {
            return new RegistrationOutcome(existing, Changed: false);
        }

        RegistrationStatus status;
        if (ConfirmedCount < Capacity)
        {
            status = RegistrationStatus.Confirmed;
        }
        else if (WaitlistCount < MatchLimits.WaitlistMax)
        {
            status = RegistrationStatus.Waitlisted;
        }
        else
        {
            return MatchErrors.WaitlistFull;
        }

        // A posição vem do contador da partida, incrementado sob o lock: a ordem FIFO é a de aquisição do lock.
        var queueSeq = NextQueueSeq++;
        var at = now.ToUniversalTime();
        var registration = existing ?? new Registration(Guid.CreateVersion7(now), Id, userId, status, queueSeq, at);
        if (existing is not null)
        {
            existing.Reenter(status, queueSeq, at);
        }

        if (status == RegistrationStatus.Confirmed)
        {
            ConfirmedCount++;
            Raise(new SpotConfirmed(Id, userId, ConfirmationSource.Direct));
        }
        else
        {
            WaitlistCount++;
        }

        return new RegistrationOutcome(registration, Changed: true);
    }

    /// <summary>
    /// Cancela a inscrição. Se ela estava confirmada, a vaga vai para <paramref name="nextInLine"/>, que deve ser a
    /// inscrição em espera com menor <see cref="Registration.QueueSeq"/> (nula só se a espera estiver vazia).
    /// </summary>
    public Result Withdraw(Registration registration, Registration? nextInLine, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (nextInLine is not null)
        {
            EnsurePromotable(nextInLine, nameof(nextInLine));
        }

        if (CheckOpen(now) is { } closed)
        {
            return closed;
        }

        if (registration.MatchId != Id || !registration.IsActive)
        {
            return MatchErrors.RegistrationNotFound;
        }

        var wasConfirmed = registration.Status == RegistrationStatus.Confirmed;
        if (wasConfirmed && nextInLine is null && WaitlistCount > 0)
        {
            throw new ArgumentException("Há gente na lista de espera: informe a próxima da fila para a promoção.", nameof(nextInLine));
        }

        registration.Withdraw(now.ToUniversalTime());
        if (wasConfirmed)
        {
            ConfirmedCount--;
            Raise(new SpotReleased(Id, registration.UserId));
            if (nextInLine is not null)
            {
                Promote(nextInLine, now);
            }
        }
        else
        {
            WaitlistCount--;
        }

        return Result.Success();
    }

    /// <summary>Partida cancelada ou já iniciada não aceita alteração (nessa ordem de checagem).</summary>
    private Error? CheckOpen(DateTimeOffset now)
    {
        if (Status == MatchStatus.Cancelled)
        {
            return MatchErrors.MatchCancelled;
        }

        return now >= StartsAt ? MatchErrors.RegistrationClosed : null;
    }

    /// <summary>Única rotina de promoção da espera (edição de capacidade e saída de confirmado).</summary>
    private void Promote(Registration registration, DateTimeOffset now)
    {
        EnsurePromotable(registration, nameof(registration));
        if (ConfirmedCount >= Capacity)
        {
            throw new InvalidOperationException("Não há vaga para promover da lista de espera.");
        }

        registration.Confirm(now.ToUniversalTime());
        WaitlistCount--;
        ConfirmedCount++;
        Raise(new SpotConfirmed(Id, registration.UserId, ConfirmationSource.WaitlistPromotion));
    }

    private void EnsurePromotable(Registration registration, string paramName)
    {
        if (registration.MatchId != Id || registration.Status != RegistrationStatus.Waitlisted)
        {
            throw new ArgumentException("Só uma inscrição em espera desta partida pode ser promovida.", paramName);
        }
    }

    private void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>Acumula erros de validação por campo para devolvê-los todos de uma vez.</summary>
    private sealed class ValidationErrors
    {
        private readonly Dictionary<string, string[]> _fields = new(StringComparer.Ordinal);

        public bool Any => _fields.Count > 0;

        public void Add(string field, string message) => _fields[field] = [message];

        public string? Title(string? value)
        {
            var trimmed = value?.Trim() ?? string.Empty;
            if (trimmed.Length is 0 or > MatchLimits.TitleMax)
            {
                Add("title", $"O título deve ter de 1 a {MatchLimits.TitleMax} caracteres.");
                return null;
            }

            return trimmed;
        }

        /// <summary>Vazio ou só espaços vira <c>null</c> (sem local).</summary>
        public string? LocationText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            if (trimmed.Length > MatchLimits.LocationTextMax)
            {
                Add("locationText", $"O local deve ter no máximo {MatchLimits.LocationTextMax} caracteres.");
                return null;
            }

            return trimmed;
        }

        public void Capacity(int value)
        {
            if (value is < MatchLimits.CapacityMin or > MatchLimits.CapacityMax)
            {
                Add("capacity", $"A capacidade deve ficar entre {MatchLimits.CapacityMin} e {MatchLimits.CapacityMax}.");
            }
        }

        public Error ToError() => MatchErrors.Validation(_fields);
    }
}
