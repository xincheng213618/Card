namespace CardGame.Core;

/// <summary>
/// A synchronous, explicit state machine. Advance runs AI turns until it reaches
/// a human decision. WPF stays responsive by calling these short methods itself;
/// no UI type, dispatcher, timer, or blocking wait exists in the rules assembly.
/// </summary>
public sealed partial class GameEngine
{
    private static readonly IReadOnlyList<string> GodFactionChoices =
        Array.AsReadOnly(new[] { "wei", "shu", "wu", "qun" });

    private readonly GameOptions _options;
    private readonly ContentRegistry _contentRegistry;
    private readonly ContentModeDefinition _modeDefinition;
    private readonly IReadOnlyList<GeneralDefinition> _generalPool;
    private readonly DeterministicRandom _random;
    private readonly int _playerCount;
    private readonly List<CharacterState> _players = [];
    private readonly List<GeneralDefinition> _availableGenerals = [];
    private readonly List<int> _selectionOrder = [];
    private readonly CardZoneStore _cardZones;
    private readonly List<GameLogEntry> _log = [];
    private readonly List<AiThoughtRecord> _aiThoughts = [];
    private readonly List<AiGeneralThought> _aiGeneralThoughts = [];
    private readonly List<CardMovementRecord> _cardMovements = [];
    private readonly FrameStore _resolutionStack = new();
    private readonly List<EventEnvelope> _events = [];
    private readonly List<IGameEvent> _pendingEvents = [];
    private readonly List<ObserverFailure> _observerFailures = [];
    private readonly Queue<EngineNotification> _pendingNotifications = [];
    private readonly Dictionary<int, SimpleAiBrain> _aiBrains = [];
    private readonly Dictionary<int, CardKind> _judgmentEffectiveCardKinds = [];
    private readonly SkillRuntimeStateStore _skillRuntimeState = new();
    private readonly TurnCardUseEffectStore _turnCardUseEffects = new();
    private readonly MatchSkillBindingIndex _skillBindingIndex;
    private bool _pendingStatePublication;

    private EngineStatus _status = EngineStatus.NotStarted;
    private Winner _winner = Winner.None;
    private string? _winnerTeamId;
    private string? _winnerFactionId;
    private TurnPhase _phase = TurnPhase.NotStarted;
    private int _turnNumber;
    private int _currentSeat;
    private int _slashCountThisTurn;
    private int _playPhaseKillCountByCurrentPlayer;
    private ActualTurnProgression _turnProgression = new(ActualTurnKind.Normal, 0, 0);
    private int _playPhaseDamageDealtByCurrentPlayer;
    // Guzheng ledger: hand cards the current turn owner moved into the discard
    // pile during this turn's discard phase. Scoped to its owning turn number so
    // an interrupted turn can never serve a stale pool to the next boundary.
    private readonly List<int> _discardPhaseHandDiscardIds = new();
    private int _discardPhaseHandDiscardTurnNumber = -1;
    private bool _usedOrPlayedSlashDuringPlayPhase;
    private bool _woodenOxUsedThisTurn;
    private int _logSequence;
    private int _thoughtSequence;
    private int _movementSequence;
    private int _observerFailureSequence;
    private long _eventSequence;
    private long _resolutionSequence;
    private int _initialCardCount;
    private int _initialHandSize;
    private int _drawPerTurn;
    private long _nextPromptId;
    private int _selectionIndex;
    private bool _started;
    private bool _setupComplete;
    private bool _isExecutingPublicOperation;
    private PendingDecision? _pendingDecisionBacking;
    private PendingDecision? _pendingDecision
    {
        get => _pendingDecisionBacking;
        set => _pendingDecisionBacking = AddRequestedDeckBasicChoices(value);
    }
    private CardAttackHandle? ActiveCardAttack
    {
        get => _resolutionStack.LastOrDefault(frame => frame switch
        {
            CardUseFrame use => use.CardAttack?.Active == true,
            ProgramSkillFrame program => program.CardAttack?.Active == true,
            JudgmentFrame judgment => judgment.CardAttack?.Active == true,
            _ => false
        }) is { } owner ? new CardAttackHandle(this, owner.Id) : null;
        set
        {
            var current = ActiveCardAttack;
            if (current is not null && current.ResolutionId != value?.ResolutionId)
                UpdateCardAttackState(current.ResolutionId, state => state! with { Active = false });
            if (value is not null)
                UpdateCardAttackState(value.ResolutionId, state => state! with { Active = true });
        }
    }
    private static bool SameAttackOwner(IDamageAttempt? left, IDamageAttempt? right) =>
        left is not null && right is not null && left.ResolutionId == right.ResolutionId;
    private static bool SameContinuationOwner(ICardContinuationHandle? left, ICardContinuationHandle? right) =>
        left is not null && right is not null && left.GetType() == right.GetType() && left.OwnerFrameId == right.OwnerFrameId;
    private DuelHandle? ActiveDuel
    {
        get => FindCardContinuationOwner(state => state.Duel?.Active == true) is { } id ? new(this, id) : null;
        set
        {
            var id = value?.OwnerFrameId ?? FindCardContinuationOwner(state => state.Duel?.Active == true);
            if (id is { } owner) UpdateCardContinuations(owner, state => state with
                { Duel = state.Duel is { } current ? current with { Active = value is not null } : null });
        }
    }
    private GroupCardHandle? ActiveGroupCard
    {
        get => FindCardContinuationOwner(state => state.GroupCard?.Active == true) is { } id ? new(this, id) : null;
        set
        {
            var id = value?.OwnerFrameId ?? FindCardContinuationOwner(state => state.GroupCard?.Active == true);
            if (id is { } owner) UpdateCardContinuations(owner, state => state with
                { GroupCard = state.GroupCard is { } current ? current with { Active = value is not null } : null });
        }
    }
    private BorrowedSwordHandle? ActiveBorrowedSword
    {
        get => FindCardContinuationOwner(state => state.BorrowedSword?.Active == true) is { } id ? new(this, id) : null;
        set
        {
            var id = value?.OwnerFrameId ?? FindCardContinuationOwner(state => state.BorrowedSword?.Active == true);
            if (id is { } owner) UpdateCardContinuations(owner, state => state with
                { BorrowedSword = state.BorrowedSword is { } current ? current with { Active = value is not null } : null });
        }
    }
    private StoneAxeHandle? ActiveStoneAxe
    {
        get => FindCardContinuationOwner(state => state.StoneAxe?.Active == true) is { } id ? new(this, id) : null;
        set
        {
            var id = value?.OwnerFrameId ?? FindCardContinuationOwner(state => state.StoneAxe?.Active == true);
            if (id is { } owner) UpdateCardContinuations(owner, state => state with
                { StoneAxe = state.StoneAxe is { } current ? current with { Active = value is not null } : null });
        }
    }
    private CixiongDoubleSwordsHandle? ActiveCixiongDoubleSwords
    {
        get => FindCardContinuationOwner(state => state.CixiongDoubleSwords?.Active == true) is { } id ? new(this, id) : null;
        set
        {
            var id = value?.OwnerFrameId ?? FindCardContinuationOwner(state => state.CixiongDoubleSwords?.Active == true);
            if (id is { } owner) UpdateCardContinuations(owner, state => state with
                { CixiongDoubleSwords = state.CixiongDoubleSwords is { } current ? current with { Active = value is not null } : null });
        }
    }
    private QinglongCrescentBladeHandle? ActiveQinglongCrescentBlade
    {
        get => FindCardContinuationOwner(state => state.QinglongCrescentBlade?.Active == true) is { } id ? new(this, id) : null;
        set
        {
            var id = value?.OwnerFrameId ?? FindCardContinuationOwner(state => state.QinglongCrescentBlade?.Active == true);
            if (id is { } owner) UpdateCardContinuations(owner, state => state with
                { QinglongCrescentBlade = state.QinglongCrescentBlade is { } current ? current with { Active = value is not null } : null });
        }
    }
    private QinglongFollowupHandle? ActiveQinglongFollowup
    {
        get => FindCardContinuationOwner(state => state.QinglongFollowup?.Active == true) is { } id ? new(this, id) : null;
        set
        {
            var id = value?.OwnerFrameId ?? FindCardContinuationOwner(state => state.QinglongFollowup?.Active == true);
            if (id is { } owner) UpdateCardContinuations(owner, state => state with
                { QinglongFollowup = state.QinglongFollowup is { } current ? current with { Active = value is not null } : null });
        }
    }
    private IceSwordHandle? ActiveIceSword
    {
        get => FindCardContinuationOwner(state => state.IceSword?.Active == true) is { } id ? new(this, id) : null;
        set
        {
            var id = value?.OwnerFrameId ?? FindCardContinuationOwner(state => state.IceSword?.Active == true);
            if (id is { } owner) UpdateCardContinuations(owner, state => state with
                { IceSword = state.IceSword is { } current ? current with { Active = value is not null } : null });
        }
    }
    private QilinBowHandle? ActiveQilinBow
    {
        get => FindCardContinuationOwner(state => state.QilinBow?.Active == true) is { } id ? new(this, id) : null;
        set
        {
            var id = value?.OwnerFrameId ?? FindCardContinuationOwner(state => state.QilinBow?.Active == true);
            if (id is { } owner) UpdateCardContinuations(owner, state => state with
                { QilinBow = state.QilinBow is { } current ? current with { Active = value is not null } : null });
        }
    }
    private FangtianHalberdHandle? ActiveFangtianHalberd
    {
        get => FindCardContinuationOwner(state => state.FangtianHalberd?.Active == true) is { } id ? new(this, id) : null;
        set
        {
            var id = value?.OwnerFrameId ?? FindCardContinuationOwner(state => state.FangtianHalberd?.Active == true);
            if (id is { } owner) UpdateCardContinuations(owner, state => state with
                { FangtianHalberd = state.FangtianHalberd is { } current ? current with { Active = value is not null } : null });
        }
    }
    private FactionDefenseHandle? ActiveFactionDefense
    {
        get => FindCardContinuationOwner(state => state.FactionDefense?.Active == true) is { } id ? new(this, id) : null;
        set
        {
            var id = value?.OwnerFrameId ?? FindCardContinuationOwner(state => state.FactionDefense?.Active == true);
            if (id is { } owner) UpdateCardContinuations(owner, state => state with
                { FactionDefense = state.FactionDefense is { } current ? current with { Active = value is not null } : null });
        }
    }
    private FactionCardRequestHandle? ActiveFactionCardRequest
    {
        get => FindCardContinuationOwner(state => state.FactionCardRequest?.Active == true) is { } id ? new(this, id) : null;
        set
        {
            var id = value?.OwnerFrameId ?? FindCardContinuationOwner(state => state.FactionCardRequest?.Active == true);
            if (id is { } owner) UpdateCardContinuations(owner, state => state with
                { FactionCardRequest = state.FactionCardRequest is { } current ? current with { Active = value is not null } : null });
        }
    }
    private DelayedTurnEffects _pendingTurnDelayedEffects;
    private readonly List<CardMovementBatchContext> _pendingCardsMovedBatches = [];
    private readonly Stack<long> _activeCardMovementBatchIds = new();
    private TargetCardSelectionFrame? ActiveTargetCardSelection =>
        _resolutionStack.OfType<TargetCardSelectionFrame>().LastOrDefault();
    private NullificationWindowFrame? ActiveNullificationWindow =>
        _resolutionStack.OfType<NullificationWindowFrame>().LastOrDefault();
    private CardUseFrame? ActiveFireAttack =>
        _resolutionStack.LastOrDefault() is CardUseFrame { FireAttackSelection: not null } frame
            ? frame
            : null;
    private DamageTriggerWindowFrame? ActiveDamageTrigger => CurrentDamageAttempt is { } attack
        ? _resolutionStack.OfType<DamageTriggerWindowFrame>().LastOrDefault(window =>
            _resolutionStack.OfType<DamageFrame>().Any(damage =>
                damage.Id == window.ParentFrameId &&
                damage.ParentFrameId == attack.ResolutionId &&
                damage.SourceSeat == window.SourceSeat &&
                damage.TargetSeat == window.TargetSeat) &&
            window.SourceCardId == attack.Card?.Id &&
            window.SourceCard == attack.EffectiveCardKind)
        : null;
    private DyingFrame? ActiveDying =>
        _resolutionStack.OfType<DyingFrame>().LastOrDefault(frame => !IsOriginalDyingSuspendedByOwnedDeathBenefit(frame));
    private JudgmentFrame? ActiveJudgment =>
        _resolutionStack.OfType<JudgmentFrame>().LastOrDefault(frame => frame.CardAttack is null);

    private CardAttackHandle? GetJudgmentAttack(JudgmentFrame frame)
    {
        if (frame.Continuation is JudgmentContinuationKind.Indulgence or
            JudgmentContinuationKind.SupplyShortage or JudgmentContinuationKind.Lightning)
            return null;
        if (ActiveCardAttack?.ResolutionId != frame.ParentAttackId ||
            (frame.Continuation is JudgmentContinuationKind.Bagua or
                JudgmentContinuationKind.FactionDefenseBagua &&
             frame.ParentAttackId != frame.ParentFrameId))
            throw new InvalidOperationException("A judgment lost its parent attack.");
        return ActiveCardAttack;
    }

    private Card? GetJudgmentCard(JudgmentFrame frame) => frame.CardId is { } cardId
        ? _cardZones.CardsAt(_cardZones.GetLocation(cardId)).Single(card => card.Id == cardId)
        : null;

    private Card? GetDelayedJudgmentCard(JudgmentFrame frame) => frame.DelayedCardId is { } cardId
        ? _cardZones.CardsAt(_cardZones.GetLocation(cardId)).Single(card => card.Id == cardId)
        : null;

    private IDamageAttempt? GetDyingAttack(DyingFrame frame)
    {
        return GetCompletedDyingAttack(new DyingCompletionReceipt(
            frame.Id, frame.ParentFrameId, frame.VictimSeat, frame.Continuation));
    }

    private IDamageAttempt? GetCompletedDyingAttack(DyingCompletionReceipt receipt)
    {
        if (receipt.Continuation == DyingContinuationKind.ProgramSkill) return null;
        if (CurrentDamageAttempt is not { } attack ||
            (receipt.Continuation == DyingContinuationKind.AttackHpLoss
                ? receipt.ParentFrameId != attack.ResolutionId
                : !_resolutionStack.OfType<DamageFrame>().Any(damage =>
                    damage.Id == receipt.ParentFrameId && damage.ParentFrameId == attack.ResolutionId)))
            throw new InvalidOperationException("The dying frame lost its active attack.");
        return attack;
    }

    private IDamageAttempt GetDamageTriggerAttack(DamageTriggerWindowFrame frame) =>
        ActiveDamageTrigger is { } active && active.Id == frame.Id && CurrentDamageAttempt is { } attack
            ? attack
            : throw new InvalidOperationException("The damage trigger lost its active attack.");

    private GameEngine(
        GameOptions options,
        ContentRegistry contentRegistry)
    {
        ValidateOptions(options);
        ArgumentNullException.ThrowIfNull(contentRegistry);
        _options = options;
        _contentRegistry = contentRegistry;
        _hasForeignDiscardCapability = contentRegistry.Skills.Values.Any(skill =>
            skill.Program?.CardPolicies.Any(policy => policy.Kind == SkillProgramCardPolicyKind.PreventForeignEquipmentDiscard) == true) ||
            contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.SelectEquipmentPairAndPayment) ||
            contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.SelectDyingOwnedCard);
        var requiredProgramRulesVersion = contentRegistry.Skills.Values
            .Where(skill => skill.Program is not null)
            .Select(skill => skill.Program!.MinimumRulesVersion)
            .DefaultIfEmpty(0)
            .Max();
        if (GameCheckpoint.CurrentRulesVersion < requiredProgramRulesVersion)
            throw new InvalidOperationException(
                $"Compiled skill programs require rules version {requiredProgramRulesVersion} or newer.");
        _modeDefinition = ResolveModeDefinition(contentRegistry, options);
        ValidateModeOptions(options, _modeDefinition);
        _generalPool = CreateRuntimeGeneralPool(contentRegistry, _modeDefinition);
        _playerCount = options.PlayerCount;
        _cardZones = new CardZoneStore(_playerCount);
        _random = new DeterministicRandom(options.Seed);
        var hpSensitiveSkillIds = contentRegistry.Skills.Values
            .Where(skill => skill.SuppressionRule is not null)
            .Select(skill => skill.Id)
            .ToHashSet(StringComparer.Ordinal);
        _hasLordProjectionCapability = contentRegistry.Skills.Values.Any(s => s.Program?.LordSkillProjection == true);
        _hasGainPhaseQualificationCapability = contentRegistry.Skills.Values.Any(s => s.Program?.Triggers.Any(t => t.GainPhaseQualification is not null) == true);
        _skillBindingIndex = new MatchSkillBindingIndex(
            contentRegistry.GetSkill,
            IsNationalWarMode,
            player => player.SkillGrants.HasEnabledSkill(hpSensitiveSkillIds),
            (owner,grant)=>IsCurrentTurnSkillGrantQualified(owner,grant)&&IsProjectedGrantQualified(owner,grant)&&IsGeneralLibraryGrantQualified(owner,grant), CaptureCurrentTurnQualificationStamp, HasPrivateGeneralLibraryCapability ? PrivateGeneralLibrarySuppressionInputs : null,
            trackMarkerQualification: contentRegistry.ProgramDependencies.UsesOwnerMarkerCount);
        var deckDefinition = ResolveDeckDefinition(
            contentRegistry,
            options.DeckId ?? _modeDefinition.DeckId);
        _initialHandSize = deckDefinition.InitialHandSize;
        _drawPerTurn = deckDefinition.DrawPerTurn;
        SetupPlayers();
        if(HasPrivateGeneralLibraryCapability)foreach(var player in _players)player.GeneralLibraryGenderQuery=GetPrivateGeneralLibraryGender;
        SetupDeck();
        _currentSeat = GetStartingSeat();
        if (!options.UseInteractiveSetup)
        {
            AssignAutomaticGodFactions();
            InitializeNationalHealth();
            DealInitialHands();
            _setupComplete = true;
        }

        foreach (var player in _players.Where(player => !player.IsHuman))
        {
            _aiBrains[player.Seat] = new SimpleAiBrain(
                player.Seat,
                unchecked(options.Seed * 397) ^ (player.Seat + 1),
                options.AiPolicyVersion);
        }
    }

    public static GameEngine CreateStandard(
        GameOptions? options,
        ContentRegistry contentRegistry) =>
        new(options ?? new GameOptions(), contentRegistry);

    private bool SupportsGodFactionSelection =>
        _modeDefinition.ModeKind == ContentModeKind.Identity;

    private bool IsClassicIdentityMode =>
        _modeDefinition.Id.StartsWith("identity:classic-", StringComparison.Ordinal);

    private bool UsesGeneralBaseHp =>
        IsClassicIdentityMode;

    private bool UsesFormalKongchengTargeting =>
        IsClassicIdentityMode;

    private bool UsesFormalJianxiongDamageCard =>
        IsClassicIdentityMode;



    private bool UsesFormalWushuang =>
        IsClassicIdentityMode;

    private bool UsesFormalDaQiao =>
        IsClassicIdentityMode;

    private bool UsesFormalDiaoChan =>
        IsClassicIdentityMode;

    private bool UsesFormalSunShangxiang =>
        IsClassicIdentityMode;

    private bool UsesFormalLuXun =>
        IsClassicIdentityMode;

    private bool UsesFormalXunYu =>
        IsClassicIdentityMode;

    private bool UsesFormalTaishiCi =>
        IsClassicIdentityMode;

    private bool UsesFormalYuanShao =>
        IsClassicIdentityMode;

    private bool UsesFormalXiahouYuan =>
        IsClassicIdentityMode;

    private bool UsesFormalHuaXiong =>
        IsClassicIdentityMode;

    private bool UsesFormalGongsunZan =>
        IsClassicIdentityMode;

    private bool UsesFormalZhangJiao =>
        IsClassicIdentityMode;

    private bool UsesFormalMengHuo =>
        IsClassicIdentityMode;

    private bool UsesFormalZhuRong =>
        IsClassicIdentityMode;

    private bool UsesFormalYuJin =>
        IsClassicIdentityMode;

    private bool UsesFormalBorrowedSword =>
        IsClassicIdentityMode;

    private bool UsesFormalStoneAxe =>
        IsClassicIdentityMode;

    private bool UsesFormalZhangbaSerpentSpear =>
        IsClassicIdentityMode;

    private bool UsesFormalCixiongDoubleSwords =>
        IsClassicIdentityMode;

    private bool UsesFormalQinglongCrescentBlade =>
        IsClassicIdentityMode;

    private bool UsesFormalIceSword =>
        IsClassicIdentityMode;

    private bool UsesFormalQilinBow =>
        IsClassicIdentityMode;

    private bool UsesFormalFangtianHalberd =>
        IsClassicIdentityMode;

    private bool UsesFormalGudingBlade =>
        IsClassicIdentityMode;

    private bool UsesFormalZhuqueFan =>
        IsClassicIdentityMode;

    private bool UsesFormalTengjia =>
        IsClassicIdentityMode;

    private bool UsesFormalSilverLion =>
        IsClassicIdentityMode;

    private bool UsesFormalWoodenOx =>
        IsClassicIdentityMode;

    private bool IsTeamMode => _modeDefinition.ModeKind == ContentModeKind.Team;

    private bool IsNationalWarMode => _modeDefinition.ModeKind == ContentModeKind.NationalWarLite;

    /// <summary>The deterministic match seed for local diagnostics and replay labels.</summary>
    public int Seed => _options.Seed;

    /// <summary>The behavior version selected for deterministic replay compatibility.</summary>
    public int RulesVersion => GameCheckpoint.CurrentRulesVersion;

    /// <summary>
    /// Monotonically increasing public state revision. Player commands must carry
    /// the revision they observed so stale decisions cannot overwrite newer state.
    /// </summary>
    public long Revision => _revision;

    /// <summary>
    /// The immutable content registry used by this match.
    /// </summary>
    public ContentRegistry ContentRegistry => _contentRegistry;

    /// <summary>The player count selected by the identity mode adapter.</summary>
    public int PlayerCount => _playerCount;

    /// <summary>
    /// Returns the shortest distance between two seats on the table ring. This
    /// first rules query intentionally ignores horses and dead-seat removal.
    /// </summary>
    public int GetSeatDistance(int sourceSeat, int targetSeat)
    {
        if (!IsValidPlayerSeat(sourceSeat))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceSeat), sourceSeat, "The source seat is outside this match.");
        }

        if (!IsValidPlayerSeat(targetSeat))
        {
            throw new ArgumentOutOfRangeException(nameof(targetSeat), targetSeat, "The target seat is outside this match.");
        }

        var directDistance = Math.Abs(sourceSeat - targetSeat);
        return Math.Min(directDistance, _playerCount - directDistance);
    }

    /// <summary>
    /// Returns the public combat distance after removing dead seats and applying
    /// the source's offensive horse and target's defensive horse. The raw seat
    /// ring query above remains available for compatibility and diagnostics.
    /// </summary>
    public int GetCombatDistance(int sourceSeat, int targetSeat)
    {
        ValidatePlayerSeat(sourceSeat, nameof(sourceSeat));
        ValidatePlayerSeat(targetSeat, nameof(targetSeat));
        if (sourceSeat == targetSeat)
        {
            return 0;
        }

        return ConvertRuleValue(EvaluateDistance(_players[sourceSeat], _players[targetSeat]));
    }

    /// <summary>
    /// Returns the current public attack range. Formal weapon definitions use
    /// their printed range from rules v13; older replays retain bonus semantics.
    /// </summary>
    public int GetAttackRange(int sourceSeat)
    {
        ValidatePlayerSeat(sourceSeat, nameof(sourceSeat));
        return ConvertRuleValue(EvaluateAttackRange(_players[sourceSeat]));
    }

    /// <summary>The namespaced mode definition selected for this match.</summary>
    public string ModeId => _modeDefinition.Id;

    /// <summary>A snapshot filtered for the configured human seat.</summary>
    public GameSnapshot State => CreateSnapshot(_options.HumanSeat);

    public PendingDecision? PendingDecision => State.PendingDecision;

    public IReadOnlyList<GameLogEntry> Log => _log.AsReadOnly();

    public IReadOnlyList<AiThoughtRecord> AiThoughts => _aiThoughts.AsReadOnly();

    /// <summary>Trusted-host diagnostics for private AI general selection.</summary>
    public IReadOnlyList<AiGeneralThought> AiGeneralThoughts => _aiGeneralThoughts.AsReadOnly();

    /// <summary>Trusted-host movement ledger. Do not include it in a player network payload.</summary>
    public IReadOnlyList<CardMovementRecord> CardMovements => _cardMovements.AsReadOnly();

    /// <summary>Observer callback failures captured after committed operations.</summary>
    public IReadOnlyList<ObserverFailure> ObserverFailures => _observerFailures.AsReadOnly();

    /// <summary>Trusted-host typed event stream; do not send it as a player view.</summary>
    public IReadOnlyList<EventEnvelope> Events => _events.AsReadOnly();

    /// <summary>
    /// Accepted trusted-host command journal for deterministic local replay.
    /// Rejected commands and legacy adapter calls are intentionally omitted.
    /// Never include this journal in a player network payload.
    /// </summary>
    public IReadOnlyList<GameCommand> AcceptedCommands => _acceptedCommands.AsReadOnly();

    /// <summary>
    /// Captures a trusted-host checkpoint for a state reached entirely through
    /// the command boundary. Legacy compatibility calls are intentionally not
    /// inferred as commands, so a mixed or legacy-driven state fails loudly.
    /// </summary>
    public GameCheckpoint CreateCheckpoint()
    {
        EnsureSessionHealthy();
        if (_revision != _acceptedCommands.Count)
        {
            throw new InvalidOperationException(
                "Checkpoint capture requires a state driven entirely through Submit; " +
                "legacy adapter calls are not represented in the command journal.");
        }

        return BuildTrustedCheckpoint(_revision);
    }

    /// <summary>
    /// Trusted-host view of the in-flight data-only resolution stack. It is not
    /// included in ordinary player snapshots.
    /// </summary>
    public IReadOnlyList<ResolutionFrame> ResolutionStack => _resolutionStack.AsReadOnly();

    public event Action<GameSnapshot>? StateChanged;

    public event Action<GameLogEntry>? LogAdded;

    public event Action<AiThoughtRecord>? AiThoughtAdded;

    public event Action<AiGeneralThought>? AiGeneralThoughtAdded;

    public event Action<CardMovementRecord>? CardMoved;

    /// <summary>Raised after a typed event batch has been committed.</summary>
    public event Action<EventEnvelope>? EventCommitted;

    /// <summary>
    /// Submits one data-only command through the exclusive game boundary.
    /// Player-input errors are typed results, not exceptions.
    /// </summary>
    public CommandResult Submit(GameCommand? command)
    {
        if (_isExecutingPublicOperation)
        {
            return Reject(
                CommandErrorCode.ReentrantOperation,
                "GameEngine is dispatching a committed operation; submit again after the callback returns.");
        }

        EnsureSessionHealthy();

        if (command is null)
        {
            return Reject(CommandErrorCode.NullCommand, "A command is required.");
        }

        if (command.ExpectedRevision != _revision)
        {
            return Reject(
                CommandErrorCode.StaleRevision,
                $"The command expects revision {command.ExpectedRevision}, but the current revision is {_revision}.");
        }

        if (command is DiscardCardsCommand discardInput)
        {
            command = discardInput with { CardIds = discardInput.CardIds?.ToArray() ?? [] };
        }
        else if (command is PlayCardCommand playInput)
        {
            command = playInput with
            {
                TargetSeats = playInput.TargetSeats?.ToArray() ?? [],
                AdditionalConversionSources = playInput.AdditionalConversionSources is { } sources
                    ? Array.AsReadOnly(sources.ToArray())
                    : null
            };
        }
        else if (command is UseEquipmentEffectCommand equipmentInput)
        {
            command = equipmentInput with
            {
                CardIds = equipmentInput.CardIds?.ToArray() ?? [],
                TargetSeats = equipmentInput.TargetSeats?.ToArray() ?? []
            };
        }
        else if (command is UseProgramSkillCommand programInput)
        {
            command = programInput with
            {
                CardIds = programInput.CardIds?.ToArray() ?? [],
                TargetSeats = programInput.TargetSeats?.ToArray() ?? []
            };
        }

        _commandAwaitingCommit = CloneCommand(command);
        try
        {
            return command switch
            {
                StartGameCommand start => SubmitStart(start),
                AdvanceCommand advance => SubmitAdvance(advance, singleStep: false),
                AdvanceOneStepCommand step => SubmitAdvance(step, singleStep: true),
                SelectGeneralCommand selectGeneral => SubmitSelectGeneral(selectGeneral),
                RevealGeneralCommand revealGeneral => SubmitRevealGeneral(revealGeneral),
                PlayCardCommand play => SubmitPlayCard(play),
                RecastCardCommand recast => SubmitRecast(recast),
                UseProgramSkillCommand program => SubmitUseProgramSkill(program),
                UseEquipmentEffectCommand equipment => SubmitUseEquipmentEffect(equipment),
                EndPlayPhaseCommand end => SubmitEndPlay(end),
                DiscardCardsCommand discard => SubmitDiscardCards(discard),
                AnswerPromptCommand answer => SubmitPromptAnswer(
                    answer.ActorSeat,
                    answer.Prompt,
                    answer.Choice),
                _ => Reject(CommandErrorCode.UnsupportedCommand, "The command type is not supported by this engine.")
            };
        }
        finally
        {
            _commandAwaitingCommit = null;
        }
    }

    private CommandResult SubmitStart(StartGameCommand command)
    {
        if (_started)
        {
            return Reject(CommandErrorCode.AlreadyStarted, "The game has already started.");
        }

        return Accept(StartCore);
    }

    private CommandResult SubmitAdvance(GameCommand command, bool singleStep)
    {
        if (command.ActorSeat != -1)
        {
            return Reject(CommandErrorCode.InvalidActor, "Advance is a host command and must use actor seat -1.");
        }

        if (!_started)
        {
            return Reject(CommandErrorCode.NotStarted, "Call StartGameCommand before advancing the game.");
        }

        if (_winner != Winner.None && _status == EngineStatus.Completed)
        {
            return Reject(CommandErrorCode.Completed, "The game is already completed.");
        }

        return Accept(singleStep ? AdvanceOneStepCore : AdvanceToHumanBoundary);
    }

    private CommandResult SubmitSelectGeneral(SelectGeneralCommand command)
    {
        var validation = ValidateHumanPrompt(
            command.ActorSeat,
            DecisionKind.SelectGeneral,
            command.PromptId,
            CommandErrorCode.InvalidGeneral);
        if (validation is not null)
        {
            return Reject(validation.Code, validation.Message);
        }

        if (string.IsNullOrWhiteSpace(command.GeneralId))
        {
            return Reject(CommandErrorCode.InvalidGeneral, "A general id is required.");
        }

        if (_pendingDecision is null ||
            !_pendingDecision.ValidContentIds.Contains(command.GeneralId, StringComparer.Ordinal))
        {
            return Reject(CommandErrorCode.InvalidGeneral, "The general is not one of the published candidates.");
        }

        return Accept(() => HumanSelectGeneralCore(command.GeneralId, advanceToHumanBoundary: _options.AdvanceAfterHumanCommands));
    }

    private CommandResult SubmitRevealGeneral(RevealGeneralCommand command)
    {
        var pendingKind = _pendingDecision?.Kind ?? DecisionKind.PlayCard;
        var validation = ValidateHumanPrompt(
            command.ActorSeat,
            pendingKind,
            command.PromptId,
            CommandErrorCode.IllegalAction);
        if (validation is not null)
        {
            return Reject(validation.Code, validation.Message);
        }

        if (pendingKind is not (DecisionKind.PlayCard or DecisionKind.RespondDodge or DecisionKind.RespondSlash))
        {
            return Reject(CommandErrorCode.IllegalAction, "The engine is not waiting for a revealable national-war action.");
        }

        if (!IsNationalWarMode)
        {
            return Reject(
                CommandErrorCode.IllegalAction,
                "General reveal is only available in the national-war lite mode.");
        }

        if (!Enum.IsDefined(command.Slot))
        {
            return Reject(CommandErrorCode.IllegalAction, "The general reveal slot is invalid.");
        }

        var actor = _players[command.ActorSeat];
        var action = BuildNationalRevealActions(actor).SingleOrDefault(candidate =>
            candidate.Kind == LegalActionKind.RevealGeneral &&
            candidate.GeneralSlot == command.Slot);
        if (action is null)
        {
            return Reject(
                CommandErrorCode.IllegalAction,
                "The selected general is already public or cannot be revealed now.");
        }

        return Accept(() => HumanRevealGeneralCore(
            command.Slot,
            advanceToHumanBoundary: _options.AdvanceAfterHumanCommands));
    }

    private CommandResult SubmitPlayCard(PlayCardCommand command)
    {
        var validation = ValidateHumanPrompt(
            command.ActorSeat,
            DecisionKind.PlayCard,
            command.PromptId,
            CommandErrorCode.IllegalAction);
        if (validation is not null)
        {
            return Reject(validation.Code, validation.Message);
        }

        var targets = command.TargetSeats?.ToArray() ?? [];
        if (targets.Any(seat => !IsValidPlayerSeat(seat)))
        {
            return Reject(
                CommandErrorCode.InvalidTarget,
                $"Every target seat must be between 0 and {_playerCount - 1}.");
        }

        var actor = _players[command.ActorSeat];
        if (TrySubmitTieredRoundZeroPlay(command, out var tieredZeroResult)) return tieredZeroResult;
        var card = FindOwnedPlayableCard(actor, command.CardId);
        if (card is null)
        {
            return Reject(CommandErrorCode.InvalidCard, "The selected card is not in the actor's playable zones.");
        }

        var legal = BuildLegalActions(actor)
            .Where(action => action.CardId == command.CardId && action.Kind != LegalActionKind.EndPlay)
            .ToArray();
        var action = SelectPlayAction(
            legal,
            card,
            targets,
            command.PlayedCardKind,
            command.TargetCardId,
            command.ConversionSource,
            command.AdditionalConversionSources);

        if (action is null)
        {
            return Reject(
                legal.Length == 0 ? CommandErrorCode.IllegalAction : CommandErrorCode.InvalidTarget,
                "The selected card and exact target list are not a legal choice for this prompt.");
        }

        return Accept(() => HumanPlayCore(
            command.CardId,
            action.TargetSeat,
            advanceToHumanBoundary: _options.AdvanceAfterHumanCommands,
            playedCardKind: action.PlayedCardKind,
            targetCardId: action.TargetCardId,
            targetSeats: action.TargetSeats,
            conversionSource: action.ConversionSource,
            additionalConversionSources: action.AdditionalConversionSources));
    }

    private CommandResult SubmitUseEquipmentEffect(UseEquipmentEffectCommand command)
    {
        var validation = ValidateHumanPrompt(
            command.ActorSeat,
            DecisionKind.PlayCard,
            command.PromptId,
            CommandErrorCode.IllegalAction);
        if (validation is not null)
        {
            return Reject(validation.Code, validation.Message);
        }

        var actor = _players[command.ActorSeat];
        var cardIds = command.CardIds?.Distinct().ToArray() ?? [];
        var targets = command.TargetSeats?.Distinct().ToArray() ?? [];
        var action = BuildLegalActions(actor).SingleOrDefault(candidate =>
            candidate.Kind == LegalActionKind.UseEquipmentEffect &&
            candidate.EquipmentKind == command.EquipmentKind);
        if (action is null)
        {
            return Reject(CommandErrorCode.IllegalAction,
                "The requested equipment effect is not legal in the current play phase.");
        }

        if (cardIds.Length < action.MinCardCount || cardIds.Length > action.MaxCardCount ||
            command.CardIds?.Count != cardIds.Length ||
            cardIds.Any(cardId => !action.SelectableCardIds.Contains(cardId)))
        {
            return Reject(CommandErrorCode.InvalidCard,
                "The equipment effect card selection is invalid.");
        }

        if (targets.Length < action.MinTargetCount || targets.Length > action.MaxTargetCount ||
            command.TargetSeats?.Count != targets.Length ||
            targets.Any(target => !action.SelectableTargetSeats.Contains(target)))
        {
            return Reject(CommandErrorCode.InvalidTarget,
                "The equipment effect target selection is invalid.");
        }

        if (command.EquipmentKind == CardKind.ZhangbaSerpentSpear && ValidateNextActualUseZhangba(actor, cardIds, targets) is { } adjustedError)
            return Reject(adjustedError.Code, adjustedError.Message);
        if (command.EquipmentKind == CardKind.ZhangbaSerpentSpear && IsTurnPhysicalUseForbidden(actor.Seat, cardIds))
            return Reject(CommandErrorCode.IllegalAction, "The effective suit of this card use is forbidden this turn.");

        return Accept(() => HumanUseEquipmentEffectCore(
            command.EquipmentKind,
            cardIds,
            targets,
            _options.AdvanceAfterHumanCommands));
    }

    private CommandResult SubmitEndPlay(EndPlayPhaseCommand command)
    {
        var validation = ValidateHumanPrompt(
            command.ActorSeat,
            DecisionKind.PlayCard,
            command.PromptId,
            CommandErrorCode.IllegalAction);
        if (validation is not null)
        {
            return Reject(validation.Code, validation.Message);
        }

        return Accept(() => HumanEndPlayCore(advanceToHumanBoundary: _options.AdvanceAfterHumanCommands));
    }

    private CommandResult SubmitPromptAnswer(int actorSeat, PromptId prompt, ChoiceId choice)
    {
        if (IsRequestedDeckBasicAnswer(choice)) return SubmitRequestedDeckBasicAnswer(actorSeat, prompt, choice);
        if (_pendingDecision?.Kind == DecisionKind.ProgramJudgmentTrigger)
        {
            return SubmitProgramJudgmentTriggerAnswer(actorSeat, prompt, choice);
        }
        if (_pendingDecision?.Kind == DecisionKind.ProgramJudgmentReplacement)
        {
            return SubmitProgramJudgmentReplacementAnswer(actorSeat, prompt, choice);
        }
        if (_pendingDecision?.Kind == DecisionKind.ProgramTrigger)
        {
            return SubmitProgramTriggerAnswer(actorSeat, prompt, choice);
        }

        if (!_started)
        {
            return Reject(CommandErrorCode.NotStarted, "Call StartGameCommand before answering a prompt.");
        }

        if (_winner != Winner.None)
        {
            return Reject(CommandErrorCode.Completed, "The game is already completed.");
        }

        if (!IsValidPlayerSeat(actorSeat))
        {
            return Reject(CommandErrorCode.InvalidActor, "The prompt responder seat is invalid.");
        }

        var pending = _pendingDecision;
        if (pending is null ||
            pending.SkillPrompt is null && pending.Kind is not (DecisionKind.SelectFaction or
                DecisionKind.RespondDodge or
                DecisionKind.RespondSlash or
                DecisionKind.RescueDying or
                DecisionKind.SelectHarvestCard or
                DecisionKind.FireAttackReveal or
                DecisionKind.FireAttackDiscard or
                DecisionKind.ProgramTopReorder or
                DecisionKind.ProgramRepeatJudgment or
                DecisionKind.SkipDiscardPolicy or
                DecisionKind.Yingbo or
                DecisionKind.StoneAxe or
                DecisionKind.CixiongDoubleSwords or
                DecisionKind.QinglongCrescentBlade or
                DecisionKind.IceSword or
                DecisionKind.QilinBow or
                DecisionKind.ZhuqueFan or
                DecisionKind.Nullification or
                DecisionKind.SelectTargetCard))
        {
            return Reject(CommandErrorCode.InvalidPrompt, "There is no answerable prompt awaiting a response.");
        }

        if (pending.PlayerSeat != actorSeat)
        {
            return Reject(CommandErrorCode.NotActorTurn, "Only the published prompt responder may answer it.");
        }

        if (pending.PromptId != prompt)
        {
            return Reject(CommandErrorCode.InvalidPrompt, "The prompt id is no longer current.");
        }

        var selected = pending.Choices.FirstOrDefault(candidate => candidate.Id == choice);
        if (selected is null)
        {
            return Reject(CommandErrorCode.InvalidChoice, "The choice was not published in the current prompt.");
        }

        if (_resolutionStack.LastOrDefault() is RecoveryReplacementFrame && pending.Kind == DecisionKind.RecoveryReplacement)
            return SubmitRecoveryReplacementAnswer(selected);
        if (_resolutionStack.LastOrDefault() is CardDeclarationChallengeFrame)
            return Accept(() => { AnswerCardDeclaration(selected); AdvanceRulesAndPublishState(); });
        if (selected.Parameters.GetValueOrDefault("response") == "tiered-round-zero-use")
            return Accept(() => ResolveTieredRoundZeroResponse(selected, _options.AdvanceAfterHumanCommands));
        if (selected.Parameters.GetValueOrDefault("response") == "extended-view-as")
            return Accept(() => ResolveExtendedViewAsResponse(selected));
        if (selected.Parameters.GetValueOrDefault("response") == "program-dodge")
            return Accept(() => BeginProgramDodgeResponse(selected, _options.AdvanceAfterHumanCommands));
        if (pending.Kind == DecisionKind.SelectFaction)
        {
            if (!selected.Parameters.TryGetValue("faction-id", out var factionId) ||
                !GodFactionChoices.Contains(factionId, StringComparer.Ordinal))
            {
                return Reject(CommandErrorCode.InvalidChoice, "The god-faction choice is malformed.");
            }
            return Accept(() => HumanSelectGodFactionCore(
                factionId,
                advanceToHumanBoundary: _options.AdvanceAfterHumanCommands));
        }

        if (pending.Kind == DecisionKind.ProgramTopReorder &&
            _resolutionStack.LastOrDefault() is ProgramSkillFrame { TopReorder.RequiredTopCount: not null })
            return SubmitProgramTopReorderAnswer(selected);

        if (pending.Kind == DecisionKind.ProgramTopReorder &&
            _resolutionStack.LastOrDefault() is ProgramSkillFrame { TopReorder.Population: not null })
            return SubmitProgramTopReorderAnswer(selected);

        if (pending.SkillPrompt is not null)
            return SubmitPindianPromptAnswer(selected);

        if (pending.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash)
            CaptureSelectedResponseConversion(selected);

        if (pending.Kind == DecisionKind.RescueDying)
        {
            return SubmitDyingPromptAnswer(selected);
        }

        if (pending.Kind == DecisionKind.SelectHarvestCard)
        {
            return SubmitHarvestPromptAnswer(selected);
        }

        if (pending.Kind == DecisionKind.SelectTargetCard)
        {
            return SubmitTargetCardPromptAnswer(selected);
        }

        if (pending.Kind is DecisionKind.FireAttackReveal or DecisionKind.FireAttackDiscard)
        {
            return SubmitFireAttackPromptAnswer(selected);
        }


        if (pending.Kind == DecisionKind.ProgramTopReorder)
        {
            return SubmitProgramTopReorderAnswer(selected);
        }

        if (pending.Kind == DecisionKind.ProgramRepeatJudgment)
        {
            return SubmitProgramRepeatJudgmentAnswer(selected);
        }

        if (pending.Kind == DecisionKind.SkipDiscardPolicy)
        {
            return SubmitSkipDiscardPolicyAnswer(selected);
        }



        if (pending.Kind == DecisionKind.Yingbo)
        {
            return SubmitYingboPromptAnswer(selected);
        }

        if (pending.Kind == DecisionKind.StoneAxe)
        {
            return SubmitStoneAxePromptAnswer(selected);
        }

        if (pending.Kind == DecisionKind.CixiongDoubleSwords)
        {
            return SubmitCixiongDoubleSwordsPromptAnswer(selected);
        }

        if (pending.Kind == DecisionKind.QinglongCrescentBlade)
        {
            return SubmitQinglongCrescentBladePromptAnswer(selected);
        }

        if (pending.Kind == DecisionKind.IceSword)
        {
            return SubmitIceSwordPromptAnswer(selected);
        }

        if (pending.Kind == DecisionKind.QilinBow)
        {
            return SubmitQilinBowPromptAnswer(selected);
        }

        if (pending.Kind == DecisionKind.ZhuqueFan)
        {
            return SubmitZhuqueFanPromptAnswer(selected);
        }

        if (pending.Kind == DecisionKind.Nullification)
        {
            return SubmitNullificationPromptAnswer(selected);
        }

        if (pending.Kind == DecisionKind.RespondSlash)
        {
            return SubmitSlashPromptAnswer(selected);
        }

        if (!selected.Parameters.TryGetValue("response", out var response))
        {
            return Reject(CommandErrorCode.InvalidChoice, "The prompt choice has no supported response effect.");
        }

        if (ActiveFactionDefense is not null)
        {
            return SubmitFactionDefensePromptAnswer(selected, response);
        }

        return response switch
        {
            "faction-defense-request" when selected.Cards.Count == 0 => Accept(() => HumanRequestFactionDefenseCore(
                _options.AdvanceAfterHumanCommands)),
            "dodge" when selected.Cards.Count == 1 => Accept(() => HumanRespondCore(
                useDodge: true,
                useBagua: false,
                requestedDodgeCardId: selected.Cards[0],
                requestedResponseCardKind: ReadResponseCardKind(selected),
                advanceToHumanBoundary: _options.AdvanceAfterHumanCommands)),
            "bagua" when selected.Cards.Count == 0 => Accept(() => HumanRespondCore(
                useDodge: false,
                useBagua: true,
                requestedDodgeCardId: null,
                requestedResponseCardKind: null,
                advanceToHumanBoundary: _options.AdvanceAfterHumanCommands)),
            "take-damage" when selected.Cards.Count == 0 => Accept(() => HumanRespondCore(
                useDodge: false,
                useBagua: false,
                requestedDodgeCardId: null,
                requestedResponseCardKind: null,
                advanceToHumanBoundary: _options.AdvanceAfterHumanCommands)),
            _ => Reject(CommandErrorCode.InvalidChoice, "The prompt choice is malformed for this response window.")
        };
    }

    private CommandResult SubmitFactionDefensePromptAnswer(PromptChoice selected, string response) =>
        response switch
        {
            "faction-defense-dodge" when selected.Cards.Count == 1 => Accept(() => HumanFactionDefenseResponseCore(
                useDodge: true,
                useBagua: false,
                requestedCardId: selected.Cards[0],
                _options.AdvanceAfterHumanCommands)),
            "faction-defense-bagua" when selected.Cards.Count == 0 => Accept(() => HumanFactionDefenseResponseCore(
                useDodge: false,
                useBagua: true,
                requestedCardId: null,
                _options.AdvanceAfterHumanCommands)),
            "faction-defense-decline" when selected.Cards.Count == 0 => Accept(() => HumanFactionDefenseResponseCore(
                useDodge: false,
                useBagua: false,
                requestedCardId: null,
                _options.AdvanceAfterHumanCommands)),
            _ => Reject(CommandErrorCode.InvalidChoice, "The prompt choice is malformed for this FactionDefense response.")
        };

    private CommandResult SubmitNullificationPromptAnswer(PromptChoice selected)
    {
        if (ActiveNullificationWindow is null ||
            !selected.Parameters.TryGetValue("response", out var response))
        {
            return Reject(CommandErrorCode.InvalidPrompt, "There is no Nullification window to answer.");
        }

        return response switch
        {
            "nullification" when selected.Cards.Count == 1 && selected.Targets.Count == 0 =>
                Accept(() => HumanNullificationCore(
                    useNullification: true,
                    requestedNullificationCardId: selected.Cards[0],
                    advanceToHumanBoundary: _options.AdvanceAfterHumanCommands,
                    selectedConversionSource: ReadPublishedNullificationSource(selected))),
            "pass" when selected.Cards.Count == 0 && selected.Targets.Count == 0 =>
                Accept(() => HumanNullificationCore(
                    useNullification: false,
                    requestedNullificationCardId: null,
                    advanceToHumanBoundary: _options.AdvanceAfterHumanCommands)),
            _ => Reject(CommandErrorCode.InvalidChoice, "The Nullification choice is malformed.")
        };
    }

    private CommandResult SubmitHarvestPromptAnswer(PromptChoice selected)
    {
        if (!selected.Parameters.TryGetValue("action", out var action) ||
            action != "harvest-pick" ||
            selected.Cards.Count != 1 ||
            selected.Targets.Count != 0)
        {
            return Reject(CommandErrorCode.InvalidChoice, "The FiveGrains choice is malformed.");
        }

        return Accept(() => HumanHarvestCardCore(
            selected.Cards[0],
            advanceToHumanBoundary: _options.AdvanceAfterHumanCommands));
    }

    private CommandResult SubmitTargetCardPromptAnswer(PromptChoice selected)
    {
        if (ActiveTargetCardSelection is not { } pending ||
            _pendingDecision is not { Kind: DecisionKind.SelectTargetCard } decision)
        {
            return Reject(CommandErrorCode.InvalidPrompt, "There is no hidden target-card selection to answer.");
        }

        if (!selected.Parameters.TryGetValue("action", out var action) ||
            action != "target-card-slot" ||
            !selected.Parameters.TryGetValue("slot-index", out var slotText) ||
            !int.TryParse(
                slotText,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var slot) ||
            selected.Cards.Count != 0 ||
            selected.Targets.Count != 1 ||
            selected.Targets[0] != pending.TargetSeat ||
            !decision.ValidTargetSeats.Contains(pending.TargetSeat) ||
            slot < 0 ||
            slot >= pending.CandidateSlots.Count)
        {
            return Reject(CommandErrorCode.InvalidChoice, "The hidden target-card choice is malformed.");
        }

        return Accept(() => HumanTargetCardSelectionCore(
            slot,
            advanceToHumanBoundary: _options.AdvanceAfterHumanCommands));
    }

    private CommandResult SubmitFireAttackPromptAnswer(PromptChoice selected)
    {
        if (ActiveFireAttack is not { } pending ||
            !selected.Parameters.TryGetValue("response", out var response))
        {
            return Reject(CommandErrorCode.InvalidPrompt, "There is no FireAttack selection to answer.");
        }

        return response switch
        {
            "fire-attack-reveal" when pending.FireAttackSelection?.RevealedCardId is null &&
                                      selected.Cards.Count == 1 &&
                                      selected.Targets.Count == 0 =>
                Accept(() => HumanFireAttackCardCore(
                    selected.Cards[0],
                    advanceToHumanBoundary: _options.AdvanceAfterHumanCommands)),
            "fire-attack-discard" when pending.FireAttackSelection?.RevealedCardId is { } &&
                                       selected.Cards.Count == 1 &&
                                       selected.Targets.Count == 0 =>
                Accept(() => HumanFireAttackCardCore(
                    selected.Cards[0],
                    advanceToHumanBoundary: _options.AdvanceAfterHumanCommands)),
            "fire-attack-skip" when pending.FireAttackSelection?.RevealedCardId is { } &&
                                   selected.Cards.Count == 0 &&
                                   selected.Targets.Count == 0 =>
                Accept(() => HumanFireAttackSkipCore(advanceToHumanBoundary: _options.AdvanceAfterHumanCommands)),
            _ => Reject(CommandErrorCode.InvalidChoice, "The choice is malformed for the FireAttack prompt.")
        };
    }




    private CommandResult SubmitStoneAxePromptAnswer(PromptChoice selected)
    {
        if (ActiveStoneAxe is null ||
            _pendingDecision is not { Kind: DecisionKind.StoneAxe })
        {
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的贯石斧触发窗口。");
        }

        if (!selected.Parameters.TryGetValue("action", out var action) ||
            selected.Targets.Count != 0)
        {
            return Reject(CommandErrorCode.InvalidChoice, "贯石斧选择不符合当前攻击窗口。");
        }

        return action switch
        {
            "stone-axe-use" when selected.Cards.Count == 2 => Accept(() => HumanStoneAxeCore(
                selected.Cards,
                advanceToHumanBoundary: _options.AdvanceAfterHumanCommands)),
            "stone-axe-skip" when selected.Cards.Count == 0 => Accept(() => HumanStoneAxeCore(
                [],
                advanceToHumanBoundary: _options.AdvanceAfterHumanCommands)),
            _ => Reject(CommandErrorCode.InvalidChoice, "贯石斧必须精确弃置两张牌，或选择不发动。")
        };
    }

    private CommandResult SubmitCixiongDoubleSwordsPromptAnswer(PromptChoice selected)
    {
        var pending = ActiveCixiongDoubleSwords;
        if (pending is null ||
            _pendingDecision is not { Kind: DecisionKind.CixiongDoubleSwords } decision ||
            decision.PlayerSeat != (pending.Stage == CixiongDoubleSwordsStage.SourceActivation
                ? pending.Attack.SourceSeat
                : pending.Attack.TargetSeat))
        {
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的雌雄双股剑触发窗口。");
        }

        if (!selected.Parameters.TryGetValue("action", out var action) ||
            selected.Targets.Count != 0)
        {
            return Reject(CommandErrorCode.InvalidChoice, "雌雄双股剑选择不符合当前攻击窗口。");
        }

        var valid = pending.Stage switch
        {
            CixiongDoubleSwordsStage.SourceActivation =>
                action is "cixiong-use" or "cixiong-skip" && selected.Cards.Count == 0,
            CixiongDoubleSwordsStage.TargetChoice =>
                action == "cixiong-draw" && selected.Cards.Count == 0 ||
                action == "cixiong-discard" && selected.Cards.Count == 1,
            _ => false
        };
        return valid
            ? Accept(() => HumanCixiongDoubleSwordsCore(
                selected,
                advanceToHumanBoundary: _options.AdvanceAfterHumanCommands))
            : Reject(CommandErrorCode.InvalidChoice, "雌雄双股剑必须发动或跳过；发动后目标须弃一张手牌或令来源摸一张牌。");
    }

    private CommandResult SubmitQinglongCrescentBladePromptAnswer(PromptChoice selected)
    {
        if (ActiveQinglongCrescentBlade is null ||
            _pendingDecision is not { Kind: DecisionKind.QinglongCrescentBlade })
        {
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的青龙偃月刀触发窗口。");
        }

        if (!selected.Parameters.TryGetValue("action", out var action) ||
            selected.Targets.Count is not 0 and not 1)
        {
            return Reject(CommandErrorCode.InvalidChoice, "青龙偃月刀选择不符合当前攻击窗口。");
        }

        return action switch
        {
            "tiered-round-zero-forced-slash" when selected.Cards.Count == 0 && selected.Targets.Count == 1 =>
                Accept(() => TryResolveTieredRoundZeroForcedSlash(selected, _options.AdvanceAfterHumanCommands)),
            "qinglong-slash" when selected.Cards.Count == 1 && selected.Targets.Count == 1 =>
                Accept(() => HumanQinglongCrescentBladeCore(
                    selected,
                    advanceToHumanBoundary: _options.AdvanceAfterHumanCommands)),
            "qinglong-jijiang" when selected.Cards.Count == 0 && selected.Targets.Count == 1 =>
                Accept(() => HumanQinglongCrescentBladeCore(
                    selected,
                    advanceToHumanBoundary: _options.AdvanceAfterHumanCommands)),
            "qinglong-skip" when selected.Cards.Count == 0 && selected.Targets.Count == 0 =>
                Accept(() => HumanQinglongCrescentBladeCore(
                    selected,
                    advanceToHumanBoundary: _options.AdvanceAfterHumanCommands)),
            _ => Reject(CommandErrorCode.InvalidChoice, "青龙偃月刀必须对同一目标使用提示中精确的一张杀、发动激将，或选择不发动。")
        };
    }

    private CommandResult SubmitZhuqueFanPromptAnswer(PromptChoice selected)
    {
        if (ActiveFactionCardRequest is not { AwaitingZhuqueFanChoice: true } pending ||
            _pendingDecision is not { Kind: DecisionKind.ZhuqueFan } decision ||
            decision.PlayerSeat != pending.OwnerSeat)
        {
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的朱雀羽扇转换窗口。");
        }

        if (selected.Cards.Count != 0 ||
            selected.Targets.Count != 0 ||
            selected.Parameters.GetValueOrDefault("action") is not
                ("zhuque-fan-normal" or "zhuque-fan-fire"))
        {
            return Reject(CommandErrorCode.InvalidChoice, "朱雀羽扇必须选择保持普通杀或改为火杀。");
        }

        return Accept(() => HumanZhuqueFanCore(
            selected.Parameters.GetValueOrDefault("action") == "zhuque-fan-fire",
            advanceToHumanBoundary: _options.AdvanceAfterHumanCommands));
    }

    private CommandResult SubmitIceSwordPromptAnswer(PromptChoice selected)
    {
        if (ActiveIceSword is null ||
            _pendingDecision is not { Kind: DecisionKind.IceSword })
        {
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的寒冰剑触发窗口。");
        }

        var action = selected.Parameters.GetValueOrDefault("action");
        var valid = action switch
        {
            "ice-sword-discard" => selected.Targets.Count == 1 && selected.Cards.Count is 0 or 1,
            "ice-sword-damage" => !ActiveIceSword.Activated &&
                                  selected.Targets.Count == 0 &&
                                  selected.Cards.Count == 0,
            _ => false
        };
        return valid
            ? Accept(() => HumanIceSwordCore(
                selected,
                advanceToHumanBoundary: _options.AdvanceAfterHumanCommands))
            : Reject(CommandErrorCode.InvalidChoice, "寒冰剑必须选择目标当前的一张暗手牌/公开装备，或在首个窗口保留原伤害。");
    }

    private CommandResult SubmitQilinBowPromptAnswer(PromptChoice selected)
    {
        if (ActiveQilinBow is null ||
            _pendingDecision is not { Kind: DecisionKind.QilinBow })
        {
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的麒麟弓触发窗口。");
        }

        var action = selected.Parameters.GetValueOrDefault("action");
        var valid = action switch
        {
            "qilin-bow-discard" => selected.Targets.Count == 1 && selected.Cards.Count == 1,
            "qilin-bow-skip" => selected.Targets.Count == 0 && selected.Cards.Count == 0,
            _ => false
        };
        return valid
            ? Accept(() => HumanQilinBowCore(
                selected,
                advanceToHumanBoundary: _options.AdvanceAfterHumanCommands))
            : Reject(CommandErrorCode.InvalidChoice, "麒麟弓必须选择目标当前的一张公开坐骑，或选择不发动。");
    }


    private CommandResult SubmitSkipDiscardPolicyAnswer(PromptChoice selected)
    {
        if (_pendingDecision is not { Kind: DecisionKind.SkipDiscardPolicy })
        {
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的弃牌阶段政策窗口。");
        }

        if (!selected.Parameters.TryGetValue("action", out var action) ||
            selected.Cards.Count != 0 ||
            selected.Targets.Count != 0)
        {
            return Reject(CommandErrorCode.InvalidChoice, "选择不符合当前弃牌阶段窗口。");
        }

        return action switch
        {
            "discard-policy-use" => Accept(() => HumanSkipDiscardPolicyCore(
                useSkill: true,
                advanceToHumanBoundary: _options.AdvanceAfterHumanCommands)),
            "discard-policy-skip" => Accept(() => HumanSkipDiscardPolicyCore(
                useSkill: false,
                advanceToHumanBoundary: _options.AdvanceAfterHumanCommands)),
            _ => Reject(CommandErrorCode.InvalidChoice, "弃牌阶段提示没有可识别的选择效果。")
        };
    }


    private CommandResult SubmitSlashPromptAnswer(PromptChoice selected)
    {
        if (selected.Parameters.GetValueOrDefault("response") == "tiered-round-zero-forced-slash")
            return Accept(() => TryResolveTieredRoundZeroForcedSlash(selected, _options.AdvanceAfterHumanCommands));
        if (!selected.Parameters.TryGetValue("response", out var response))
        {
            return Reject(CommandErrorCode.InvalidChoice, "The Slash response prompt has no supported response effect.");
        }

        if (ActiveFactionCardRequest is not null)
        {
            if (ActiveFactionCardRequest.IsAssistedProgramUse)
                return Accept(() => ResolveAssistedFactionSlashChoice(selected, _options.AdvanceAfterHumanCommands));
            return SubmitFactionSlashPromptAnswer(selected, response);
        }

        if (ActiveBorrowedSword is { AwaitingSlashChoice: true })
        {
            return response switch
            {
                "faction-slash-request" when selected.Cards.Count == 0 =>
                    Accept(() => HumanBorrowedSwordFactionSlashCore(_options.AdvanceAfterHumanCommands)),
                "borrowed-sword-slash" when selected.Cards.Count == 1 =>
                    Accept(() => HumanBorrowedSwordResponseCore(
                        useSlash: true,
                        requestedSlashCardId: selected.Cards[0],
                        requestedEffectiveKind: ReadResponseCardKind(selected),
                        advanceToHumanBoundary: _options.AdvanceAfterHumanCommands)),
                "zhangba-slash" when selected.Cards.Count == 2 =>
                    Accept(() => HumanZhangbaSlashResponseCore(
                        selected.Cards,
                        _options.AdvanceAfterHumanCommands)),
                "program-view-as-slash" when selected.Cards.Count >= 2 =>
                    Accept(() => HumanProgramViewAsSlashResponseCore(
                        selected.Cards,
                        RequireConversionSource(selected),
                        _options.AdvanceAfterHumanCommands)),
                "borrowed-sword-give-weapon" when selected.Cards.Count == 0 =>
                    Accept(() => HumanBorrowedSwordResponseCore(
                        useSlash: false,
                        requestedSlashCardId: null,
                        requestedEffectiveKind: null,
                        advanceToHumanBoundary: _options.AdvanceAfterHumanCommands)),
                _ => Reject(CommandErrorCode.InvalidChoice,
                    "The choice is malformed for this Borrowed Sword response window.")
            };
        }

        return response switch
        {
            "faction-slash-request" when selected.Cards.Count == 0 => Accept(() => HumanRequestFactionSlashCore(
                _options.AdvanceAfterHumanCommands)),
            "slash" when selected.Cards.Count == 1 => Accept(() => HumanSlashResponseCore(
                useSlash: true,
                requestedSlashCardId: selected.Cards[0],
                requestedResponseCardKind: ReadResponseCardKind(selected),
                advanceToHumanBoundary: _options.AdvanceAfterHumanCommands)),
            "zhangba-slash" when selected.Cards.Count == 2 => Accept(() => HumanZhangbaSlashResponseCore(
            selected.Cards,
            _options.AdvanceAfterHumanCommands)),
            "program-view-as-slash" when selected.Cards.Count >= 2 => Accept(() => HumanProgramViewAsSlashResponseCore(
            selected.Cards,
            RequireConversionSource(selected),
            _options.AdvanceAfterHumanCommands)),
            "take-damage" when selected.Cards.Count == 0 => Accept(() => HumanSlashResponseCore(
                useSlash: false,
                requestedSlashCardId: null,
                requestedResponseCardKind: null,
                advanceToHumanBoundary: _options.AdvanceAfterHumanCommands)),
            _ => Reject(CommandErrorCode.InvalidChoice, "The choice is malformed for this Slash response window.")
        };
    }

    private CommandResult SubmitFactionSlashPromptAnswer(PromptChoice selected, string response) =>
        response switch
        {
            "faction-request-cost" when selected.Cards.Count == 1 => Accept(() =>
                ResolveFactionRequestCostChoice(selected, _options.AdvanceAfterHumanCommands)),
            "faction-slash-slash" when selected.Cards.Count == 1 => Accept(() => HumanFactionSlashResponseCore(
                useSlash: true,
                requestedCardId: selected.Cards[0],
                requestedEffectiveKind: ReadResponseCardKind(selected),
                _options.AdvanceAfterHumanCommands)),
            "zhangba-slash" when selected.Cards.Count == 2 => Accept(() => HumanZhangbaSlashResponseCore(
            selected.Cards,
            _options.AdvanceAfterHumanCommands)),
            "program-view-as-slash" when selected.Cards.Count >= 2 => Accept(() => HumanProgramViewAsSlashResponseCore(
            selected.Cards,
            RequireConversionSource(selected),
            _options.AdvanceAfterHumanCommands)),
            "faction-slash-decline" when selected.Cards.Count == 0 => Accept(() => HumanFactionSlashResponseCore(
                useSlash: false,
                requestedCardId: null,
                requestedEffectiveKind: null,
                _options.AdvanceAfterHumanCommands)),
            _ => Reject(CommandErrorCode.InvalidChoice, "The prompt choice is malformed for this FactionSlash response.")
        };

    private CommandResult SubmitDyingPromptAnswer(PromptChoice selected)
    {
        if (!selected.Parameters.TryGetValue("response", out var response))
        {
            return Reject(CommandErrorCode.InvalidChoice, "The dying prompt choice has no supported response effect.");
        }

        _ = TryReadConversionSource(selected.Parameters, out var peachConversionSource);

        return response switch
        {
            "peach" when selected.Cards.Count == 1 => Accept(() => HumanDyingResponseCore(
                usePeach: true,
                requestedPeachCardId: selected.Cards[0],
                useAlcohol: false,
                requestedAlcoholCardId: null,
                advanceToHumanBoundary: _options.AdvanceAfterHumanCommands,
                peachConversionSource: peachConversionSource)),
            "alcohol" when selected.Cards.Count == 1 => Accept(() => HumanDyingResponseCore(
                usePeach: false,
                requestedPeachCardId: null,
                useAlcohol: true,
                requestedAlcoholCardId: selected.Cards[0],
                advanceToHumanBoundary: _options.AdvanceAfterHumanCommands,
                alcoholConversionSource: peachConversionSource)),
            "program-trigger" when selected.Cards.Count == 0 => Accept(() =>
                HumanDyingProgramTriggerCore(selected, _options.AdvanceAfterHumanCommands)),
            "let-die" when selected.Cards.Count == 0 => Accept(() => HumanDyingResponseCore(
                usePeach: false,
                requestedPeachCardId: null,
                useAlcohol: false,
                requestedAlcoholCardId: null,
                advanceToHumanBoundary: _options.AdvanceAfterHumanCommands)),
            _ => Reject(CommandErrorCode.InvalidChoice, "The choice is malformed for this dying window.")
        };
    }

    private CommandError? ValidateHumanPrompt(
        int actorSeat,
        DecisionKind expectedKind,
        PromptId? promptId,
        CommandErrorCode inactiveCode)
    {
        if (!_started)
        {
            return new CommandError(CommandErrorCode.NotStarted, "Call StartGameCommand before submitting a player action.");
        }

        if (_winner != Winner.None)
        {
            return new CommandError(CommandErrorCode.Completed, "The game is already completed.");
        }

        if (!IsValidPlayerSeat(actorSeat))
        {
            return new CommandError(CommandErrorCode.InvalidActor, "The actor seat is invalid.");
        }

        if (_pendingDecision is null || _pendingDecision.Kind != expectedKind)
        {
            return new CommandError(inactiveCode, "The engine is not waiting for this player action.");
        }

        if (_pendingDecision.PlayerSeat != actorSeat)
        {
            return new CommandError(CommandErrorCode.NotActorTurn, "Only the published prompt responder may act.");
        }

        if (promptId is { } supplied && supplied != _pendingDecision.PromptId)
        {
            return new CommandError(CommandErrorCode.InvalidPrompt, "The prompt id is no longer current.");
        }

        return null;
    }

    private bool IsValidPlayerSeat(int seat) => seat >= 0 && seat < _playerCount;

    private void ValidatePlayerSeat(int seat, string parameterName)
    {
        if (!IsValidPlayerSeat(seat))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                seat,
                $"Seat must be between 0 and {_playerCount - 1}.");
        }
    }

    private int GetAliveSeatDistance(int sourceSeat, int targetSeat)
    {
        var clockwise = CountAliveSeatsOnPath(sourceSeat, targetSeat, step: 1);
        var counterClockwise = CountAliveSeatsOnPath(sourceSeat, targetSeat, step: -1);
        return Math.Min(clockwise, counterClockwise);
    }

    private int CountAliveSeatsOnPath(int sourceSeat, int targetSeat, int step)
    {
        var distance = 0;
        var seat = sourceSeat;
        do
        {
            seat = (seat + step + _playerCount) % _playerCount;
            if (_players[seat].IsAlive || seat == targetSeat)
            {
                distance++;
            }
        }
        while (seat != targetSeat);

        return distance;
    }

    private CommandResult Accept(Action operation)
    {
        var result = ExecuteExclusive(() =>
        {
            operation();
            if (_winner != Winner.None &&
                _status != EngineStatus.Completed &&
                _pendingDecision is null &&
                _resolutionStack.Count == 0)
            {
                CompleteGame();
            }

        });
        return new CommandResult(true, null, _revision, result);
    }

    private CommandResult Reject(CommandErrorCode code, string message) =>
        new(false, new CommandError(code, message), _revision,
            _commandSession.ResultBeingDelivered ?? BuildResult());


    private void StartCore()
    {
        if (_started)
        {
            throw new InvalidOperationException("The game has already started.");
        }

        _started = true;
        _status = EngineStatus.Running;
        AddLog(
            "GameStarted",
            IsNationalWarMode
                ? $"{_playerCount}人国战简化局开始，1号位先行动。"
                : IsTeamMode
                    ? $"{_playerCount}人公开阵营局开始，{GetRoleName(GetTeamRole(GetModeTeamIds()[0]))}先行动。"
                    : $"{_playerCount}人身份局开始，主公先行动。");
        if (_setupComplete)
        {
            AdvanceEventRulesAndQueueFact(new GameStartedEvent(_playerCount, _modeDefinition.Id));
            foreach (var player in _players.Where(player => player.ChosenFactionId is not null)
                         .OrderBy(player => player.Seat))
            {
                AddLog(
                    "GodFactionSelected",
                    $"{player.Name} 为神势力武将 {player.General.Name} 选择了{GetFactionName(player.ChosenFactionId!)}势力。",
                    player.Seat);
                AdvanceEventRulesAndQueueFact(new GodFactionSelectedEvent(player.Seat, player.ChosenFactionId!));
            }
        }
        else
        {
            AdvanceEventRulesAndQueueFact(new SetupStartedEvent(_playerCount, _modeDefinition.Id));
        }
        if (IsTeamMode)
        {
            foreach (var player in _players.OrderBy(player => player.Seat))
            {
                AdvanceEventRulesAndQueueFact(new TeamAssignedEvent(player.Seat, player.TeamId!));
            }
        }
        var dyingAlcoholRules = ("酒可使本回合下一张直接杀伤害 +1，也可在濒死时仅自救 1 点体力");
        var targetCardRules = ("过河拆桥和顺手牵羊的手牌效果由使用者选择不透明牌位，目标牌面不公开"
);
        var supplyShortageTargetRules = ("对距离为 1 的其他角色使用，奇才可忽略此距离限制"
);
        var delayedCardRules = ($"乐不思蜀置入目标判定区并在其下回合摸牌前判定，结果不为红桃时跳过出牌阶段；兵粮寸断{supplyShortageTargetRules}，置入目标判定区并在其下回合摸牌前判定，结果不为梅花时跳过摸牌阶段；闪电置于自己的判定区，判定为黑桃 2 至 9 时受到 3 点雷电伤害，否则移至下一名存活角色。"
);
        var modeRules = IsNationalWarMode
            ? $"{FormatFactionSummary(_modeDefinition)}；每名角色选择两名同势力武将，势力和武将默认暗置，明置后公开；消灭其他势力获胜"
            : IsTeamMode
                ? $"{FormatTeamSummary(_modeDefinition)}；阵营身份和队友关系公开，击败另一阵营即获胜，{GetRoleName(GetTeamRole(GetModeTeamIds()[0]))}的首位座位先行动"
                : FormatRoleSummary(_modeDefinition);
        var weaponRules = ("诸葛连弩攻击范围为 1 且杀不受次数限制，青釭剑攻击范围为 2 且使直接杀无视目标防具"
);
        var armorRules = ("八卦阵在需要使用或打出闪时可选择发动判定，红色判定牌视为闪，仁王盾使黑色杀在指定目标后无效"
);
        var fireAttackRules = ("火攻可对自己使用，展示牌仍留在目标手牌中"
);
        var alcoholPlayRules = ("出牌阶段每回合限使用一次酒"
);
        AddLog(
            "Rules",
            $"{modeRules}；模式 {_modeDefinition.Id}；牌堆含杀、火杀、雷杀、闪、桃、酒、决斗、无中生有、南蛮入侵、万箭齐发、桃园结义、五谷丰登、过河拆桥、顺手牵羊、火攻、乐不思蜀、兵粮寸断、无懈可击和七种装备牌。默认战斗距离按存活座位环计算、攻击范围为 1；装备按五类槽位公开替换，{weaponRules}，赤兔和绝影修正战斗距离，玉玺额外摸一张，{armorRules}。{dyingAlcoholRules}，{alcoholPlayRules}，桃可在出牌阶段自救或在基础濒死窗口救援，桃园结义按座次使所有存活角色各回复 1 点体力，五谷丰登公开翻牌并按座次私有选牌，{fireAttackRules}，并通过攻击者同花色弃牌决定是否造成火焰伤害，无懈可击在可抵消锦囊结算前按座次进入有限多层响应窗口，{(_options.UseInteractiveDiscard ? "人类回合末弃牌由玩家选择，AI 自动处理" : "弃牌自动处理")}。");
        AddLog("Rules", $"{targetCardRules}。");
        AddLog("Rules", delayedCardRules);
        AddLog("Rules", "闪电仅可对自己使用：黑桃 2 至 9 命中并造成 3 点雷电伤害，其他判定牌则移至下一名存活角色。 ");
        AdvanceRulesAndPublishState();
        AdvanceToHumanBoundary();
    }

    public IReadOnlyList<LegalAction> GetLegalActions() => GetHumanLegalActions();

    public IReadOnlyList<LegalAction> GetHumanLegalActions()
    {
        if (_pendingDecision is not { } pending || pending.PlayerSeat != _options.HumanSeat)
        {
            return [];
        }

        return pending.Kind == DecisionKind.PlayCard
            ? BuildLegalActions(_players[_options.HumanSeat])
            : pending.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash
                ? BuildNationalRevealActions(_players[_options.HumanSeat])
                : [];
    }

    private void HumanSelectGeneralCore(
        string generalId,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.SelectGeneral);
        var pending = _pendingDecision ??
            throw new InvalidOperationException("There is no general-selection prompt.");
        if (!pending.ValidContentIds.Contains(generalId, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The selected general is not a legal candidate.");
        }

        var general = _availableGenerals.SingleOrDefault(candidate => candidate.Id == generalId) ??
            throw new InvalidOperationException("The selected general is no longer available.");
        ApplyGeneralSelection(_players[pending.PlayerSeat], general);
        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }

    private void HumanRevealGeneralCore(
        GeneralSelectionSlot slot,
        bool advanceToHumanBoundary)
    {
        EnsureStarted();
        var pendingKind = _pendingDecision?.Kind ??
            throw new InvalidOperationException("There is no national-war action prompt.");
        if (pendingKind is not (DecisionKind.PlayCard or DecisionKind.RespondDodge or DecisionKind.RespondSlash))
        {
            throw new InvalidOperationException("The current prompt does not allow a national-war general reveal.");
        }

        RequireHumanDecision(pendingKind);
        if (!IsNationalWarMode)
        {
            throw new InvalidOperationException("General reveal is only available in national-war lite mode.");
        }

        var actor = _players[_options.HumanSeat];
        var action = BuildNationalRevealActions(actor).SingleOrDefault(candidate =>
            candidate.Kind == LegalActionKind.RevealGeneral &&
            candidate.GeneralSlot == slot);
        if (action is null)
        {
            throw new InvalidOperationException("The selected general cannot be revealed now.");
        }

        var isResponse = pendingKind is DecisionKind.RespondDodge or DecisionKind.RespondSlash;
        if (!isResponse)
        {
            ClearPendingDecision();
        }

        ExecuteAction(actor, action);
        if (isResponse)
        {
            RefreshNationalResponseDecision();
        }

        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }


    private void HumanPlayCore(
        int cardId,
        int? targetSeat,
        bool advanceToHumanBoundary,
        CardKind? playedCardKind = null,
        int? targetCardId = null,
        IReadOnlyList<int>? targetSeats = null,
        CardConversionSource? conversionSource = null,
        IReadOnlyList<CardConversionSource>? additionalConversionSources = null)
    {
        RequireHumanDecision(DecisionKind.PlayCard);
        var actor = _players[_options.HumanSeat];
        var card = FindOwnedPlayableCard(actor, cardId);
        var legal = card is null
            ? []
            : BuildLegalActions(actor)
                .Where(action => action.CardId == cardId && action.Kind != LegalActionKind.EndPlay)
                .ToArray();
        var selectedTargets = targetSeats?.ToArray() ??
            (targetSeat is { } target ? [target] : []);
        var action = card is null
            ? null
            : SelectPlayAction(
                legal,
                card,
                selectedTargets,
                playedCardKind,
                targetCardId,
                conversionSource,
                additionalConversionSources);

        if (action is null || action.Kind == LegalActionKind.EndPlay)
        {
            throw new InvalidOperationException("The selected card and target are not a legal play.");
        }

        ClearPendingDecision();
        ExecuteAction(actor, action);
        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }

    private void HumanUseEquipmentEffectCore(
        CardKind equipmentKind,
        IReadOnlyList<int> cardIds,
        IReadOnlyList<int> targetSeats,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.PlayCard);
        var actor = _players[_options.HumanSeat];
        var action = BuildLegalActions(actor).SingleOrDefault(candidate =>
            candidate.Kind == LegalActionKind.UseEquipmentEffect &&
            candidate.EquipmentKind == equipmentKind) ??
            throw new InvalidOperationException("The requested equipment effect is not a legal action.");
        var selectedIds = cardIds.Distinct().ToArray();
        var selectedTargets = targetSeats.Distinct().ToArray();
        if (selectedIds.Length < action.MinCardCount || selectedIds.Length > action.MaxCardCount ||
            cardIds.Count != selectedIds.Length ||
            selectedIds.Any(cardId => !action.SelectableCardIds.Contains(cardId)) ||
            selectedTargets.Length < action.MinTargetCount || selectedTargets.Length > action.MaxTargetCount ||
            targetSeats.Count != selectedTargets.Length ||
            selectedTargets.Any(target => !action.SelectableTargetSeats.Contains(target)))
        {
            throw new InvalidOperationException("The equipment-effect cards or target are no longer legal.");
        }

        ClearPendingDecision();
        if (equipmentKind == CardKind.WoodenOx)
        {
            ResolveWoodenOx(actor, selectedIds[0], selectedTargets.SingleOrDefault(-1));
        }
        else
        {
            var cards = selectedIds.Select(cardId =>
                GetPlayableCards(actor).Single(card => card.Id == cardId)).ToArray();
            if (selectedTargets.Length > 1) ResolveNextActualUseZhangba(actor, selectedTargets, cards);
            else ResolveZhangbaSlash(actor, _players[selectedTargets[0]], cards);
        }
        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }


    private void HumanEndPlayCore(bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.PlayCard);
        ClearPendingDecision();
        CompleteCurrentPlayPhase();
        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }


    private void HumanRequestFactionDefenseCore(bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.RespondDodge);
        var attack = ActiveCardAttack ??
            throw new InvalidOperationException("There is no Dodge response awaiting FactionDefense.");
        if (_pendingDecision?.Choices.All(choice =>
                choice.Parameters.GetValueOrDefault("response") != "faction-defense-request") != false)
        {
            throw new InvalidOperationException("FactionDefense is not available in the current response window.");
        }

        BeginFactionDefenseRequest(attack);
        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }

    private void HumanFactionDefenseResponseCore(
        bool useDodge,
        bool useBagua,
        int? requestedCardId,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.RespondDodge);
        var pending = ActiveFactionDefense ??
            throw new InvalidOperationException("There is no FactionDefense request awaiting a response.");
        if (pending.CurrentCandidateSeat != _options.HumanSeat)
        {
            throw new InvalidOperationException("The current FactionDefense responder is not the human seat.");
        }

        ResolveFactionDefenseCandidateResponse(pending, useDodge, useBagua, requestedCardId);
        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }

    private void HumanRequestFactionSlashCore(bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.RespondSlash);
        var attack = ActiveCardAttack ??
            throw new InvalidOperationException("There is no Slash response awaiting FactionSlash.");
        if (_pendingDecision?.Choices.All(choice =>
                choice.Parameters.GetValueOrDefault("response") != "faction-slash-request") != false)
        {
            throw new InvalidOperationException("FactionSlash is not available in the current response window.");
        }

        BeginFactionSlashResponseRequest(attack);
        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }

    private void HumanFactionSlashResponseCore(
        bool useSlash,
        int? requestedCardId,
        CardKind? requestedEffectiveKind,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.RespondSlash);
        var pending = ActiveFactionCardRequest ??
            throw new InvalidOperationException("There is no FactionSlash request awaiting a response.");
        if (pending.CurrentCandidateSeat != _options.HumanSeat)
        {
            throw new InvalidOperationException("The current FactionSlash responder is not the human seat.");
        }

        ResolveFactionSlashCandidateResponse(
            pending,
            useSlash,
            requestedCardId,
            requestedEffectiveKind);
        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }

    private void HumanRespondCore(
        bool useDodge,
        bool useBagua,
        int? requestedDodgeCardId,
        CardKind? requestedResponseCardKind,
        bool advanceToHumanBoundary)
    {
        if (_pendingDecision?.Kind == DecisionKind.Nullification)
        {
            HumanNullificationCore(
                useDodge,
                requestedDodgeCardId,
                advanceToHumanBoundary);
            return;
        }

        if (ActiveGroupCard is { Effect: GroupCardEffect.ResponseAttack })
        {
            HumanGroupResponseCore(
                useResponse: useDodge,
                useBagua: useBagua,
                requestedResponseCardId: requestedDodgeCardId,
                requestedResponseCardKind: requestedResponseCardKind,
                advanceToHumanBoundary: advanceToHumanBoundary);
            return;
        }

        RequireHumanDecision(DecisionKind.RespondDodge);
        var attack = ActiveCardAttack ??
            throw new InvalidOperationException("There is no Slash awaiting resolution.");
        var defender = _players[attack.TargetSeat];
        if (useDodge && useBagua)
        {
            throw new InvalidOperationException("A response cannot use both Dodge and Bagua.");
        }

        if (useBagua && attack.IgnoresArmor)
        {
            throw new InvalidOperationException("The attacking weapon ignores armor, so Bagua cannot respond.");
        }

        if (useBagua && !HasBagua(defender))
        {
            throw new InvalidOperationException("The responding player has no Bagua equipment.");
        }

        var selectedDodge = useDodge
            ? GetResponseCards(defender, CardKind.Dodge).FirstOrDefault(card =>
                (!requestedDodgeCardId.HasValue || card.Id == requestedDodgeCardId.Value) &&
                (!requestedResponseCardKind.HasValue ||
                 GetEffectiveResponseKind(defender, card, CardKind.Dodge) == requestedResponseCardKind.Value))
            : null;
        if (useDodge && selectedDodge is null)
        {
            throw new InvalidOperationException("The responding player has no legal Dodge response card.");
        }

        PopResponseWindow(attack.ResolutionId);
        SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        ClearPendingDecision();

        if (useDodge)
        {
            ResolveDodgeResponse(attack, defender, selectedDodge!);
        }
        else if (useBagua)
        {
            var judgmentResult = ResolveBaguaJudgment(attack, defender);
            if (judgmentResult is { } succeeded)
            {
                CompleteBaguaResponse(attack, succeeded);
            }
        }
        else
        {
            if (!ApplyAttackDamage(attack))
            {
                CompleteAttack(attack);
            }
        }

        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }

    private void HumanNullificationCore(
        bool useNullification,
        int? requestedNullificationCardId,
        bool advanceToHumanBoundary,
        CardConversionSource? selectedConversionSource = null)
    {
        RequireHumanDecision(DecisionKind.Nullification);
        var pending = ActiveNullificationWindow ??
            throw new InvalidOperationException("There is no Nullification window.");
        var decision = _pendingDecision ??
            throw new InvalidOperationException("There is no Nullification prompt.");
        Card? selected = null;
        if (useNullification)
        {
            var cardId = requestedNullificationCardId ??
                throw new InvalidOperationException("A Nullification response must name its published card.");
            selected = GetNullificationCards(_players[decision.PlayerSeat])
                .SingleOrDefault(card => card.Id == cardId);
            if (selected is null || !decision.ValidCardIds.Contains(selected.Id))
            {
                throw new InvalidOperationException(
                    "The requested Nullification card is not in the published response choices.");
            }
            ValidateNullificationConversion(_players[decision.PlayerSeat], selected, selectedConversionSource);
        }
        else if (requestedNullificationCardId is not null || selectedConversionSource is not null)
        {
            throw new InvalidOperationException("A passed Nullification response cannot name a card.");
        }

        ClearPendingDecision();
        ResolveNullificationChoice(pending, _players[decision.PlayerSeat], selected, selectedConversionSource);
        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }

    private void HumanStoneAxeCore(
        IReadOnlyList<int> discardedCardIds,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.StoneAxe);
        ResolveStoneAxeChoice(discardedCardIds);
        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }

    private void HumanCixiongDoubleSwordsCore(
        PromptChoice selected,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.CixiongDoubleSwords);
        ResolveCixiongDoubleSwordsChoice(selected);
        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }

    private void HumanQinglongCrescentBladeCore(
        PromptChoice selected,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.QinglongCrescentBlade);
        ResolveQinglongCrescentBladeChoice(selected);
        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }

    private void HumanZhuqueFanCore(
        bool convertToFireSlash,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.ZhuqueFan);
        var pending = ActiveFactionCardRequest ??
            throw new InvalidOperationException("There is no FactionSlash Zhuque Fan continuation.");
        ResolveFactionSlashZhuqueFanChoice(pending, convertToFireSlash);
        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }

    private void HumanIceSwordCore(
        PromptChoice selected,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.IceSword);
        ResolveIceSwordChoice(selected);
        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }

    private void HumanQilinBowCore(
        PromptChoice selected,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.QilinBow);
        ResolveQilinBowChoice(selected);
        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }


    private void HumanDyingResponseCore(
        bool usePeach,
        int? requestedPeachCardId,
        bool useAlcohol,
        int? requestedAlcoholCardId,
        bool advanceToHumanBoundary,
        CardConversionSource? peachConversionSource = null,
        CardConversionSource? alcoholConversionSource = null)
    {
        RequireHumanDecision(DecisionKind.RescueDying);
        var pending = ActiveDying ??
            throw new InvalidOperationException("There is no dying response awaiting resolution.");
        var responder = _players[pending.ResponderSeat];
        if (responder.Seat != _options.HumanSeat)
        {
            throw new InvalidOperationException("The current dying responder is not the human seat.");
        }

        if (usePeach && useAlcohol)
        {
            throw new InvalidOperationException("A dying response can use only one rescue source.");
        }

        if (usePeach)
        {
            var peach = GetDyingPeaches(responder).FirstOrDefault(card =>
                (!requestedPeachCardId.HasValue || card.Id == requestedPeachCardId.Value));
            if (peach is null)
            {
                throw new InvalidOperationException("The responding player has no requested Peach card.");
            }
            var sources = GetDyingPeachConversionSources(responder, peach);
            if (peach.Kind == CardKind.Peach
                ? peachConversionSource is not null
                : peachConversionSource is null || !sources.Contains(peachConversionSource))
            {
                throw new InvalidOperationException("The Peach response has no matching published conversion source.");
            }
        }

        if (useAlcohol)
        {
            if (responder.Seat != pending.VictimSeat)
            {
                throw new InvalidOperationException("Alcohol can only rescue its dying holder.");
            }
            var alcohol = GetDyingAlcohols(responder, pending.VictimSeat).FirstOrDefault(card =>
                (!requestedAlcoholCardId.HasValue || card.Id == requestedAlcoholCardId.Value));
            if (alcohol is null)
            {
                throw new InvalidOperationException("The responding player has no requested Alcohol card.");
            }
            ValidateAlcoholConversion(responder, alcohol, alcoholConversionSource, forResponse: true);
        }

        ClearPendingDecision();
        ApplyDyingResponse(
            responder,
            usePeach,
            requestedPeachCardId,
            useAlcohol,
            requestedAlcoholCardId,
            peachConversionSource,
            alcoholConversionSource);
        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }

    private void HumanDyingProgramTriggerCore(
        PromptChoice selected,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.RescueDying);
        var dying = ActiveDying ??
            throw new InvalidOperationException("There is no dying program response awaiting resolution.");
        var responder = _players[dying.ResponderSeat];
        var candidate = GetDyingProgramCandidates(responder, dying).SingleOrDefault(item =>
            item.SkillId == selected.Parameters.GetValueOrDefault("skill-id") &&
            item.BindingId == selected.Parameters.GetValueOrDefault("binding-id") &&
            item.SkillInstanceId == selected.Parameters.GetValueOrDefault("skill-instance-id")) ??
            throw new InvalidOperationException("The selected dying program binding is no longer eligible.");
        BeginDyingProgramBinding(candidate, dying);
        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }

    private void HumanHarvestCardCore(
        int cardId,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.SelectHarvestCard);
        var group = ActiveGroupCard ??
            throw new InvalidOperationException("There is no FiveGrains draft awaiting a choice.");
        var picker = _players[group.TargetSeats[group.TargetIndex]];
        if (picker.Seat != _options.HumanSeat)
        {
            throw new InvalidOperationException("The current FiveGrains picker is not the human seat.");
        }

        if (!group.RevealedCardIds.Contains(cardId))
        {
            throw new InvalidOperationException("The selected card is not in the public FiveGrains reveal.");
        }

        ClearPendingDecision();
        ResolveHarvestSelection(group, picker, cardId);
        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }




    private void HumanSkipDiscardPolicyCore(bool useSkill, bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.SkipDiscardPolicy);
        ResolveSkipDiscardPolicyChoice(useSkill);
        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }

    private void HumanTargetCardSelectionCore(
        int slot,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.SelectTargetCard);
        var pending = ActiveTargetCardSelection ??
            throw new InvalidOperationException("There is no hidden target-card selection awaiting a choice.");
        if (pending.SourceSeat != _options.HumanSeat)
        {
            throw new InvalidOperationException("The current hidden target-card source is not the human seat.");
        }

        ClearPendingDecision();
        ResolveTargetCardSelection(pending, slot);
        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }

    private void HumanFireAttackCardCore(
        int cardId,
        bool advanceToHumanBoundary)
    {
        var pending = ActiveFireAttack ??
            throw new InvalidOperationException("There is no FireAttack selection awaiting a choice.");
        var decisionKind = pending.FireAttackSelection?.RevealedCardId is null
            ? DecisionKind.FireAttackReveal
            : DecisionKind.FireAttackDiscard;
        RequireHumanDecision(decisionKind);
        if (_pendingDecision is not { } decision ||
            !decision.ValidCardIds.Contains(cardId))
        {
            throw new InvalidOperationException("The selected FireAttack card is not a legal choice.");
        }

        ClearPendingDecision();
        if (decisionKind == DecisionKind.FireAttackReveal)
        {
            ResolveFireAttackReveal(pending, cardId);
        }
        else
        {
            ResolveFireAttackDiscard(pending, cardId);
        }

        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }

    private void HumanFireAttackSkipCore(bool advanceToHumanBoundary)
    {
        var pending = ActiveFireAttack ??
            throw new InvalidOperationException("There is no FireAttack discard selection awaiting a choice.");
        RequireHumanDecision(DecisionKind.FireAttackDiscard);
        if (_pendingDecision is not { } decision ||
            !decision.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("response") == "fire-attack-skip"))
        {
            throw new InvalidOperationException("The current FireAttack prompt does not allow skipping.");
        }

        ClearPendingDecision();
        ResolveFireAttackDiscard(pending, selectedCardId: null);
        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }

    private void HumanSlashResponseCore(
        bool useSlash,
        int? requestedSlashCardId,
        CardKind? requestedResponseCardKind,
        bool advanceToHumanBoundary)
    {
        if (ActiveFactionCardRequest is not null)
            HumanFactionSlashResponseCore(useSlash, requestedSlashCardId,
                requestedResponseCardKind, advanceToHumanBoundary);
        else if (_pendingDecision?.Kind == DecisionKind.Nullification)
            HumanNullificationCore(useNullification: false,
                requestedNullificationCardId: null, advanceToHumanBoundary);
        else if (_pendingDecision?.Kind == DecisionKind.FireAttackReveal)
            HumanFireAttackCardCore(_pendingDecision.ValidCardIds.First(), advanceToHumanBoundary);
        else if (_pendingDecision?.Kind == DecisionKind.FireAttackDiscard)
        {
            if (_pendingDecision.ValidCardIds.Count > 0)
                HumanFireAttackCardCore(_pendingDecision.ValidCardIds.First(), advanceToHumanBoundary);
            else
                HumanFireAttackSkipCore(advanceToHumanBoundary);
        }
        else if (ActiveGroupCard is { Effect: GroupCardEffect.ResponseAttack })
            HumanGroupResponseCore(useResponse: useSlash, useBagua: false,
                requestedResponseCardId: requestedSlashCardId,
                requestedResponseCardKind: requestedResponseCardKind,
                advanceToHumanBoundary: advanceToHumanBoundary);
        else
            HumanDuelResponseCore(useSlash, requestedSlashCardId,
                requestedResponseCardKind, advanceToHumanBoundary);
    }

    private void HumanZhangbaSlashResponseCore(
        IReadOnlyList<int> requestedCardIds,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.RespondSlash);
        var responder = _players[_pendingDecision!.PlayerSeat];
        var pair = FindZhangbaSlashPair(responder, requestedCardIds) ??
            throw new InvalidOperationException("The responding player has no matching Zhangba Slash pair.");

        if (ActiveFactionCardRequest is { } jijiang)
        {
            ResolveFactionSlashZhangbaCandidateResponse(jijiang, responder, pair);
        }
        else if (ActiveBorrowedSword is { AwaitingSlashChoice: true } borrowedSword)
        {
            ResolveBorrowedSwordZhangbaSlashChoice(borrowedSword, pair);
        }
        else if (ActiveGroupCard is { Effect: GroupCardEffect.ResponseAttack } group)
        {
            PopResponseWindow(group.ResolutionId);
            SetCardUseStep(group.ResolutionId, ResolutionFrameStep.ResolvingEffect);
            ClearPendingDecision();
            ResolveGroupZhangbaResponse(group, responder, pair);
        }
        else
        {
            var duel = ActiveDuel ??
                throw new InvalidOperationException("There is no Slash response continuation.");
            PopResponseWindow(duel.ResolutionId);
            SetResponseParentStep(duel.ResolutionId, ResolutionFrameStep.ResolvingEffect);
            ClearPendingDecision();
            ResolveDuelZhangbaResponse(duel, responder, pair);
        }

        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }

    private void HumanProgramViewAsSlashResponseCore(
        IReadOnlyList<int> requestedCardIds,
        CardConversionSource source,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.RespondSlash);
        var responder = _players[_pendingDecision!.PlayerSeat];
        var forResponse = ActiveBorrowedSword is null &&
                          ActiveFactionCardRequest is not
                          {
                              IsProgramSkillUse: true
                          } and not
                          {
                              IsBorrowedSwordUse: true
                          } and not
                          {
                              IsQinglongCrescentBladeUse: true
                          };
        var selection = FindProgramMultiCardViewAsSelection(
            responder, requestedCardIds, CardKind.Slash, forResponse, source) ??
            throw new InvalidOperationException("The responding player has no matching configured Slash conversion.");

        if (ActiveFactionCardRequest is { } jijiang)
        {
            ResolveFactionSlashProgramMultiCardCandidateResponse(jijiang, responder, selection);
        }
        else if (ActiveBorrowedSword is { AwaitingSlashChoice: true } borrowedSword)
        {
            ResolveBorrowedSwordProgramMultiCardSlashChoice(borrowedSword, selection);
        }
        else if (ActiveGroupCard is { Effect: GroupCardEffect.ResponseAttack } group)
        {
            PopResponseWindow(group.ResolutionId);
            SetCardUseStep(group.ResolutionId, ResolutionFrameStep.ResolvingEffect);
            ClearPendingDecision();
            ResolveGroupProgramMultiCardResponse(group, responder, selection);
        }
        else
        {
            var duel = ActiveDuel ??
                throw new InvalidOperationException("There is no Slash response continuation.");
            PopResponseWindow(duel.ResolutionId);
            SetResponseParentStep(duel.ResolutionId, ResolutionFrameStep.ResolvingEffect);
            ClearPendingDecision();
            ResolveDuelProgramMultiCardResponse(duel, responder, selection);
        }

        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }

    private void HumanBorrowedSwordResponseCore(
        bool useSlash,
        int? requestedSlashCardId,
        CardKind? requestedEffectiveKind,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.RespondSlash);
        var pending = ActiveBorrowedSword ??
            throw new InvalidOperationException("There is no Borrowed Sword awaiting a response.");
        var owner = _players[pending.WeaponOwnerSeat];
        if (!pending.AwaitingSlashChoice || owner.Seat != _options.HumanSeat)
        {
            throw new InvalidOperationException("The current Borrowed Sword responder is not the human seat.");
        }

        var slash = useSlash
            ? GetBorrowedSwordSlashCards(owner, _players[pending.SlashTargetSeat])
                .SingleOrDefault(card => card.Id == requestedSlashCardId)
            : null;
        if (useSlash && slash is null)
        {
            throw new InvalidOperationException("The responding player has no matching legal Slash.");
        }

        if (slash is not null)
        {
            ResolveBorrowedSwordSlashChoice(pending, slash, requestedEffectiveKind);
        }
        else
        {
            CompleteBorrowedSwordWithoutSlash(pending, transferWeapon: true);
        }

        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }

    private void HumanBorrowedSwordFactionSlashCore(bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.RespondSlash);
        var pending = ActiveBorrowedSword ??
            throw new InvalidOperationException("There is no Borrowed Sword awaiting FactionSlash.");
        if (!pending.AwaitingSlashChoice ||
            _pendingDecision?.Choices.All(choice =>
                choice.Parameters.GetValueOrDefault("response") != "faction-slash-request") != false)
        {
            throw new InvalidOperationException("FactionSlash is not available in the current Borrowed Sword response.");
        }

        BeginBorrowedSwordFactionSlashRequest(pending);
        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }

    private void HumanDuelResponseCore(
        bool useSlash,
        int? requestedSlashCardId,
        CardKind? requestedResponseCardKind,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.RespondSlash);
        var duel = ActiveDuel ??
            throw new InvalidOperationException("There is no Duel awaiting a response.");
        var responder = _players[duel.ResponderSeat];
        if (responder.Seat != _options.HumanSeat)
        {
            throw new InvalidOperationException("The current Duel responder is not the human seat.");
        }

        var selectedSlash = useSlash
            ? GetResponseCards(responder, CardKind.Slash).FirstOrDefault(card =>
                (!requestedSlashCardId.HasValue || card.Id == requestedSlashCardId.Value) &&
                (!requestedResponseCardKind.HasValue ||
                 GetEffectiveResponseKind(responder, card, CardKind.Slash) == requestedResponseCardKind.Value))
            : null;
        if (useSlash && selectedSlash is null)
        {
            throw new InvalidOperationException("The responding player has no legal Slash response card.");
        }

        PopResponseWindow(duel.ResolutionId);
        SetResponseParentStep(duel.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        ClearPendingDecision();
        ResolveDuelResponse(duel, responder, selectedSlash);
        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }

    private void HumanGroupResponseCore(
        bool useResponse,
        bool useBagua,
        int? requestedResponseCardId,
        CardKind? requestedResponseCardKind,
        bool advanceToHumanBoundary)
    {
        var group = ActiveGroupCard ??
            throw new InvalidOperationException("There is no group attack awaiting a response.");
        var requiredCardKind = group.RequiredCardKind ??
            throw new InvalidOperationException("A group response attack must declare a required card kind.");
        var expectedDecision = requiredCardKind == CardKind.Dodge
            ? DecisionKind.RespondDodge
            : DecisionKind.RespondSlash;
        RequireHumanDecision(expectedDecision);
        var attack = group.CurrentAttack ??
            throw new InvalidOperationException("The group attack has no current target.");
        var responder = _players[attack.TargetSeat];
        if (responder.Seat != _options.HumanSeat)
        {
            throw new InvalidOperationException("The current group responder is not the human seat.");
        }

        if (useResponse && useBagua)
        {
            throw new InvalidOperationException("A response cannot use both a physical card and Bagua.");
        }

        var canUseBagua = requiredCardKind == CardKind.Dodge &&
                          !attack.IgnoresArmor &&
                          HasBagua(responder);
        if (useBagua && !canUseBagua)
        {
            throw new InvalidOperationException(
                "Bagua is not available for this group response under the active rules version.");
        }

        var selectedResponse = useResponse
            ? GetResponseCards(responder, requiredCardKind).FirstOrDefault(card =>
                (!requestedResponseCardId.HasValue || card.Id == requestedResponseCardId.Value) &&
                (!requestedResponseCardKind.HasValue ||
                 GetEffectiveResponseKind(responder, card, requiredCardKind) == requestedResponseCardKind.Value))
            : null;
        if (useResponse && selectedResponse is null)
        {
            throw new InvalidOperationException(
                $"The responding player has no legal {requiredCardKind} response card.");
        }

        PopResponseWindow(group.ResolutionId);
        SetCardUseStep(group.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        ClearPendingDecision();
        if (useBagua)
        {
            var judgmentResult = ResolveBaguaJudgment(attack, responder);
            if (judgmentResult is { } succeeded)
            {
                CompleteBaguaResponse(attack, succeeded);
            }
        }
        else
        {
            ResolveGroupResponse(group, responder, selectedResponse);
        }
        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }

    /// <summary>
    /// Creates a viewer-safe snapshot. Pass revealAll only for diagnostics/tests or
    /// an explicit post-game reveal screen.
    /// </summary>
    public GameSnapshot CreateSnapshot(int viewerSeat, bool revealAll = false) =>
        ProjectPlayerView(viewerSeat, revealAll);

    /// <summary>
    /// Returns every physical card and its exact location for trusted diagnostics,
    /// invariant tests, and future replay tooling. This is intentionally not a player view.
    /// </summary>
    public IReadOnlyList<CardZoneDiagnostic> CreateCardZoneDiagnostics() =>
        _cardZones.CreateDiagnostics();

    public string SerializeState(bool revealAll = false) =>
        SnapshotJson.Serialize(CreateSnapshot(_options.HumanSeat, revealAll));

    private static void ValidateOptions(GameOptions options)
    {
        if (options.AiPolicyVersion is not (1 or 2 or 3))
        {
            throw new ArgumentOutOfRangeException(nameof(options.AiPolicyVersion),
                "Supported AI policy versions are 1, 2 and 3.");
        }

        if (options.PlayerCount is not (4 or 5 or 6 or 8))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.PlayerCount),
                "The built-in adapters currently support 4, 5, 6, or 8 players.");
        }

        if (options.HumanSeat < -1 || options.HumanSeat >= options.PlayerCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.HumanSeat),
                $"HumanSeat must be -1 or a seat from 0 through {options.PlayerCount - 1}.");
        }

        if (options.HumanSeat == -1 && options.HumanRole is not null)
        {
            throw new ArgumentException("HumanRole must be null when HumanSeat is -1.", nameof(options));
        }

        if (options.MaxTurns <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaxTurns));
        }
    }

    private static ContentModeDefinition ResolveModeDefinition(
        ContentRegistry contentRegistry,
        GameOptions options)
    {
        var modeId = options.ModeId ??
            (options.PlayerCount == 4
                ? "team:standard-2v2"
                : $"identity:standard-{options.PlayerCount}");
        if (!contentRegistry.Modes.TryGetValue(modeId, out var registeredMode))
        {
            throw new InvalidOperationException(
                $"The supplied content registry does not contain mode '{modeId}'.");
        }
        if (options.PlayerCount < registeredMode.MinPlayers ||
            options.PlayerCount > registeredMode.MaxPlayers)
        {
            throw new InvalidOperationException(
                $"Mode '{modeId}' does not support {options.PlayerCount} players.");
        }
        return registeredMode;
    }

    private static void ValidateModeOptions(
        GameOptions options,
        ContentModeDefinition mode)
    {
        if (mode.ModeKind == ContentModeKind.Team)
        {
            if (options.HumanRole is not null)
            {
                throw new ArgumentException(
                    "HumanRole must be null in a public-team mode.",
                    nameof(options.HumanRole));
            }

            if (mode.TeamCounts is null || mode.TeamCounts.Count != 2 ||
                mode.TeamCounts.Values.Sum() != options.PlayerCount)
            {
                throw new InvalidOperationException(
                    $"Mode '{mode.Id}' is not supported by the two-team adapter for {options.PlayerCount} players.");
            }

            if (options.HumanSeat < 0 && options.HumanTeamId is not null)
            {
                throw new ArgumentException(
                    "HumanTeamId requires a human seat.",
                    nameof(options.HumanTeamId));
            }

            if (options.HumanTeamId is not null &&
                !mode.TeamCounts.ContainsKey(options.HumanTeamId))
            {
                throw new ArgumentException(
                    $"Team '{options.HumanTeamId}' is not defined by mode '{mode.Id}'.",
                    nameof(options.HumanTeamId));
            }
        }
        else if (mode.ModeKind == ContentModeKind.NationalWarLite)
        {
            if (options.HumanRole is not null)
            {
                throw new ArgumentException(
                    "HumanRole must be null in a national-war mode.",
                    nameof(options.HumanRole));
            }

            if (options.HumanTeamId is not null)
            {
                throw new ArgumentException(
                    "HumanTeamId is not valid in a national-war mode.",
                    nameof(options.HumanTeamId));
            }

            if (mode.FactionCounts is null ||
                mode.FactionCounts.Count < 2 ||
                mode.FactionCounts.Values.Sum() != options.PlayerCount)
            {
                throw new InvalidOperationException(
                    $"Mode '{mode.Id}' must define at least two factions totaling {options.PlayerCount} players.");
            }
        }
        else if (options.HumanTeamId is not null)
        {
            throw new ArgumentException(
                "HumanTeamId is only valid in a public-team mode.",
                nameof(options.HumanTeamId));
        }
    }

    private static List<Role> CreateIdentityRoles(ContentModeDefinition mode, int playerCount)
    {
        var roles = new List<Role>();
        foreach (var role in Enum.GetValues<Role>())
        {
            if (!mode.RoleCounts.TryGetValue(role.ToString(), out var count))
            {
                continue;
            }

            if (count < 0)
            {
                throw new InvalidOperationException($"Mode '{mode.Id}' has a negative {role} count.");
            }

            for (var index = 0; index < count; index++)
            {
                roles.Add(role);
            }
        }

        var unknownRoles = mode.RoleCounts.Keys
            .Where(key => !Enum.TryParse<Role>(key, ignoreCase: false, out _))
            .ToArray();
        if (unknownRoles.Length > 0)
        {
            throw new InvalidOperationException(
                $"Mode '{mode.Id}' contains unknown role ids: {string.Join(", ", unknownRoles)}.");
        }

        if (roles.Count != playerCount || roles.Count(role => role == Role.Lord) != 1)
        {
            throw new InvalidOperationException(
                $"Mode '{mode.Id}' does not provide exactly one valid {playerCount}-seat identity distribution.");
        }

        return roles;
    }

    private static string FormatRoleSummary(ContentModeDefinition mode)
    {
        var roleNames = new Dictionary<Role, string>
        {
            [Role.Lord] = "主公",
            [Role.Loyalist] = "忠臣",
            [Role.Rebel] = "反贼",
            [Role.Renegade] = "内奸"
        };
        return string.Join(
            "、",
            Enum.GetValues<Role>()
                .Where(role => mode.RoleCounts.GetValueOrDefault(role.ToString()) > 0)
                .Select(role => $"{mode.RoleCounts[role.ToString()]} {roleNames[role]}"));
    }

    private static string FormatTeamSummary(ContentModeDefinition mode) =>
        string.Join(
            "、",
            mode.TeamCounts!
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => $"{entry.Value} {GetTeamName(entry.Key)}"));

    private static string FormatFactionSummary(ContentModeDefinition mode) =>
        string.Join(
            "、",
            mode.FactionCounts!
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => $"{entry.Value} {GetFactionName(entry.Key)}"));

    private static string GetTeamName(string teamId) => teamId switch
    {
        "team:blue" => "青队",
        "team:red" => "赤队",
        _ => teamId
    };

    private IReadOnlyList<string> GetModeTeamIds() =>
        _modeDefinition.TeamCounts is { } teamCounts
            ? teamCounts.Keys.OrderBy(teamId => teamId, StringComparer.Ordinal).ToArray()
            : [];

    private IReadOnlyList<string> GetModeFactionIds() =>
        _modeDefinition.FactionCounts is { } factionCounts
            ? factionCounts.Keys.OrderBy(factionId => factionId, StringComparer.Ordinal).ToArray()
            : [];

    private static string GetFactionName(string factionId) => factionId switch
    {
        "wei" => "魏",
        "shu" => "蜀",
        "ambitious" => "野心家",
        "wu" => "吴",
        "qun" => "群",
        _ => factionId
    };

    private string? GetEffectiveFactionId(CharacterState player) =>
        IsNationalWarMode
            ? player.NationalFactionId
            : GetPrivateGeneralLibraryFaction(player) ?? player.ChosenFactionId ?? player.General.FactionId;

    private bool RequiresGodFactionSelection(CharacterState player) =>
        SupportsGodFactionSelection &&
        player.GeneralSelected &&
        string.Equals(player.General.FactionId, "god", StringComparison.Ordinal) &&
        player.ChosenFactionId is null;

    private string ChooseAutomaticGodFaction(CharacterState player) =>
        GodFactionChoices[(int)(((uint)_options.Seed + (uint)player.Seat) % (uint)GodFactionChoices.Count)];

    private void AssignAutomaticGodFactions()
    {
        foreach (var player in _players.Where(RequiresGodFactionSelection).OrderBy(player => player.Seat))
        {
            player.ChosenFactionId = ChooseAutomaticGodFaction(player);
        }
    }

    private Role GetTeamRole(string teamId)
    {
        var teamIndex = GetModeTeamIds()
            .Select((candidate, index) => (candidate, index))
            .Where(item => item.candidate == teamId)
            .Select(item => item.index)
            .FirstOrDefault(-1);
        return teamIndex switch
        {
            0 => Role.TeamA,
            1 => Role.TeamB,
            _ => throw new InvalidOperationException(
                $"Mode '{_modeDefinition.Id}' does not define a supported team '{teamId}'.")
        };
    }

    private int GetStartingSeat()
    {
        if (IsNationalWarMode)
        {
            return 0;
        }

        if (!IsTeamMode)
        {
            return _players.Single(player => player.Role == Role.Lord).Seat;
        }

        var firstTeam = GetModeTeamIds().FirstOrDefault()
            ?? throw new InvalidOperationException($"Mode '{_modeDefinition.Id}' has no teams.");
        var seat = _players
            .Where(player => player.TeamId == firstTeam)
            .OrderBy(player => player.Seat)
            .Select(player => player.Seat)
            .FirstOrDefault(-1);
        return seat >= 0
            ? seat
            : throw new InvalidOperationException(
                $"Mode '{_modeDefinition.Id}' has no seat assigned to team '{firstTeam}'.");
    }

    private static IReadOnlyList<GeneralDefinition> CreateRuntimeGeneralPool(
        ContentRegistry contentRegistry,
        ContentModeDefinition mode)
    {
        var ids = mode.GeneralPoolIds ?? contentRegistry.Generals.Keys
            .Where(contentRegistry.IsGeneralPlayable)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        return ids.Select(id =>
            {
                var definition = contentRegistry.Generals.TryGetValue(id, out var general)
                    ? general
                    : throw new InvalidOperationException(
                        $"Mode '{mode.Id}' references unknown general '{id}'.");
                var skills = definition.SkillIds
                    .Select(contentRegistry.GetSkill)
                    .ToArray();
                return new GeneralDefinition(
                    definition.Id,
                    definition.Name,
                    definition.PortraitKey,
                    skills.Select(ToRuntimeSkillDefinition).ToArray(),
                    definition.FactionId,
                    definition.BaseHp,
                    definition.Gender) { InitialHp = definition.InitialHp };
            })
            .ToArray();
    }

    private static ContentDeckRecipe ResolveDeckDefinition(
        ContentRegistry contentRegistry,
        string? deckId)
    {
        var requestedId = deckId ?? "standard:basic-demo";
        if (!contentRegistry.Decks.TryGetValue(requestedId, out var definition))
        {
            throw new InvalidOperationException(
                $"The supplied content registry does not contain deck '{requestedId}'.");
        }

        var hasCountRecipe = definition.Cards.Count > 0;
        var hasPhysicalRecipe = definition.PhysicalCards is { Count: > 0 };
        if (definition.InitialHandSize < 0 || definition.DrawPerTurn < 0 ||
            hasCountRecipe == hasPhysicalRecipe)
        {
            throw new InvalidOperationException("The supplied deck recipe is invalid.");
        }

        return definition;
    }

    private void SetupPlayers()
    {
        if (IsTeamMode)
        {
            SetupTeamPlayers();
            return;
        }

        if (IsNationalWarMode)
        {
            SetupNationalWarPlayers();
            return;
        }

        var availableRoles = CreateIdentityRoles(_modeDefinition, _playerCount);
        var assigned = new Role[_playerCount];

        if (_options.HumanSeat >= 0 && _options.HumanRole is { } forcedRole)
        {
            assigned[_options.HumanSeat] = forcedRole;
            if (!availableRoles.Remove(forcedRole))
            {
                throw new ArgumentException(
                    $"Mode '{_modeDefinition.Id}' does not contain a {forcedRole} seat.",
                    nameof(_options.HumanRole));
            }
            _random.Shuffle(availableRoles);
            var roleIndex = 0;
            for (var seat = 0; seat < _playerCount; seat++)
            {
                if (seat != _options.HumanSeat)
                {
                    assigned[seat] = availableRoles[roleIndex++];
                }
            }
        }
        else
        {
            _random.Shuffle(availableRoles);
            for (var seat = 0; seat < _playerCount; seat++)
            {
                assigned[seat] = availableRoles[seat];
            }
        }

        if (_generalPool.Count < _playerCount)
        {
            throw new InvalidOperationException(
                $"Mode '{_modeDefinition.Id}' needs {_playerCount} generals but only {_generalPool.Count} are available.");
        }

        var generals = _generalPool.ToList();
        if (!_options.UseInteractiveSetup)
        {
            _random.Shuffle(generals);
        }

        var hiddenGeneral = CreateHiddenGeneral();
        for (var seat = 0; seat < _playerCount; seat++)
        {
            var role = assigned[seat];
            var assignedGeneral = _options.UseInteractiveSetup ? hiddenGeneral : generals[seat];
            var maxHp = UsesGeneralBaseHp
                ? assignedGeneral.BaseHp + (role == Role.Lord ? 1 : 0)
                : role == Role.Lord ? 5 : 4;
            _players.Add(new CharacterState
            {
                Seat = seat,
                Name = seat == _options.HumanSeat ? "你" : $"AI {seat + 1}",
                IsHuman = seat == _options.HumanSeat,
                Role = role,
                TeamId = null,
                TeamRevealed = false,
                RoleRevealed = role == Role.Lord,
                General = assignedGeneral,
                GeneralSelected = !_options.UseInteractiveSetup,
                GeneralRevealed = !_options.UseInteractiveSetup,
                MaxHp = maxHp,
                Hp = UsesGeneralBaseHp && assignedGeneral.InitialHp is { } initialHp
                    ? initialHp + (role == Role.Lord ? 1 : 0) : maxHp
            });
        }

        if (_options.UseInteractiveSetup)
        {
            _availableGenerals.AddRange(generals);
            _random.Shuffle(_availableGenerals);
            _selectionOrder.AddRange(_players
                .OrderBy(player => player.Role == Role.Lord ? 0 : 1)
                .ThenBy(player => player.Seat)
                .Select(player => player.Seat));
        }
    }

    private void SetupTeamPlayers()
    {
        var teamIds = GetModeTeamIds();
        if (teamIds.Count != 2 || _modeDefinition.TeamCounts is null)
        {
            throw new InvalidOperationException(
                $"Mode '{_modeDefinition.Id}' requires exactly two public teams for this engine adapter.");
        }

        var availableTeams = _modeDefinition.TeamCounts
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .SelectMany(entry => Enumerable.Repeat(entry.Key, entry.Value))
            .ToList();
        var assignedTeams = new string[_playerCount];
        if (_options.HumanSeat >= 0 && _options.HumanTeamId is { } humanTeamId)
        {
            assignedTeams[_options.HumanSeat] = humanTeamId;
            if (!availableTeams.Remove(humanTeamId))
            {
                throw new ArgumentException(
                    $"Mode '{_modeDefinition.Id}' has no remaining seat for team '{humanTeamId}'.",
                    nameof(_options.HumanTeamId));
            }

            _random.Shuffle(availableTeams);
            var teamIndex = 0;
            for (var seat = 0; seat < _playerCount; seat++)
            {
                if (seat != _options.HumanSeat)
                {
                    assignedTeams[seat] = availableTeams[teamIndex++];
                }
            }
        }
        else
        {
            _random.Shuffle(availableTeams);
            for (var seat = 0; seat < _playerCount; seat++)
            {
                assignedTeams[seat] = availableTeams[seat];
            }
        }

        if (_generalPool.Count < _playerCount)
        {
            throw new InvalidOperationException(
                $"Mode '{_modeDefinition.Id}' needs {_playerCount} generals but only {_generalPool.Count} are available.");
        }

        var generals = _generalPool.ToList();
        if (!_options.UseInteractiveSetup)
        {
            _random.Shuffle(generals);
        }

        var hiddenGeneral = CreateHiddenGeneral();
        for (var seat = 0; seat < _playerCount; seat++)
        {
            var teamId = assignedTeams[seat];
            _players.Add(new CharacterState
            {
                Seat = seat,
                Name = seat == _options.HumanSeat ? "你" : $"AI {seat + 1}",
                IsHuman = seat == _options.HumanSeat,
                Role = GetTeamRole(teamId),
                TeamId = teamId,
                TeamRevealed = true,
                RoleRevealed = true,
                General = _options.UseInteractiveSetup ? hiddenGeneral : generals[seat],
                GeneralSelected = !_options.UseInteractiveSetup,
                GeneralRevealed = !_options.UseInteractiveSetup,
                MaxHp = 4,
                Hp = 4
            });
        }

        if (_options.UseInteractiveSetup)
        {
            _availableGenerals.AddRange(generals);
            _random.Shuffle(_availableGenerals);
            _selectionOrder.AddRange(_players
                .OrderBy(player => player.Seat)
                .Select(player => player.Seat));
        }
    }

    private void SetupNationalWarPlayers()
    {
        if (_modeDefinition.FactionCounts is not { Count: >= 2 } factionCounts ||
            factionCounts.Values.Sum() != _playerCount)
        {
            throw new InvalidOperationException(
                $"Mode '{_modeDefinition.Id}' requires at least two hidden factions totaling {_playerCount} seats.");
        }

        var availableFactions = factionCounts
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .SelectMany(entry => Enumerable.Repeat(entry.Key, entry.Value))
            .ToList();
        _random.Shuffle(availableFactions);

        var generals = _generalPool.ToList();
        if (!_options.UseInteractiveSetup)
        {
            _random.Shuffle(generals);
        }

        var hiddenGeneral = CreateHiddenGeneral();
        for (var seat = 0; seat < _playerCount; seat++)
        {
            var factionId = availableFactions[seat];
            var seatGenerals = generals
                .Where(general => string.Equals(general.FactionId, factionId, StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (!_options.UseInteractiveSetup && seatGenerals.Length != 2)
            {
                throw new InvalidOperationException(
                    $"Mode '{_modeDefinition.Id}' could not reserve two generals for faction '{factionId}'.");
            }

            if (!_options.UseInteractiveSetup)
            {
                foreach (var general in seatGenerals)
                {
                    generals.Remove(general);
                }
            }

            _players.Add(new CharacterState
            {
                Seat = seat,
                Name = seat == _options.HumanSeat ? "你" : $"AI {seat + 1}",
                IsHuman = seat == _options.HumanSeat,
                // The legacy combat adapter still needs a non-null Role for
                // old AI/scoring contracts. National snapshots never expose
                // this compatibility value as an identity role.
                Role = Role.Renegade,
                TeamId = null,
                TeamRevealed = false,
                NationalFactionId = factionId,
                FactionRevealed = false,
                RoleRevealed = false,
                General = _options.UseInteractiveSetup ? hiddenGeneral : seatGenerals[0],
                GeneralSelected = !_options.UseInteractiveSetup,
                GeneralRevealed = false,
                SecondaryGeneral = _options.UseInteractiveSetup ? null : seatGenerals[1],
                SecondaryGeneralSelected = !_options.UseInteractiveSetup,
                SecondaryGeneralRevealed = false,
                MaxHp = 4,
                Hp = 4
            });
        }

        if (_options.UseInteractiveSetup)
        {
            _availableGenerals.AddRange(_generalPool);
            _random.Shuffle(_availableGenerals);
            _selectionOrder.AddRange(_players
                .OrderBy(player => player.Seat)
                .SelectMany(player => new[] { player.Seat, player.Seat }));
        }
    }

    private static IReadOnlyList<Card> CreateDeckFromRegistry(
        ContentRegistry contentRegistry,
        ContentDeckRecipe definition)
    {
        if (definition.PhysicalCards is { Count: > 0 } physicalCards)
        {
            return physicalCards.Select((entry, index) =>
            {
                var cardDefinition = contentRegistry.GetCard(entry.CardDefinitionId);
                if (cardDefinition.LegacyKind is not { } kind)
                {
                    throw new InvalidOperationException(
                        $"Card '{cardDefinition.Id}' has no legacy runtime projection for this engine.");
                }

                return new Card(index + 1, kind, entry.Suit, entry.Rank);
            }).ToArray();
        }

        var cards = new List<Card>(definition.Cards.Sum(entry => entry.Count));
        var id = 1;
        foreach (var entry in definition.Cards)
        {
            if (entry.Count <= 0)
            {
                throw new InvalidOperationException(
                    $"Deck recipe '{definition.Id}' contains a non-positive count.");
            }

            var cardDefinition = contentRegistry.GetCard(entry.CardDefinitionId);
            if (cardDefinition.LegacyKind is not { } kind)
            {
                throw new InvalidOperationException(
                    $"Card '{cardDefinition.Id}' has no legacy runtime projection for this engine.");
            }

            for (var copy = 0; copy < entry.Count; copy++)
            {
                var suit = (Suit)((id - 1) % 4);
                var rank = ((id - 1) % 13) + 1;
                cards.Add(new Card(id++, kind, suit, rank));
            }
        }

        return cards;
    }

    private void SetupDeck()
    {
        var cards = CreateDeckFromRegistry(
            _contentRegistry,
            ResolveDeckDefinition(_contentRegistry, _options.DeckId ?? _modeDefinition.DeckId));
        _cardZones.LoadInitialDeck(cards);
        _initialCardCount = _cardZones.TotalCards;
        if (!_options.UseInteractiveSetup)
        {
            if (UsesFormalWoodenOx && cards.Count(card => card.Kind == CardKind.WoodenOx) == 1)
            {
                _cardZones.ShuffleKeepingSingleKindAtBottom(CardLocation.DrawPile, _random, CardKind.WoodenOx);
            }
            else
            {
                _cardZones.Shuffle(CardLocation.DrawPile, _random);
            }
        }
        AssertCoreInvariants();
    }

    private void DealInitialHands()
    {
        for (var cardIndex = 0; cardIndex < _initialHandSize; cardIndex++)
        {
            foreach (var player in _players)
            {
                // The first seat of the first public team starts with one
                // fewer card in the 2v2 adapter, compensating for initiative.
                if (IsTeamMode && cardIndex == 0 && player.Seat == _currentSeat)
                {
                    continue;
                }

                DrawCards(player, 1, log: false, CardMoveReasons.InitialDeal);
            }
        }

        AssertCoreInvariants();
    }

    private void BeginTurn()
    {
        var current = _players[_currentSeat];
        if (!current.IsAlive)
        {
            _currentSeat = FindNextAliveSeat(_currentSeat);
            current = _players[_currentSeat];
        }

        if (_turnNumber >= _options.MaxTurns)
        {
            EndAsDraw($"达到最大回合数 {_options.MaxTurns}");
            return;
        }

        BeginRoundForTurn(current);
        _turnNumber++;
        _turnProgression = _turnProgression with { OwnerSeat = _currentSeat, TurnNumber = _turnNumber };
        ExpireOwnerTurnStartDamageModifiers(current.Seat);
        _slashCountThisTurn = 0;
        ResetCardUseDebitPhase();
        _usedOrPlayedSlashDuringPlayPhase = false;
        _woodenOxUsedThisTurn = false;
        current.UsedPlayPhaseAlcoholThisTurn = false;
        _skillRuntimeState.ResetTurn();
        ResetTurnProgramBooleanStates();
        foreach (var character in _players)
            foreach (var grant in character.SkillGrants.Grants.Where(grant =>
                         grant.SourceId.StartsWith("turn:", StringComparison.Ordinal)))
                character.SkillGrants.RemoveGrant(grant.GrantId);
        foreach (var key in _programUses.Keys.Where(key => key.Seat == current.Seat).ToArray())
            _programUses.Remove(key);
        _phase = TurnPhase.Draw;
        AddLog(
            "TurnStarted",
            $"第 {_turnNumber} 回合：{current.Name}（{GetPublicGeneralName(current)}）行动。",
            current.Seat);
        AdvanceEventRulesAndQueueFact(new TurnStartedEvent(_turnNumber, current.Seat));
        if (TryQueueHengyeRecovery(current)) return;
        ResolveHengyeTurnStart(current);
        ContinueTurnAfterHengye(current);
    }

    private void ContinueTurnAfterHengye(CharacterState current)
    {
        if (TryBeginForeignActualTurnStart(current)) return;
        ContinueTurnAfterForeignActualContests(current);
    }

    private void ContinueTurnAfterForeignActualContests(CharacterState current)
    {
        ExpirePrepDiscardEndingPromises();
        ConsumeSkippedNextTurnDrawBenefits(current);
        CleanupDeferredHandAlignments();
        CleanupLostDeferredPileSources();
        CleanupLostPublicPersistentPiles();
        if (current.IsFaceDown)
        {
            ClearActualDiscardRecoveryPhase();
            current.IsFaceDown = false;
            RecordCharacterStateChange(current.Seat, SkillProgramTriggerWindow.CharacterTurnedFaceUp);
            RecordCharacterTurnedOver(current.Seat, wasFaceDown: true);
            AddLog("SkillTriggered", $"{current.Name} 将武将牌翻回正面并跳过本回合。", current.Seat);
            if (!TryBeginCharacterStateProgramWindow(continuation: CharacterStateContinuation.SkippedTurn))
                CompleteFaceUpSkippedTurn(current);
            return;
        }

        StartActualDiscardRecoveryPhase(ActualDiscardRecoveryPhaseKind.Preparation, current.Seat);
        ApplyQueuedNextTurnRuleModifiers(current.Seat);
        _pendingTurnDelayedEffects = DelayedTurnEffects.None;
        if (TryBeginTurnStartProgramWindow(current))
        {
            return;
        }

        BeginNormalTurnStartAfterProgramBindings(current);
    }

    private void BeginTurnStartAfterProgramLifecycle(CharacterState current)
    {
        if (_pendingTurnDelayedEffects.HasFlag(DelayedTurnEffects.SkipJudgmentPhase))
            CompleteTurnStart(current, _pendingTurnDelayedEffects);
        else
        {
            StartActualDiscardRecoveryPhase(ActualDiscardRecoveryPhaseKind.Judgment, current.Seat);
            if (!TryBeginJudgmentPhaseStartingPrograms(current)) BeginDelayedJudgmentOrTurnStart(current);
        }
    }

    private void BeginDelayedJudgmentOrTurnStart(CharacterState current, int? excludedCardId = null)
    {
        var delayedCard = GetJudgment(current)
            .FirstOrDefault(card =>
                IsDelayedCard(GetJudgmentEffectiveCardKind(card)) &&
                card.Id != excludedCardId);
        if (delayedCard is not null)
        {
            var effectiveKind = GetJudgmentEffectiveCardKind(delayedCard);
            if (TryBeginDelayedCardEffectPrograms(current,delayedCard,effectiveKind)) return;
            BeginActualDelayedJudgment(current,delayedCard,effectiveKind);

            return;
        }

        CompleteTurnStart(current, _pendingTurnDelayedEffects);
    }

    private void CompleteTurnStart(CharacterState current, DelayedTurnEffects delayedEffects)
    {
        if (delayedEffects.HasFlag(DelayedTurnEffects.SkipDrawPhase)) ClearActualDiscardRecoveryPhase();
        else StartActualDiscardRecoveryPhase(ActualDiscardRecoveryPhaseKind.Draw, current.Seat);
        if (delayedEffects.HasFlag(DelayedTurnEffects.SkipDrawPhase))
        {
            AddLog("DelayedCardEffect", $"{current.Name} 跳过摸牌阶段。", current.Seat);
        }
        else if (TryBeginDrawPhaseProgramWindow(
                     current,
                     delayedEffects.HasFlag(DelayedTurnEffects.SkipPlayPhase)))
        {
            return;
        }
        else
        {
            DrawCards(current, GetTurnDrawCount(current), log: true);
        }

        CompleteTurnStartAfterDraw(current, delayedEffects);
    }

    private int GetTurnDrawCount(CharacterState current)
        => ConvertRuleValue(EvaluateDrawCount(current));

    private void CompleteDrawPhaseAfterProgramWindow(
        CharacterState current,
        bool skipPlayPhaseAfterDraw,
        bool normalDrawReplaced,
        int normalDrawAdjustment,
        int? frozenBaseDrawCount = null,
        bool confirmedActualDrawSubstitution = false)
    {
        if (confirmedActualDrawSubstitution)
        {
            CompleteTurnStartAfterDraw(current, _pendingTurnDelayedEffects |
                (skipPlayPhaseAfterDraw ? DelayedTurnEffects.SkipPlayPhase : DelayedTurnEffects.None));
            return;
        }
        if (!normalDrawReplaced)
            DrawCards(current, Math.Max(0, checked((frozenBaseDrawCount ?? GetTurnDrawCount(current)) +
                normalDrawAdjustment)), log: true);
        CompleteTurnStartAfterDraw(
            current,
            skipPlayPhaseAfterDraw ? DelayedTurnEffects.SkipPlayPhase : DelayedTurnEffects.None);
    }

    private void CompleteTurnStartAfterDraw(CharacterState current, DelayedTurnEffects delayedEffects,
        bool afterNormalDrawProgramsCompleted = false, bool drawPhaseEndedProgramsCompleted = false,
        bool actualDrawCompletionCompleted = false)
    {
        if (!drawPhaseEndedProgramsCompleted && !actualDrawCompletionCompleted && TryBeginActualDrawCompletion(current, delayedEffects)) return;
        if (!drawPhaseEndedProgramsCompleted && !delayedEffects.HasFlag(DelayedTurnEffects.SkipDrawPhase) && TryBeginDrawPhaseEndedProgramWindow(current, delayedEffects)) return;
        if (!afterNormalDrawProgramsCompleted &&
            !delayedEffects.HasFlag(DelayedTurnEffects.SkipPlayPhase) &&
            TryBeginAfterNormalDrawProgramWindow(current))
            return;
        if (_programPhaseSchedule?.Phase == TurnPhase.Draw && ResumeScheduledProgramPhase()) return;
        if (delayedEffects.HasFlag(DelayedTurnEffects.SkipPlayPhase))
        {
            BeginDiscardPhase();
            AddLog("DelayedCardEffect", $"{current.Name} 跳过出牌阶段。", current.Seat);
        }
        else
        {
            EnterPlayPhase(current);
        }
        AdvanceRulesAndPublishState();
    }

    private void EnterPlayPhase(CharacterState current)
    {
        // Play-phase limits are distinct from turn-wide effects. Dangxian can
        // create two Play phases in one turn, so Slash count and phase-limited
        // actions reset here while Alcohol's once-per-turn flag remains intact.
        _slashCountThisTurn = 0;
        ResetCardUseDebitPhase();
        _woodenOxUsedThisTurn = false;
        ResetProgramContributionUsesForPlayPhase(current.Seat);
        foreach (var key in _programPhaseUses.Keys.Where(key => key.Seat == current.Seat).ToArray())
            _programPhaseUses.Remove(key);
        _skillRuntimeState.ResetPhase();
        _phase = TurnPhase.Play;
        StartActualDiscardRecoveryPhase(ActualDiscardRecoveryPhaseKind.Play, current.Seat);
        _playPhaseKillCountByCurrentPlayer = 0;
        _playPhaseDamageDealtByCurrentPlayer = 0;
        IssueGrantedEntityPhaseBoundary(current);
        AddLog("PhaseChanged", $"{current.Name} 进入出牌阶段。", current.Seat);
        AdvanceEventRulesAndQueueFact(new PhaseChangedEvent(_phase, current.Seat));
        TryBeginPlayPhaseStartingBoundary(current);
    }

    private static bool IsDelayedCard(CardKind kind) =>
        kind is CardKind.Indulgence or CardKind.SupplyShortage or CardKind.Lightning;

    private static bool IsOrdinaryTrick(CardKind kind) =>
        CardCatalog.Get(kind).CategoryName == "锦囊牌" && !IsDelayedCard(kind);

    private CardKind GetJudgmentEffectiveCardKind(Card card) =>
        _judgmentEffectiveCardKinds.GetValueOrDefault(card.Id, card.Kind);

    private bool HasJudgmentEffectiveCard(CharacterState player, CardKind kind) =>
        GetJudgment(player).Any(card => GetJudgmentEffectiveCardKind(card) == kind);

    private static bool IsDelayedJudgmentContinuation(JudgmentContinuationKind continuation) =>
        continuation is JudgmentContinuationKind.Indulgence or
            JudgmentContinuationKind.SupplyShortage or
            JudgmentContinuationKind.Lightning;

    private static (string Reason, JudgmentContinuationKind Continuation) GetDelayedJudgmentInfo(
        CardKind kind) => kind switch
        {
            CardKind.Indulgence => (JudgmentReasons.Indulgence, JudgmentContinuationKind.Indulgence),
            CardKind.SupplyShortage => (JudgmentReasons.SupplyShortage, JudgmentContinuationKind.SupplyShortage),
            CardKind.Lightning => (JudgmentReasons.Lightning, JudgmentContinuationKind.Lightning),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The card is not a delayed judgment card.")
        };

    private static DelayedTurnEffects GetDelayedTurnEffects(CardKind kind) => kind switch
    {
        CardKind.Indulgence => DelayedTurnEffects.SkipPlayPhase,
        CardKind.SupplyShortage => DelayedTurnEffects.SkipDrawPhase,
        CardKind.Lightning => DelayedTurnEffects.None,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The card is not a delayed turn card.")
    };

    private void RunOneSetupStep()
    {
        if (_selectionIndex >= _selectionOrder.Count)
        {
            var factionPlayer = _players
                .Where(RequiresGodFactionSelection)
                .OrderBy(player => player.Seat)
                .FirstOrDefault();
            if (factionPlayer is not null)
            {
                AdvanceEventRulesAndQueueFact(new GodFactionSelectionRequestedEvent(
                    factionPlayer.Seat,
                    GodFactionChoices));
                if (factionPlayer.IsHuman)
                {
                    RequestHumanGodFactionSelection(factionPlayer);
                }
                else
                {
                    ApplyGodFactionSelection(factionPlayer, ChooseAutomaticGodFaction(factionPlayer));
                    AdvanceRulesAndPublishState();
                }
                return;
            }

            CompleteSetup();
            return;
        }

        var seat = _selectionOrder[_selectionIndex];
        var player = _players[seat];
        var candidates = GetAvailableGeneralCandidates(player);
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                $"Mode '{_modeDefinition.Id}' ran out of generals during setup.");
        }

        AdvanceEventRulesAndQueueFact(new GeneralSelectionRequestedEvent(
            player.Seat,
            candidates.Select(general => general.Id).ToArray()));

        if (player.IsHuman)
        {
            RequestHumanGeneralSelection(player, candidates);
            return;
        }

        var view = CreateSnapshot(player.Seat);
        var (general, thought) = _aiBrains[player.Seat].ChooseGeneral(
            view,
            view.Players.Single(snapshot => snapshot.Seat == player.Seat).Role ?? Role.Renegade,
            candidates,
            ++_thoughtSequence);
        AddGeneralThought(thought);
        ApplyGeneralSelection(player, general);
        AdvanceRulesAndPublishState();
    }

    private void RequestHumanGodFactionSelection(CharacterState player)
    {
        var choices = GodFactionChoices
            .Select(factionId => new PromptChoice(
                new ChoiceId($"setup.god-faction.{factionId}"),
                $"选择{GetFactionName(factionId)}势力",
                [],
                [],
                new Dictionary<string, string>
                {
                    ["action"] = "select-god-faction",
                    ["faction-id"] = factionId
                }))
            .ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.SelectFaction,
            player.Seat,
            $"{player.General.Name}为神势力武将。请选择本局归属的魏、蜀、吴、群势力；该选择会影响主公技等势力判定，但不会改写武将的印刷势力。",
            [],
            [])
        {
            PromptId = CreatePromptId(),
            Choices = choices,
            IsPrivate = true
        };
        _status = EngineStatus.AwaitingHumanFactionSelection;
        AdvanceRulesAndPublishState();
    }

    private void ApplyGodFactionSelection(CharacterState player, string factionId)
    {
        if (!RequiresGodFactionSelection(player))
        {
            throw new InvalidOperationException(
                $"Seat {player.Seat} does not currently require a god-faction choice.");
        }
        if (!GodFactionChoices.Contains(factionId, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Faction '{factionId}' is not a legal god-faction choice.");
        }

        player.ChosenFactionId = factionId;
        ClearPendingDecision();
        _status = EngineStatus.Running;
        AddLog(
            "GodFactionSelected",
            $"{player.Name} 为神势力武将 {player.General.Name} 选择了{GetFactionName(factionId)}势力。",
            player.Seat);
        AdvanceEventRulesAndQueueFact(new GodFactionSelectedEvent(player.Seat, factionId));
    }

    private void HumanSelectGodFactionCore(string factionId, bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.SelectFaction);
        var pending = _pendingDecision ??
            throw new InvalidOperationException("There is no god-faction selection prompt.");
        ApplyGodFactionSelection(_players[pending.PlayerSeat], factionId);
        AdvanceRulesAndPublishState();
        if (advanceToHumanBoundary) AdvanceToHumanBoundary();
    }

    private IReadOnlyList<GeneralDefinition> GetAvailableGeneralCandidates(CharacterState player) =>
        _availableGenerals
            .Where(general => !IsNationalWarMode ||
                              string.Equals(general.FactionId, player.NationalFactionId, StringComparison.Ordinal))
            .Take(Math.Min(_modeDefinition.GeneralCandidateCount, _availableGenerals.Count))
            .ToArray();

    private void RequestHumanGeneralSelection(
        CharacterState player,
        IReadOnlyList<GeneralDefinition> candidates)
    {
        var choices = candidates.Select(general =>
        {
            var factionSuffix = IsNationalWarMode
                ? $" · {GetFactionName(general.FactionId ?? string.Empty)}"
                : string.Empty;
            var choice = new PromptChoice(
                new ChoiceId($"setup.general.{general.Id}"),
                $"选择 {general.Name}（{general.SkillSummary}{factionSuffix}）",
                [],
                [],
                new Dictionary<string, string>
                {
                    ["action"] = "select-general",
                    ["general-id"] = general.Id,
                    ["faction-id"] = general.FactionId ?? string.Empty
                });
            choice = choice with { ContentIds = [general.Id] };
            if (!IsNationalWarMode && UsesGeneralBaseHp)
            {
                var identityMaxHp = general.BaseHp + (player.Role == Role.Lord ? 1 : 0);
                choice = choice with
                {
                    Parameters = new Dictionary<string, string>(choice.Parameters, StringComparer.Ordinal)
                    {
                        ["base-hp"] = general.BaseHp.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["identity-max-hp"] = identityMaxHp.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["health-preview"] = (player.Role == Role.Lord
                            ? $"主公体力上限为基础 {general.BaseHp} + 1 = {identityMaxHp}。"
                            : $"身份模式体力上限为 {identityMaxHp}。") +
                            (general.InitialHp is { } initialHp
                                ? $"初始体力为 {initialHp + (player.Role == Role.Lord ? 1 : 0)}/{identityMaxHp}。" : string.Empty)
                    }
                };
            }
            choice = WithNationalHealthPreview(choice, player, general);
            return choice;
        }).ToArray();

        _pendingDecision = new PendingDecision(
            DecisionKind.SelectGeneral,
            player.Seat,
            IsNationalWarMode
                ? player.GeneralSelected
                    ? $"主将 {player.General.Name}（基础体力 {player.General.BaseHp}）· 请选择副将，卡片显示组合后的体力上限。"
                    : $"请选择你的第 {(player.GeneralSelected ? "二" : "一")} 名同势力武将。候选仅对你可见，明置前势力不会公开。"
                : "请选择你的武将。候选仅对你可见，所有人完成后才会公开结果。",
            [],
            [])
        {
            PromptId = CreatePromptId(),
            Choices = choices,
            ValidContentIds = candidates.Select(general => general.Id).ToArray()
        };
        _status = EngineStatus.AwaitingHumanGeneralSelection;
        AdvanceRulesAndPublishState();
    }

    private void ApplyGeneralSelection(CharacterState player, GeneralDefinition general)
    {
        if (!_availableGenerals.Remove(general))
        {
            throw new InvalidOperationException(
                $"General '{general.Id}' is not available for seat {player.Seat}.");
        }

        if (IsNationalWarMode)
        {
            if (string.IsNullOrWhiteSpace(general.FactionId) ||
                !string.Equals(general.FactionId, player.NationalFactionId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"General '{general.Id}' does not belong to seat {player.Seat}'s assigned faction.");
            }

            if (!player.GeneralSelected)
            {
                player.General = general;
                player.GeneralSelected = true;
                AdvanceEventRulesAndQueueFact(new GeneralSelectedEvent(player.Seat, general.Id));
            }
            else if (!player.SecondaryGeneralSelected)
            {
                player.SecondaryGeneral = general;
                player.SecondaryGeneralSelected = true;
                AdvanceEventRulesAndQueueFact(new SecondaryGeneralSelectedEvent(player.Seat, general.Id));
            }
            else
            {
                throw new InvalidOperationException($"Seat {player.Seat} already selected both generals.");
            }
        }
        else
        {
            player.General = general;
            player.GeneralSelected = true;
            player.GeneralRevealed = false;
            if (UsesGeneralBaseHp)
            {
                player.MaxHp = general.BaseHp + (player.Role == Role.Lord ? 1 : 0);
                player.Hp = general.InitialHp is { } initialHp
                    ? initialHp + (player.Role == Role.Lord ? 1 : 0) : player.MaxHp;
            }
            AddLog("GeneralSelected", $"座位 {player.Seat + 1} 完成私有选将。", player.Seat);
            AdvanceEventRulesAndQueueFact(new GeneralSelectedEvent(player.Seat, general.Id));
        }
        _selectionIndex++;
        ClearPendingDecision();
    }

    private void CompleteSetup()
    {
        if (_setupComplete)
        {
            return;
        }

        if (_selectionIndex != _selectionOrder.Count ||
            _players.Any(player => !player.GeneralSelected ||
                                  IsNationalWarMode && !player.SecondaryGeneralSelected ||
                                  RequiresGodFactionSelection(player)))
        {
            throw new InvalidOperationException("Setup cannot complete before every seat selects a general.");
        }

        InitializeNationalHealth();
        _cardZones.Shuffle(CardLocation.DrawPile, _random);
        DealInitialHands();
        foreach (var player in _players.Where(player => !IsNationalWarMode))
        {
            player.GeneralRevealed = true;
        }
        InitializeStructuredConversionSkills();

        _setupComplete = true;
        _status = EngineStatus.Running;
        AddLog(
            "SetupCompleted",
            IsNationalWarMode
                ? "国战选将完成，双将与势力保持暗置；出牌阶段或杀响应时可按需明置一名武将。"
                : $"选将完成并公开：{string.Join("、", _players.OrderBy(player => player.Seat).Select(player => player.General.Name))}。",
            _currentSeat);
        AdvanceEventRulesAndQueueFact(new GameStartedEvent(_playerCount, _modeDefinition.Id));
        AdvanceEventRulesAndQueueFact(new SetupCompletedEvent(_players.Count, _modeDefinition.Id));
        AdvanceRulesAndPublishState();
    }

    private void RunOneAiPlayDecision(CharacterState player)
    {
        var legalActions = BuildLegalActions(player);
        var view = CreateSnapshot(player.Seat);
        var (action, thought) = _aiBrains[player.Seat].ChoosePlay(view, legalActions, ++_thoughtSequence);
        AddThought(thought);

        if (action.Kind == LegalActionKind.EndPlay)
        {
            CompleteCurrentPlayPhase();
            AdvanceRulesAndPublishState();
            return;
        }

        var activeSkillCards = action.Kind is LegalActionKind.UseEquipmentEffect or LegalActionKind.UseProgramSkill
            ? _aiBrains[player.Seat].ChooseActiveSkillCards(view, action)
            : [];
        var activeSkillTargets = action.Kind is LegalActionKind.UseEquipmentEffect or LegalActionKind.UseProgramSkill
            ? _aiBrains[player.Seat].ChooseActiveSkillTargets(view, action)
            : [];
        ExecuteAction(player, action, activeSkillCards, activeSkillTargets);
        AdvanceRulesAndPublishState();
    }

    private void ExecuteAction(
        CharacterState actor,
        LegalAction action,
        IReadOnlyList<int>? activeSkillCardIds = null,
        IReadOnlyList<int>? activeSkillTargetSeats = null)
    {
        _selectedNextCardTargetSeats = IsTargetAdjustmentAction(actor, action)
            ? action.TargetSeats : null;
        _selectedRedAdditionalTargetSource = action.ProgramActivationId == "red-additional-targets" &&
            _redAdditionalTargetGrants.TryGetValue(actor.Seat, out var redGrant) ? redGrant.Source : null;
        SelectUseConversion(action);
        if (action.Kind == LegalActionKind.UseProgramSkill)
        {
            if (TryExecuteForeignPublicPileSlash(actor, action, activeSkillCardIds ?? [], activeSkillTargetSeats ?? [])) return;
            ExecuteProgramSkill(actor, action, activeSkillCardIds ?? [], activeSkillTargetSeats ?? []);
            return;
        }
        if (action.Kind == LegalActionKind.RevealGeneral)
        {
            RevealNationalGeneral(
                actor,
                action.GeneralSlot ??
                throw new InvalidOperationException("A general reveal action must identify its slot."));
            return;
        }

        if (action.Kind == LegalActionKind.UseEquipmentEffect)
        {
            var cardIds = activeSkillCardIds?.Distinct().ToArray() ?? [];
            var targets = activeSkillTargetSeats?.Distinct().ToArray() ?? [];
            if (cardIds.Length < action.MinCardCount || cardIds.Length > action.MaxCardCount ||
                targets.Length < action.MinTargetCount || targets.Length > action.MaxTargetCount ||
                cardIds.Any(cardId => !action.SelectableCardIds.Contains(cardId)) ||
                targets.Any(target => !action.SelectableTargetSeats.Contains(target)))
            {
                throw new InvalidOperationException("The equipment-effect selection is invalid.");
            }

            if (action.EquipmentKind == CardKind.WoodenOx)
            {
                ResolveWoodenOx(actor, cardIds[0], targets.SingleOrDefault(-1));
            }
            else
            {
                var cards = cardIds.Select(cardId =>
                    GetPlayableCards(actor).Single(card => card.Id == cardId)).ToArray();
                if (targets.Length > 1) ResolveNextActualUseZhangba(actor, targets, cards);
                else ResolveZhangbaSlash(actor, _players[targets[0]], cards);
            }
            return;
        }

        if (TryExecuteTieredRoundZeroPlay(actor, action)) return;
        var card = FindOwnedPlayableCard(actor, action.CardId) ??
            throw new InvalidOperationException("The chosen card is no longer in the actor's playable zones.");
        if (TryBeginCardDeclaration(actor, card, action.PlayedCardKind ?? card.Kind, action.ConversionSource,
            DeclarationReturn(CardDeclarationPurpose.Use, actor.Seat, action), action.TargetSeats.Count > 0 ? action.TargetSeats : action.TargetSeat is {} declaredTarget ? [declaredTarget] : [])) return;
        if (TryExecuteSingleCardTrickConversion(actor, card, action)) return;
        card = ApplyProgramUseAppearance(actor, card, action.ConversionSource);

        switch (action.Kind)
        {
            case LegalActionKind.Recast:
                ResolveRecast(actor, card, action.PlayedCardKind);
                break;
            case LegalActionKind.Slash:
                if (action.TargetSeat is null)
                {
                    throw new InvalidOperationException("Slash requires a target.");
                }

                if (action.TargetSeats.Count > 1)
                {
                    ResolveFangtianHalberdSlash(
                        actor,
                        action.TargetSeats.Select(seat => _players[seat]).ToArray(),
                        card,
                        action.PlayedCardKind ?? card.Kind,
                        action.ConversionSource,
                        action.AdditionalConversionSources);
                }
                else
                {
                    ResolveSlash(
                        actor,
                        _players[action.TargetSeat.Value],
                        card,
                        action.PlayedCardKind ?? card.Kind,
                        action.ConversionSource,
                        action.AdditionalConversionSources);
                }
                break;
            case LegalActionKind.Peach:
                ResolvePeach(actor, actor, card, allowDying: false, conversionSource: action.ConversionSource);
                break;
            case LegalActionKind.Duel:
                if (action.TargetSeat is null)
                {
                    throw new InvalidOperationException("Duel requires a target.");
                }

                ResolveDuel(actor, _players[action.TargetSeat.Value], card, action.PlayedCardKind);
                break;
            case LegalActionKind.DrawTwo:
                ResolveDrawTwo(actor, card);
                break;
            case LegalActionKind.Alcohol:
                ResolveAlcohol(actor, card, action.ConversionSource);
                break;
            case LegalActionKind.Equip:
                ResolveEquip(actor, card);
                break;
            case LegalActionKind.BarbarianAssault:
                ResolveGroupCard(actor, card, LegalActionKind.BarbarianAssault, CardKind.Slash);
                break;
            case LegalActionKind.ArrowBarrage:
                ResolveGroupCard(actor, card, LegalActionKind.ArrowBarrage, CardKind.Dodge);
                break;
            case LegalActionKind.PeachGarden:
                ResolvePeachGarden(actor, card);
                break;
            case LegalActionKind.FiveGrains:
                ResolveFiveGrains(actor, card);
                break;
            case LegalActionKind.IronChain:
                if (action.TargetSeats.Count < 1 || action.TargetSeats.Count >
                    GetProgramIronChainTargetLimit(actor) +
                    (IsTargetAdjustmentAction(actor, action) ? GetAdditionalTargetAdjustmentLimit(actor, action) : 0))
                {
                    throw new InvalidOperationException("IronChain requires an exact target list within its current limit.");
                }

                ResolveIronChain(actor, card, action.TargetSeats, action.PlayedCardKind);
                break;
            case LegalActionKind.Indulgence:
            case LegalActionKind.SupplyShortage:
            case LegalActionKind.Lightning:
                if (action.TargetSeat is null)
                {
                    throw new InvalidOperationException($"{action.Kind} requires a target.");
                }

                ResolveDelayedCard(
                    actor,
                    _players[action.TargetSeat.Value],
                    card,
                    action.Kind,
                    action.PlayedCardKind);
                break;
            case LegalActionKind.Dismantlement:
                if (action.TargetSeat is null)
                {
                    throw new InvalidOperationException("Dismantlement requires a target.");
                }

                ResolveDismantlement(
                    actor,
                    _players[action.TargetSeat.Value],
                    card,
                    action.TargetCardId,
                    action.PlayedCardKind);
                break;
            case LegalActionKind.Snatch:
                if (action.TargetSeat is null)
                {
                    throw new InvalidOperationException("Snatch requires a target.");
                }

                ResolveSnatch(
                    actor,
                    _players[action.TargetSeat.Value],
                    card,
                    action.TargetCardId,
                    action.PlayedCardKind);
                break;
            case LegalActionKind.FireAttack:
                if (action.TargetSeat is null)
                {
                    throw new InvalidOperationException("FireAttack requires a target.");
                }

                ResolveFireAttack(actor, _players[action.TargetSeat.Value], card, action.PlayedCardKind);
                break;
            case LegalActionKind.BorrowedSword:
                if (action.TargetSeats.Count is not (2 or 4) &&
                    !(action.TargetSeats.Count == 6 && action.ProgramActivationId == "red-additional-targets" &&
                      HasRedAdditionalTargets(actor) && GetAdditionalTargetAdjustmentLimit(actor, action) == 2))
                {
                    throw new InvalidOperationException("Borrowed Sword requires an ordered weapon owner and Slash target.");
                }

                ResolveBorrowedSword(
                    actor,
                    _players[action.TargetSeats[0]],
                    _players[action.TargetSeats[1]],
                    card);
                break;
            default:
                throw new InvalidOperationException($"Unsupported action {action.Kind}.");
        }
    }

    private void RevealNationalGeneral(CharacterState actor, GeneralSelectionSlot slot)
    {
        if (!IsNationalWarMode)
        {
            throw new InvalidOperationException("General reveal is only available in national-war lite mode.");
        }

        var general = slot switch
        {
            GeneralSelectionSlot.Primary => actor.GeneralSelected && !actor.GeneralRevealed
                ? actor.General
                : null,
            GeneralSelectionSlot.Secondary => actor.SecondaryGeneralSelected &&
                                              !actor.SecondaryGeneralRevealed
                ? actor.SecondaryGeneral
                : null,
            _ => null
        };
        if (general is null)
        {
            throw new InvalidOperationException("The selected national general is already public or unavailable.");
        }

        if (slot == GeneralSelectionSlot.Primary)
        {
            actor.GeneralRevealed = true;
        }
        else
        {
            actor.SecondaryGeneralRevealed = true;
        }

        var firstFactionReveal = !actor.FactionRevealed;
        actor.FactionRevealed = true;
        AdvanceEventRulesAndQueueFact(new NationalGeneralRevealedEvent(actor.Seat, slot, general.Id));
        if (firstFactionReveal)
        {
            AdvanceEventRulesAndQueueFact(new NationalFactionRevealedEvent(actor.Seat, actor.NationalFactionId!));
        }

        AddLog(
            "NationalGeneralRevealed",
            $"{actor.Name} 明置{GetFactionName(actor.NationalFactionId!)}势力的【{general.Name}】。",
            actor.Seat);
    }

    private void RevealNationalInformation(CharacterState player)
    {
        if (!IsNationalWarMode || player.NationalFactionId is null)
        {
            return;
        }

        if (player.GeneralSelected && !player.GeneralRevealed)
        {
            player.GeneralRevealed = true;
            AdvanceEventRulesAndQueueFact(new NationalGeneralRevealedEvent(
                player.Seat,
                GeneralSelectionSlot.Primary,
                player.General.Id));
        }

        if (player.SecondaryGeneralSelected &&
            player.SecondaryGeneral is { } secondary &&
            !player.SecondaryGeneralRevealed)
        {
            player.SecondaryGeneralRevealed = true;
            AdvanceEventRulesAndQueueFact(new NationalGeneralRevealedEvent(
                player.Seat,
                GeneralSelectionSlot.Secondary,
                secondary.Id));
        }

        if (!player.FactionRevealed)
        {
            player.FactionRevealed = true;
            AdvanceEventRulesAndQueueFact(new NationalFactionRevealedEvent(player.Seat, player.NationalFactionId));
        }
    }

    private void ResolveEquip(CharacterState source, Card equipment)
    {
        var stillLegal = BuildLegalActions(source).Any(action =>
            action.Kind == LegalActionKind.Equip &&
            action.CardId == equipment.Id &&
            action.TargetSeat is null);
        if (!stillLegal)
        {
            throw new InvalidOperationException("The equipment card became illegal before resolution.");
        }

        var resolutionId = BeginCardUse(equipment, source.Seat, []);
        MoveCard(
            equipment,
            FindOwnedCardLocation(source, equipment),
            CardLocation.Processing,
            CardMoveReasons.EquipmentUse);
        if (!TryBeginEquipmentTargetPrograms(source, equipment, resolutionId))
            CompleteEquipmentUse(source, equipment, resolutionId);
    }

    private void CompleteEquipmentUse(CharacterState source, Card equipment, long resolutionId)
    {
        if (source.EquipmentSlotCapacity(EquipmentCatalog.Get(equipment.Kind).Slot) == 0)
        {
            MoveCard(equipment, CardLocation.Processing, CardLocation.DiscardPile,
                new CardMoveReason("area.abolished.equipment"));
            BeginSimpleCardUse(resolutionId, new(equipment.Id, SimpleCardUseEffect.Equipment));
            return;
        }
        var definition = EquipmentCatalog.Get(equipment.Kind);
        var sameSlot = GetEquipment(source).Where(card => EquipmentCatalog.Get(card.Kind).Slot == definition.Slot).ToArray();
        var replaced = sameSlot.Length >= source.EquipmentSlotCapacity(definition.Slot) ? sameSlot.FirstOrDefault() : null;
        if (replaced is not null)
        {
            CopyFirstReplacedWeapon(equipment, replaced);
            MoveCard(
                replaced,
                CardLocation.Equipment(source.Seat),
                CardLocation.DiscardPile,
                CardMoveReasons.EquipmentReplace);
        }

        if (TryPauseRecoveryPaidCardUse(resolutionId, new(RecoveryPaidCardUseKind.EquipmentUse, source.Seat, equipment.Id,
            ReplacedCardId: replaced?.Id, EquipmentSlot: definition.Slot))) return;
        MoveCard(
            equipment,
            CardLocation.Processing,
            CardLocation.Equipment(source.Seat),
            CardMoveReasons.EquipmentEnter);
        AdvanceEventRulesAndQueueFact(new EquipmentChangedEvent(
            resolutionId,
            source.Seat,
            definition.Slot,
            equipment.Id,
            equipment.Kind,
            replaced?.Id));
        AddLog(
            "EquipmentChanged",
            replaced is null
                ? $"{source.Name} 装备【{definition.DisplayName}】。"
                : $"{source.Name} 装备【{definition.DisplayName}】，替换并弃置原有{EquipmentCatalog.GetSlotName(definition.Slot)}。",
            source.Seat);
        BeginSimpleCardUse(resolutionId, new(equipment.Id, SimpleCardUseEffect.Equipment));
    }

    private void BeginJizhiOrNullificationWindow(
        long resolutionId,
        Card effectCard,
        int sourceSeat,
        IReadOnlyList<int> targetSeats,
        LegalActionKind actionKind,
        int? targetCardId = null,
        CardKind? requiredCardKind = null,
        CardKind? playedCardKind = null)
    {
        if (TryPauseRecoveryPaidCardUse(resolutionId, new(RecoveryPaidCardUseKind.Trick, sourceSeat,
            effectCard.Id, targetSeats, actionKind, targetCardId, requiredCardKind, playedCardKind))) return;
        var effectiveCardKind = playedCardKind ?? effectCard.Kind;
        if ((LifecycleCardUse(resolutionId)?.TargetsAdjusted == true))
        {
            var adjustedUse = _resolutionStack.OfType<CardUseFrame>().Single(frame => frame.Id == resolutionId);
            targetSeats = actionKind == LegalActionKind.BorrowedSword
                ? adjustedUse.TargetSeats.Skip(adjustedUse.TargetIndex).Take(2).ToArray() : adjustedUse.TargetSeats;
        }
        if (TryBeginCommittedCardUse(resolutionId, ProgramCardContinuation.CommittedTrick,
                trick: new(effectCard.Id, actionKind, targetCardId, requiredCardKind))) return;
        var pending = new JizhiResolution(
            sourceSeat,
            resolutionId,
            effectCard,
            effectiveCardKind,
            targetSeats,
            actionKind,
            targetCardId,
            requiredCardKind);
        if (TryBeginFinalizedTrickPrograms(pending)) return;
        if (TryBeginProgramCardUseBeforeTargetEffects(pending))
        {
            return;
        }
        ContinueJizhiOrNullificationAfterTargetTriggers(pending);
    }

    private void ContinueJizhiOrNullificationAfterTargetTriggers(JizhiResolution pending)
    {
        BeginNullificationWindow(
            pending.ResolutionId,
            pending.Card,
            pending.PlayerSeat,
            pending.TargetSeats,
            pending.ActionKind,
            pending.TargetCardId,
            pending.RequiredCardKind,
            pending.EffectiveCardKind);
    }

    private void BeginNullificationWindow(
        long resolutionId,
        Card effectCard,
        int sourceSeat,
        IReadOnlyList<int> targetSeats,
        LegalActionKind actionKind,
        int? targetCardId = null,
        CardKind? requiredCardKind = null,
        CardKind? playedCardKind = null)
    {
        targetSeats = FreezeSequentialTrickTarget(resolutionId, targetSeats, actionKind, targetCardId, requiredCardKind);
        if (IsYingboUnrespondable(resolutionId) || IsIssuedCardUnrespondable(resolutionId))
        {
            SetCardUseStep(resolutionId, ResolutionFrameStep.ResolvingEffect);
            ResolveNullifiableEffect(new NullificationWindowFrame(
                0, resolutionId, sourceSeat, Array.AsReadOnly(targetSeats.ToArray()),
                effectCard.Id, playedCardKind ?? effectCard.Kind, actionKind,
                targetCardId, requiredCardKind, []));
            return;
        }

        if (ActiveNullificationWindow is not null)
        {
            throw new InvalidOperationException("The engine cannot open two Nullification windows at once.");
        }

        var candidates = BuildNullificationCandidateSeats(sourceSeat);
        SetCardUseStep(resolutionId, ResolutionFrameStep.AwaitingResponse);
        var pending = new NullificationWindowFrame(
            ++_resolutionSequence,
            resolutionId,
            sourceSeat,
            Array.AsReadOnly(targetSeats.ToArray()),
            effectCard.Id,
            playedCardKind ?? effectCard.Kind,
            actionKind,
            targetCardId,
            requiredCardKind,
            candidates);
        PushRuntimeFrame(pending);
        ContinueNullificationWindow(pending);
    }

    private IReadOnlyList<int> BuildNullificationCandidateSeats(int startAfterSeat)
    {
        return Array.AsReadOnly(Enumerable.Range(1, _playerCount)
            .Select(offset => _players[(startAfterSeat + offset) % _playerCount])
            .Where(player => player.IsAlive)
            .Select(player => player.Seat)
            .ToArray());
    }

    private void ContinueNullificationWindow(NullificationWindowFrame pending)
    {
        if (TryFinishUnrespondableCounterspell(pending)) return;
        if (pending.IssuedNoResponseNode is {} issuedNode)
        {
            if (issuedNode.ChainDepth != pending.ChainDepth || issuedNode.Policy.ParentFrameId != pending.Id || issuedNode.Policy.CardActionId != issuedNode.ActionId)
                throw new InvalidOperationException("Issued counterspell node lost its exact typed parent.");
            FinishNullificationWindow(pending);
            return;
        }
        if (_resolutionStack.LastOrDefault() is not NullificationWindowFrame frame ||
            frame.Id != pending.Id)
        {
            throw new InvalidOperationException("The Nullification window is not the current resolution frame.");
        }
        pending = frame;

        while (pending.CandidateIndex < pending.CandidateSeats.Count)
        {
            var responder = _players[pending.CandidateSeats[pending.CandidateIndex]];
            var cards = pending.ChainDepth == 0 &&
                        IsNearbyTargetResponseProhibited(pending.SourceSeat, responder.Seat,
                            pending.EffectCardKind, pending.TargetSeats)
                ? []
                : GetNullificationCards(responder);
            if (cards.Count == 0 && TieredRoundZeroResponseChoices(responder, CardKind.Nullification).Count == 0)
            {
                pending = ReplaceNullificationWindowFrame(pending with
                {
                    CandidateIndex = pending.CandidateIndex + 1,
                    Step = ResolutionFrameStep.AwaitingResponse
                });
                continue;
            }

            var effectName = CardCatalog.Get(pending.EffectCardKind).DisplayName;
            _pendingDecision = new PendingDecision(
                DecisionKind.Nullification,
                responder.Seat,
                pending.EffectNullified
                    ? $"上一张【无懈可击】使【{effectName}】暂时失效，是否继续使用【无懈可击】？"
                    : $"{_players[pending.SourceSeat].Name} 使用了【{effectName}】，是否使用【无懈可击】？",
                cards.Select(card => card.Id).ToArray(),
                [],
                pending.SourceSeat,
                pending.EffectCardKind)
            {
                PromptId = CreatePromptId(),
                Choices = CreateNullificationChoices(pending, responder, cards),
                TargetSeat = GetNullificationTargetSeat(pending),
                RequiredCardKind = CardKind.Nullification
            };
            AdvanceEventRulesAndQueueFact(new NullificationRequestedEvent(
                pending.ParentFrameId,
                pending.EffectCardId,
                pending.EffectCardKind,
                pending.SourceSeat,
                responder.Seat,
                pending.EffectNullified,
                pending.ChainDepth));
            AddLog(
                "NullificationRequested",
                pending.EffectNullified
                    ? $"{responder.Name} 可以继续使用【无懈可击】恢复【{effectName}】效果。"
                    : $"{responder.Name} 可以使用【无懈可击】响应【{effectName}】。",
                pending.SourceSeat,
                responder.Seat);
            pending = ReplaceNullificationWindowFrame(pending with { Step = ResolutionFrameStep.AwaitingResponse });
            _status = responder.IsHuman
                ? EngineStatus.AwaitingHumanResponse
                : EngineStatus.Running;
            return;
        }

        FinishNullificationWindow(pending);
    }

    private IReadOnlyList<PromptChoice> CreateNullificationChoices(
        NullificationWindowFrame pending,
        CharacterState responder,
        IReadOnlyList<Card> cards)
    {
        var effectName = CardCatalog.Get(pending.EffectCardKind).DisplayName;
        var choices = cards.Where(card => card.Kind == CardKind.Nullification ||
                GetProgramViewAsConversions(responder, card, CardKind.Nullification, true).Count > 0)
            .SelectMany(card => CreateConversionChoiceVariants(
                responder, card, CardKind.Nullification, forResponse: true,
                $"nullification.card-{card.Id}.depth-{pending.ChainDepth}",
                pending.EffectNullified
                    ? $"使用【无懈可击】恢复【{effectName}】效果。"
                    : $"使用【无懈可击】使【{effectName}】失效。",
                [card.Id],
                [],
                new Dictionary<string, string>
                {
                    ["response"] = "nullification",
                    ["effect-card-id"] = pending.EffectCardId.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    ["chain-depth"] = pending.ChainDepth.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                }))
            .ToList();
        choices.AddRange(ExtendedViewAsResponseChoices(responder, CardKind.Nullification));
        choices.AddRange(TieredRoundZeroResponseChoices(responder, CardKind.Nullification));
        choices.Add(new PromptChoice(
            new ChoiceId($"nullification.pass.depth-{pending.ChainDepth}"),
            "不使用【无懈可击】。",
            [],
            [],
            new Dictionary<string, string>
            {
                ["response"] = "pass"
            }));
        return choices;
    }

    private IReadOnlyList<Card> GetNullificationCards(CharacterState responder) =>
        GetPlayableCards(responder).Concat(GetEquipment(responder)).Where(card =>
            !IsCardUseForbidden(responder.Seat, CardKind.Nullification, CardActionType.Use) &&
            !IsTurnHandCardRestricted(responder, card) &&
            !HasProgramCardIdentity(responder, card) &&
            (card.Kind == CardKind.Nullification ||
             GetProgramViewAsConversions(responder, card, CardKind.Nullification,
                 forResponse: true).Count != 0)).Concat(GetProgramMultiCardViewAsSelections(responder, CardKind.Nullification, true)
                    .Select(selection => selection.Cards[0])).DistinctBy(card => card.Id).ToArray();

    private static CardConversionSource? ReadPublishedNullificationSource(PromptChoice choice)
    {
        if (choice.Parameters.ContainsKey("conversion-skill-id") &&
            !TryReadConversionSource(choice.Parameters, out var source))
        {
            throw new InvalidOperationException("The Nullification choice has an invalid conversion source.");
        }
        _ = TryReadConversionSource(choice.Parameters, out var selected);
        return selected;
    }

    private void ValidateNullificationConversion(
        CharacterState responder, Card card, CardConversionSource? selectedSource)
    {
        var candidates = GetProgramViewAsConversions(responder, card, CardKind.Nullification,
            forResponse: true);
        if (selectedSource is not null && candidates.Contains(selectedSource)) return;
        if (selectedSource is null && card.Kind == CardKind.Nullification &&
            !HasProgramCardIdentity(responder, card)) return;
        throw new InvalidOperationException("The selected Nullification conversion is no longer legal.");
    }

    private void ResolveNullificationChoice(
        NullificationWindowFrame pending,
        CharacterState responder,
        Card? selectedCard,
        CardConversionSource? selectedConversionSource = null)
    {
        if (selectedCard is {} declaredCounter && TryBeginCardDeclaration(responder, declaredCounter, CardKind.Nullification,
            PeekDeclarationConversion(responder, declaredCounter, CardKind.Nullification, false, selectedConversionSource),
            DeclarationReturn(CardDeclarationPurpose.Counterspell, responder.Seat), pending.TargetSeats)) return;
        if (!ReferenceEquals(_resolutionStack.LastOrDefault(), pending) ||
            pending.CandidateIndex >= pending.CandidateSeats.Count ||
            pending.CandidateSeats[pending.CandidateIndex] != responder.Seat)
        {
            throw new InvalidOperationException("The Nullification response is not for the current responder.");
        }

        if (selectedCard is null)
        {
            pending = ReplaceNullificationWindowFrame(pending with
            {
                CandidateIndex = pending.CandidateIndex + 1,
                Step = ResolutionFrameStep.AwaitingResponse
            });
            ContinueNullificationWindow(pending);
            return;
        }

        if (selectedConversionSource is null && selectedCard.Kind != CardKind.Nullification &&
            GetProgramViewAsConversions(responder, selectedCard, CardKind.Nullification, true).Count == 0 &&
            GetProgramMultiCardViewAsSelections(responder, CardKind.Nullification, true)
                .FirstOrDefault(selection => selection.Cards[0].Id == selectedCard.Id) is { } extended)
        {
            ResolveExtendedViewAsResponse(responder, extended);
            return;
        }
        var card = GetNullificationCards(responder).SingleOrDefault(candidate =>
            candidate.Id == selectedCard.Id);
        if (card is null)
        {
            throw new InvalidOperationException("The selected Nullification card is not in the responder's hand.");
        }
        ValidateNullificationConversion(responder, card, selectedConversionSource);
        if (selectedConversionSource is not null && ViewAsRule(selectedConversionSource) is { } usageRule &&
            (usageRule.TieredRoundConversion is not null || usageRule.UnusedOutputNameThisGame || usageRule.ConversionStateId is not null && usageRule.UsesPerPhase is not null)) ConsumeProgramViewAsUsage([selectedConversionSource]);

        var unrespondableSource = FreezeUnrespondableCounterspellSource(responder, [card.Id]);
        var responseFrom = FindOwnedCardLocation(responder, card);
        var responseCostIsRed = CapturePhysicalCardColor(responder.Seat,card);
        var completedResponseUseSuit = FreezeCompletedResponseUseSuit(responder, [card], CardKind.Nullification);
        MoveCard(
            card,
            responseFrom,
            CardLocation.Processing,
            CardMoveReasons.Nullification);
        MoveCard(
            card,
            CardLocation.Processing,
            CardLocation.DiscardPile,
            CardMoveReasons.NullificationFinished);
        pending = (NullificationWindowFrame)_resolutionStack.Single(frame => frame.Id == pending.Id);
        pending = ReplaceNullificationWindowFrame(pending with
        {
            EffectNullified = !pending.EffectNullified,
            ChainDepth = pending.ChainDepth + 1,
            CandidateSeats = BuildNullificationCandidateSeats(responder.Seat),
            CandidateIndex = 0,
            Step = ResolutionFrameStep.AwaitingResponse
        });
        AdvanceEventRulesAndQueueFact(new CardRespondedEvent(
            card.Id,
            responder.Seat,
            pending.SourceSeat,
            CardKind.Nullification));
        AdvanceEventRulesAndQueueFact(new NullificationRespondedEvent(
            pending.ParentFrameId,
            pending.EffectCardId,
            pending.EffectCardKind,
            responder.Seat,
            card.Id,
            pending.EffectNullified,
            pending.ChainDepth));
        AddLog(
            "NullificationResponded",
            pending.EffectNullified
                ? $"{responder.Name} 使用【无懈可击】，使【{CardCatalog.Get(pending.EffectCardKind).DisplayName}】失效。"
                : $"{responder.Name} 使用【无懈可击】，使【{CardCatalog.Get(pending.EffectCardKind).DisplayName}】恢复。",
            responder.Seat,
            pending.SourceSeat);
        var parentActionId = _resolutionStack.OfType<CardUseFrame>()
            .LastOrDefault(frame => frame.Id == pending.ParentFrameId)?.Action?.ActionId;
        var responseAction = CaptureFactionAction(new CardActionContext(++_cardActionSequence, parentActionId,
            CardActionType.Response, responder.Seat, responder.Seat, null, responder.Seat,
            pending.SourceSeat, CardKind.Nullification, [],
            [new CardActionCost(card.Id, card.Kind, responseFrom,responseCostIsRed)],
            selectedConversionSource is null ? [] : [selectedConversionSource], effectiveSuit: completedResponseUseSuit,effectiveIsRed:TracksActionDiscardColor?SuitColor(completedResponseUseSuit) ?? responseCostIsRed:null));
        TryIssueTieredRoundResponse(pending, responseAction);
        RecordActualPlayPhaseUse(responseAction);
        AdvanceEventRulesAndQueueFact(new CardActionAcceptedEvent(responseAction));
        if (TryBeginPolicyCounterspellPayment(pending, responseAction, unrespondableSource, [card.Id])) return;
        if (TryBeginCommittedResponseUsePrograms(null, responseAction, ProgramCardContinuation.NullificationResponse)) return;
        if (TryBeginProgramCardWindow(null, responseAction,
                SkillProgramTriggerWindow.CardResponseAccepted, [],
                ProgramCardContinuation.NullificationResponse)) return;
        ContinueNullificationAfterResponseUse(responseAction);
    }

    private void FinishNullificationWindow(NullificationWindowFrame pending)
    {
        if (_resolutionStack.LastOrDefault() is not NullificationWindowFrame frame ||
            frame.Id != pending.Id)
        {
            throw new InvalidOperationException("The Nullification window cannot finish from its current frame.");
        }

        pending = ReplaceNullificationWindowFrame(frame with { Step = ResolutionFrameStep.Completed });
        PopResolutionFrame(pending.Id, ResolutionFrameKind.NullificationWindow);
        AdvanceEventRulesAndQueueFact(new NullificationResolvedEvent(
            pending.ParentFrameId,
            pending.EffectCardId,
            pending.EffectCardKind,
            pending.EffectNullified,
            pending.ChainDepth));
        ClearPendingDecision();
        SetCardUseStep(pending.ParentFrameId, ResolutionFrameStep.ResolvingEffect);
        if (pending.EffectNullified && !HasCurrentCardEnhancement(pending.ParentFrameId, CurrentCardEnhancement.Uncancelable))
        {
            foreach (var physicalCard in GetCardUsePhysicalCards(pending.ParentFrameId))
            {
                MoveFinishedTrickCard(pending.ParentFrameId, physicalCard);
            }
            AddLog(
                "CardEffect",
                $"【{CardCatalog.Get(pending.EffectCardKind).DisplayName}】的效果被无懈。",
                pending.SourceSeat);
            FinishCardUse(pending.ParentFrameId, GetNullificationEffectCard(pending), pending.EffectCardKind);
            return;
        }

        ResolveNullifiableEffect(pending);
    }

    private void ResolveNullifiableEffect(NullificationWindowFrame pending)
    {
        if (pending.TargetSeats.Count == 1 &&
            IsCardEffectIneffective(pending.ParentFrameId, pending.TargetSeats[0]))
        {
            CompleteIneffectiveTrickTarget(pending, pending.TargetSeats[0]);
            return;
        }

        if (TryBeginOrdinaryCardEffectPrograms(pending)) return;
        ResolveNullifiableEffectCore(pending);
    }

    private void ResolveNullifiableEffectCore(NullificationWindowFrame pending)
    {
        switch (pending.ActionKind)
        {
            case LegalActionKind.DrawTwo:
                ResolveDrawTwoEffect(pending);
                break;
            case LegalActionKind.BarbarianAssault:
            case LegalActionKind.ArrowBarrage:
                ResolveGroupCardEffect(pending);
                break;
            case LegalActionKind.PeachGarden:
                ResolvePeachGardenEffect(pending);
                break;
            case LegalActionKind.FiveGrains:
                ResolveFiveGrainsEffect(pending);
                break;
            case LegalActionKind.Dismantlement:
            case LegalActionKind.Snatch:
                ResolveTargetCardEffect(pending);
                break;
            case LegalActionKind.FireAttack:
                ResolveFireAttackEffect(pending);
                break;
            case LegalActionKind.BorrowedSword:
                ResolveBorrowedSwordEffect(pending);
                break;
            case LegalActionKind.Duel:
                ResolveDuelEffect(pending);
                break;
            case LegalActionKind.IronChain:
                ResolveIronChainEffect(pending);
                break;
            case LegalActionKind.Indulgence:
            case LegalActionKind.SupplyShortage:
            case LegalActionKind.Lightning:
                ResolveDelayedCardEffect(pending);
                break;
            default:
                throw new InvalidOperationException(
                    $"Card kind {pending.EffectCardKind} cannot continue after Nullification.");
        }
    }

    private void ResolveDrawTwoEffect(NullificationWindowFrame pending)
    {
        var source = _players[GetNullificationTargetSeat(pending) ?? pending.SourceSeat];
        SetCardUseStep(pending.ParentFrameId, ResolutionFrameStep.ResolvingEffect);
        DrawCards(source, 2, log: true);
        MoveFinishedTrickCard(pending.ParentFrameId, GetNullificationEffectCard(pending));
        FinishCardUse(pending.ParentFrameId, GetNullificationEffectCard(pending));
        AddLog("CardEffect", $"{source.Name} 使用【无中生有】，摸两张牌。", source.Seat);
    }

    private void ResolveGroupCardEffect(NullificationWindowFrame pending)
    {
        var source = _players[pending.SourceSeat];
        var requiredCardKind = pending.RequiredCardKind ??
            throw new InvalidOperationException("A group card must retain its response card kind.");
        var physicalCards = GetCardUsePhysicalCards(pending.ParentFrameId);
        var effectiveCard = pending.EffectCardKind == GetNullificationEffectCard(pending).Kind
            ? GetNullificationEffectCard(pending)
            : GetNullificationEffectCard(pending) with { Kind = pending.EffectCardKind };
        var group = new GroupCardHandle(this,
            pending.ParentFrameId,
            source.Seat,
            effectiveCard,
            pending.TargetSeats,
            GroupCardEffect.ResponseAttack,
            requiredCardKind,
            physicalCards);
        if (effectiveCard.Kind == CardKind.BarbarianAssault)
        {
            var mengHuo = _players.FirstOrDefault(player =>
                player.IsAlive && HasCardPolicy(player, SkillProgramCardPolicyKind.AttributeGlobalDamage,
                    CardKind.BarbarianAssault));
            if (mengHuo is not null)
            {
                group.DamageSourceSeat = mengHuo.Seat;
                AdvanceEventRulesAndQueueFact(new HuoshouAttributedEvent(group.ResolutionId, source.Seat, mengHuo.Seat));
            }
        }
        ActiveGroupCard = group;
        AddLog(
            "CardUsed",
            $"{source.Name} 使用【{CardCatalog.Get(pending.EffectCardKind).DisplayName}】，依次攻击 {pending.TargetSeats.Count} 名角色。",
            source.Seat);
        AdvanceEventRulesAndQueueFact(new GroupCardUsedEvent(
            pending.ParentFrameId,
            pending.EffectCardId,
            pending.EffectCardKind,
            source.Seat,
            pending.TargetSeats));
        NotifyAiOfGroupAttack(source, pending.TargetSeats);
        BeginGroupAttackResponse(group);
    }

    private void ResolvePeachGardenEffect(NullificationWindowFrame pending)
    {
        var source = _players[pending.SourceSeat];
        var effectiveCard = pending.EffectCardKind == GetNullificationEffectCard(pending).Kind
            ? GetNullificationEffectCard(pending)
            : GetNullificationEffectCard(pending) with { Kind = pending.EffectCardKind };
        var group = new GroupCardHandle(this,
            pending.ParentFrameId,
            source.Seat,
            effectiveCard,
            pending.TargetSeats,
            GroupCardEffect.Recovery,
            requiredCardKind: null,
            GetCardUsePhysicalCards(pending.ParentFrameId));
        ActiveGroupCard = group;
        AddLog(
            "CardUsed",
            $"{source.Name} 使用【{CardCatalog.Get(pending.EffectCardKind).DisplayName}】，使 {pending.TargetSeats.Count} 名存活角色依次回复体力。",
            source.Seat);
        AdvanceEventRulesAndQueueFact(new GroupCardUsedEvent(
            pending.ParentFrameId,
            pending.EffectCardId,
            pending.EffectCardKind,
            source.Seat,
            pending.TargetSeats));
    }

    private void ResolveFiveGrainsEffect(NullificationWindowFrame pending)
    {
        var source = _players[pending.SourceSeat];
        var effectiveCard = pending.EffectCardKind == GetNullificationEffectCard(pending).Kind
            ? GetNullificationEffectCard(pending)
            : GetNullificationEffectCard(pending) with { Kind = pending.EffectCardKind };
        var group = new GroupCardHandle(this,
            pending.ParentFrameId,
            source.Seat,
            effectiveCard,
            pending.TargetSeats,
            GroupCardEffect.PublicDraft,
            requiredCardKind: null,
            GetCardUsePhysicalCards(pending.ParentFrameId));
        ActiveGroupCard = group;
        foreach (var _ in pending.TargetSeats)
        {
            var revealed = DrawOneToProcessing(CardMoveReasons.Reveal) ??
                throw new InvalidOperationException("FiveGrains could not reveal the promised card count.");
            group.AddRevealedCard(revealed.Id);
        }

        AddLog(
            "CardUsed",
            $"{source.Name} 使用【{CardCatalog.Get(pending.EffectCardKind).DisplayName}】，公开展示 {group.RevealedCardIds.Count} 张牌并依次选取。",
            source.Seat);
        AdvanceEventRulesAndQueueFact(new GroupCardUsedEvent(
            pending.ParentFrameId,
            pending.EffectCardId,
            pending.EffectCardKind,
            source.Seat,
            pending.TargetSeats));
        AdvanceEventRulesAndQueueFact(new CardsRevealedEvent(
            pending.ParentFrameId,
            group.RevealedCardIds
                .Select(cardId => _cardZones.CardsAt(CardLocation.Processing).Single(card => card.Id == cardId))
                .Select(ToSnapshot)
                .ToArray()));
        if (group.TargetSeats.Count == 0)
        {
            FinishFiveGrains(group);
        }
        else
        {
            BeginHarvestSelection(group);
        }
    }

    private void ResolveTargetCardEffect(NullificationWindowFrame pending)
    {
        var targetSeat = GetNullificationTargetSeat(pending) ??
            throw new InvalidOperationException("A target-card effect must retain one target.");
        var target = _players[targetSeat];
        Card targetCard;
        CardLocation targetCardFromZone;
        if (pending.TargetCardId is { } requestedTargetCardId)
        {
            var publicTarget = FindPublicTargetCard(target, requestedTargetCardId);
            if (publicTarget is null)
            {
                SkipUnavailableTargetCardEffect(pending, target, CardEffectSkipReason.PublicTargetMissing);
                return;
            }
            targetCard = publicTarget.Value.Card;
            targetCardFromZone = publicTarget.Value.Location;
        }
        else
        {
            var targetHand = GetHand(target);
            if (targetHand.Count == 0)
            {
                SkipUnavailableTargetCardEffect(pending, target, CardEffectSkipReason.TargetHandEmpty);
                return;
            }

            BeginTargetCardSelection(pending, target, targetHand.Count);
            return;
        }

        ApplyTargetCardEffect(
            pending.ParentFrameId,
            GetNullificationEffectCard(pending),
            pending.EffectCardKind,
            pending.SourceSeat,
            target.Seat,
            pending.ActionKind,
            targetCard,
            targetCardFromZone);
    }

    private void BeginTargetCardSelection(
        NullificationWindowFrame pending,
        CharacterState target,
        int candidateCount)
    {
        if (ActiveTargetCardSelection is not null)
        {
            throw new InvalidOperationException("The engine cannot open two hidden target-card selections at once.");
        }

        var source = _players[pending.SourceSeat];
        var candidateSlots = Enumerable.Range(0, candidateCount).ToArray();
        SetCardUseStep(pending.ParentFrameId, ResolutionFrameStep.AwaitingResponse);
        PushRuntimeFrame(new TargetCardSelectionFrame(
            ++_resolutionSequence,
            pending.ParentFrameId,
            source.Seat,
            target.Seat,
            pending.EffectCardId,
            pending.EffectCardKind,
            pending.ActionKind,
            Array.AsReadOnly(candidateSlots)));
        _pendingDecision = new PendingDecision(
            DecisionKind.SelectTargetCard,
            source.Seat,
            $"{source.Name} 使用【{CardCatalog.Get(pending.EffectCardKind).DisplayName}】，请选择 {target.Name} 的一张暗牌。",
            [],
            [target.Seat],
            source.Seat,
            pending.EffectCardKind)
        {
            PromptId = CreatePromptId(),
            Choices = Array.AsReadOnly(candidateSlots
                .Select(slot => new PromptChoice(
                    new ChoiceId($"target-card.slot-{slot}.resolution-{pending.ParentFrameId}"),
                    $"选择 {target.Name} 的一张暗牌（牌位 {slot + 1}）",
                    [],
                    [target.Seat],
                    new Dictionary<string, string>
                    {
                        ["action"] = "target-card-slot",
                        ["slot-index"] = slot.ToString(
                            System.Globalization.CultureInfo.InvariantCulture)
                    }))
                .ToArray()),
            IsPrivate = true,
            TargetSeat = target.Seat
        };
        AdvanceEventRulesAndQueueFact(new TargetCardSelectionRequestedEvent(
            pending.ParentFrameId,
            source.Seat,
            target.Seat,
            pending.ActionKind,
            candidateCount));
        AddLog(
            "TargetCardSelectionRequested",
            $"{source.Name} 对 {target.Name} 的手牌进行不透明牌位选择。",
            source.Seat,
            target.Seat);
        _status = source.IsHuman
            ? EngineStatus.AwaitingHumanCardSelection
            : EngineStatus.Running;
    }

    private void ResolveTargetCardSelection(
        TargetCardSelectionFrame pending,
        int slot)
    {
        if (!ReferenceEquals(_resolutionStack.LastOrDefault(), pending))
        {
            throw new InvalidOperationException("The hidden target-card selection is not the current resolution frame.");
        }

        var target = _players[pending.TargetSeat];
        var targetHand = GetHand(target);
        if (targetHand.Count != pending.CandidateSlots.Count ||
            slot < 0 ||
            slot >= pending.CandidateSlots.Count)
        {
            throw new InvalidOperationException(
                "The target hand changed while the hidden target-card selection was pending.");
        }

        var targetCard = targetHand[slot];
        var effectCard = GetTrickRepresentation(pending.ParentFrameId, pending.EffectCardId, requireProcessing: true);
        ReplaceRuntimeTop(pending with { Step = ResolutionFrameStep.Completed });
        PopResolutionFrame(pending.Id, ResolutionFrameKind.TargetCardSelection);
        SetCardUseStep(pending.ParentFrameId, ResolutionFrameStep.ResolvingEffect);
        ApplyTargetCardEffect(
            pending.ParentFrameId,
            effectCard,
            pending.EffectCardKind,
            pending.SourceSeat,
            pending.TargetSeat,
            pending.ActionKind,
            targetCard,
            CardLocation.Hand(target.Seat));
    }

    private void ApplyTargetCardEffect(
        long resolutionId,
        Card effectCard,
        CardKind effectiveCardKind,
        int sourceSeat,
        int targetSeat,
        LegalActionKind actionKind,
        Card targetCard,
        CardLocation targetCardFromZone)
    {
        var source = _players[sourceSeat];
        var target = _players[targetSeat];
        var targetMoveReason = GetTargetCardMoveReason(actionKind, targetCardFromZone.Zone);
        if (actionKind == LegalActionKind.Dismantlement && IsForeignEquipmentDiscardPrevented(sourceSeat, targetCard,
                targetCardFromZone, OwnedCardMoveIntent.Discard))
        {
            MoveFinishedTrickCard(resolutionId, effectCard);
            FinishCardUse(resolutionId, effectCard, effectiveCardKind);
            return;
        }
        var publicTargetKind = targetCardFromZone.Zone == CardZoneKind.Judgment
            ? GetJudgmentEffectiveCardKind(targetCard)
            : targetCard.Kind;
        MoveCard(
            targetCard,
            targetCardFromZone,
            CardLocation.Processing,
            targetMoveReason);

        if (actionKind == LegalActionKind.Dismantlement)
        {
            AdvanceEventRulesAndQueueFact(new TargetCardDiscardedEvent(
                resolutionId,
                source.Seat,
                target.Seat,
                targetCardFromZone.Zone,
                IsPublicTargetZone(targetCardFromZone.Zone) ? targetCard.Id : null,
                IsPublicTargetZone(targetCardFromZone.Zone) ? publicTargetKind : null));
            MoveProcessingCardUnlessDestroyed(
                targetCard,
                CardLocation.DiscardPile,
                GetTargetCardFinishedMoveReason(actionKind, targetCardFromZone.Zone));
        }
        else
        {
            AdvanceEventRulesAndQueueFact(new TargetCardTakenEvent(
                resolutionId,
                source.Seat,
                target.Seat,
                targetCardFromZone.Zone,
                IsPublicTargetZone(targetCardFromZone.Zone) ? targetCard.Id : null,
                IsPublicTargetZone(targetCardFromZone.Zone) ? publicTargetKind : null));
            MoveProcessingCardUnlessDestroyed(
                targetCard,
                CardLocation.Hand(source.Seat),
                GetTargetCardFinishedMoveReason(actionKind, targetCardFromZone.Zone));
        }

        MoveFinishedTrickCard(resolutionId, effectCard);
        FinishCardUse(resolutionId, effectCard, effectiveCardKind);

        var displayName = CardCatalog.Get(effectiveCardKind).DisplayName;
        var targetDescription = targetCardFromZone.Zone switch
        {
            CardZoneKind.Equipment => $"装备【{targetCard.DisplayName}】",
            CardZoneKind.Judgment => $"判定区【{CardCatalog.Get(publicTargetKind).DisplayName}】",
            _ => "一张手牌"
        };
        var outcome = actionKind == LegalActionKind.Dismantlement
            ? $"弃置 {target.Name} 的{targetDescription}"
            : $"获得 {target.Name} 的{targetDescription}";
        AddLog(
            "CardEffect",
            IsPublicTargetZone(targetCardFromZone.Zone)
                ? $"{source.Name} 使用【{displayName}】，{outcome}。"
                : $"{source.Name} 使用【{displayName}】，{outcome}（牌面不公开）。",
            source.Seat,
            target.Seat);
    }

    private void ResolveFireAttackEffect(NullificationWindowFrame pending)
    {
        var source = _players[pending.SourceSeat];
        var targetSeat = GetNullificationTargetSeat(pending) ??
            throw new InvalidOperationException("FireAttack must retain one target.");
        if (GetHand(_players[targetSeat]).Count == 0)
        {
            SkipUnavailableTargetCardEffect(pending, _players[targetSeat], CardEffectSkipReason.TargetHandEmpty);
            return;
        }
        if (_resolutionStack.LastOrDefault() is not CardUseFrame cardUse ||
            cardUse.Id != pending.ParentFrameId ||
            cardUse.CardId != pending.EffectCardId ||
            cardUse.CardKind != pending.EffectCardKind ||
            cardUse.SourceSeat != source.Seat ||
            cardUse.TargetIndex >= cardUse.TargetSeats.Count ||
            cardUse.TargetSeats[cardUse.TargetIndex] != targetSeat)
        {
            throw new InvalidOperationException("FireAttack lost its current CardUse target.");
        }
        SetFireAttackSelection(cardUse.Id, new FireAttackSelectionState(null));
        AddLog(
            "CardUsed",
            $"{source.Name} 对 {_players[targetSeat].Name} 使用【火攻】，等待目标展示一张手牌。",
            source.Seat,
            targetSeat);
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(
            pending.EffectCardId,
            pending.EffectCardKind,
            source.Seat,
            targetSeat));
        NotifyAiOfFireAttack(source, _players[targetSeat]);
        BeginFireAttackReveal(ActiveFireAttack ??
            throw new InvalidOperationException("FireAttack lost its reveal selection."));
    }

    private void ResolveBorrowedSwordEffect(NullificationWindowFrame pending)
    {
        if (pending.TargetSeats.Count != 2)
        {
            throw new InvalidOperationException("Borrowed Sword must retain its ordered two targets.");
        }

        var ineffectiveTarget = pending.TargetSeats.FirstOrDefault(
            targetSeat => IsCardEffectIneffective(pending.ParentFrameId, targetSeat),
            -1);
        if (ineffectiveTarget >= 0)
        {
            CompleteIneffectiveTrickTarget(pending, ineffectiveTarget);
            return;
        }

        var resolution = new BorrowedSwordHandle(this,
            pending.ParentFrameId,
            pending.SourceSeat,
            pending.TargetSeats[0],
            pending.TargetSeats[1],
            GetNullificationEffectCard(pending));
        ActiveBorrowedSword = resolution;
        AddLog(
            "CardUsed",
            $"{_players[resolution.SourceSeat].Name} 对 {_players[resolution.WeaponOwnerSeat].Name} 使用【借刀杀人】，指定其攻击 {_players[resolution.SlashTargetSeat].Name}。",
            resolution.SourceSeat,
            resolution.WeaponOwnerSeat);
        BeginBorrowedSwordSlashChoice(resolution, includeFactionSlash: true);
    }

    private void SkipUnavailableTargetCardEffect(NullificationWindowFrame pending, CharacterState target, CardEffectSkipReason reason)
    {
        AdvanceEventRulesAndQueueFact(new CardEffectSkippedEvent(pending.ParentFrameId, pending.SourceSeat, target.Seat, pending.EffectCardKind, reason));
        MoveFinishedTrickCard(pending.ParentFrameId, GetNullificationEffectCard(pending));
        FinishCardUse(pending.ParentFrameId, GetNullificationEffectCard(pending), pending.EffectCardKind);
        var explanation = reason == CardEffectSkipReason.TargetHandEmpty ? "已没有手牌" : "所选的公开牌已不在原处";
        AddLog("CardEffect", $"【{CardCatalog.Get(pending.EffectCardKind).DisplayName}】结算时，{target.Name}{explanation}，本次效果跳过。", pending.SourceSeat, target.Seat);
    }

    private void ResolveDuelEffect(NullificationWindowFrame pending)
    {
        var source = _players[pending.SourceSeat];
        var targetSeat = GetNullificationTargetSeat(pending) ??
            throw new InvalidOperationException("Duel must retain one target.");
        var target = _players[targetSeat];
        var virtualOrigin = LifecycleCardUse(pending.ParentFrameId)?.SelectedActorDuelOrigin;
        if (virtualOrigin is not null)
            UpdateLifecycleCardUse(pending.ParentFrameId, use => use with { SelectedActorDuelOrigin = virtualOrigin with { AttackStarted = true } });
        var obtainOrigin = LifecycleCardUse(pending.ParentFrameId)?.DamageTargetDuelOrigin;
        if (obtainOrigin is not null)
            UpdateLifecycleCardUse(pending.ParentFrameId, use => use with { DamageTargetDuelOrigin = obtainOrigin with { AttackStarted = true } });
        var dualColorOrigin = LifecycleCardUse(pending.ParentFrameId)?.DualColorDuelOrigin;
        if (dualColorOrigin is not null)
            UpdateLifecycleCardUse(pending.ParentFrameId, use => use with { DualColorDuelOrigin = dualColorOrigin with { AttackStarted = true } });
        var conditionalOrigin = LifecycleCardUse(pending.ParentFrameId)?.ConditionalDiscardDuelOrigin;
        if (conditionalOrigin is not null)
            UpdateLifecycleCardUse(pending.ParentFrameId, use => use with { ConditionalDiscardDuelOrigin = conditionalOrigin with { AttackStarted = true } });
        var attack = new CardAttackHandle(this,
            pending.ParentFrameId,
            source.Seat,
            target.Seat,
            virtualOrigin is not null || obtainOrigin is not null || dualColorOrigin is not null || conditionalOrigin is not null || IsTieredRoundZeroUse(pending.ParentFrameId) ? null : GetNullificationEffectCard(pending),
            playedCardKind: pending.EffectCardKind,
            physicalCards: GetCardUsePhysicalCards(pending.ParentFrameId),
            programSkillCardUseFrameId: virtualOrigin?.ParentProgramFrameId ?? obtainOrigin?.ParentProgramFrameId ?? dualColorOrigin?.ParentProgramFrameId ?? conditionalOrigin?.ParentProgramFrameId);
        ActiveCardAttack = attack;
        var duel = new DuelHandle(this, attack);
        ActiveDuel = duel;
        AddLog("CardUsed", $"{source.Name} 对 {target.Name} 使用【决斗】。", source.Seat, target.Seat);
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(
            pending.EffectCardId,
            pending.EffectCardKind,
            source.Seat,
            target.Seat));
        NotifyAiOfDuel(source, target);
        BeginDuelResponse(duel);
    }

    private void ResolveIronChainEffect(NullificationWindowFrame pending)
    {
        SetCardUseStep(pending.ParentFrameId, ResolutionFrameStep.ResolvingEffect);
        var changes = new List<string>(pending.TargetSeats.Count);
        foreach (var targetSeat in pending.TargetSeats)
        {
            var target = _players[targetSeat];
            if (!target.IsAlive || IsCardEffectIneffective(pending.ParentFrameId, targetSeat))
            {
                continue;
            }

            var wasChained = target.IsChained;
            target.IsChained = ResolveEnteringChain(target, HasCardPolicy(target, SkillProgramCardPolicyKind.ForceChained) || !target.IsChained);
            if (!wasChained && target.IsChained)
                RecordCharacterStateChange(target.Seat, SkillProgramTriggerWindow.CharacterEnteredChain, pending.ParentFrameId);
            changes.Add($"{target.Name}{(target.IsChained ? "横置" : "重置")}");
            AdvanceEventRulesAndQueueFact(new IronChainStateChangedEvent(
                pending.ParentFrameId,
                pending.SourceSeat,
                target.Seat,
                target.IsChained));
        }

        AdvanceEventRulesAndQueueFact(new IronChainResolvedEvent(
            pending.ParentFrameId,
            pending.SourceSeat,
            pending.TargetSeats));
        var source = _players[pending.SourceSeat];
        AddLog(
            "CardEffect",
            changes.Count == 0
                ? $"{source.Name} 使用【铁索连环】，但目标已无存活角色。"
                : $"{source.Name} 使用【铁索连环】，" + string.Join("、", changes) + "。",
            source.Seat);
        var finishedIronChain = GetNullificationEffectCard(pending);
        if (!SkipTieredRoundZeroFinishedMovement(pending.ParentFrameId, finishedIronChain))
            MoveCard(finishedIronChain, CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.IronChainFinished);
        FinishCardUse(pending.ParentFrameId, GetNullificationEffectCard(pending));
    }

    private void ResolveDrawTwo(CharacterState source, Card drawTwo)
    {
        var stillLegal = BuildLegalActions(source).Any(action =>
            action.Kind == LegalActionKind.DrawTwo &&
            action.CardId == drawTwo.Id &&
            action.TargetSeat is null);
        if (!stillLegal)
        {
            throw new InvalidOperationException("DrawTwo became illegal before resolution.");
        }

        var resolutionId = BeginCardUse(drawTwo, source.Seat, []);
        MoveCard(
            drawTwo,
            FindOwnedCardLocation(source, drawTwo),
            CardLocation.Processing,
            CardMoveReasons.Use);
        BeginJizhiOrNullificationWindow(
            resolutionId,
            drawTwo,
            source.Seat,
            [],
            LegalActionKind.DrawTwo);
    }

    private void ResolveAlcohol(CharacterState source, Card alcohol, CardConversionSource? conversionSource = null)
    {
        var stillLegal = BuildLegalActions(source).Any(action =>
            action.Kind == LegalActionKind.Alcohol &&
            action.CardId == alcohol.Id &&
            action.ConversionSource == conversionSource &&
            action.TargetSeat is null);
        if (!stillLegal)
        {
            throw new InvalidOperationException("Alcohol became illegal before resolution.");
        }

        ValidateAlcoholConversion(source, alcohol, conversionSource, forResponse: false);
        var resolutionId = BeginCardUse(alcohol, source.Seat, [],
            playedCardKind: CardKind.Alcohol, conversionSource: conversionSource);
        MoveCard(
            alcohol,
            FindOwnedCardLocation(source, alcohol),
            CardLocation.Processing,
            CardMoveReasons.Use);
        // Spend the allowance at commitment, before an optional trigger can pause.
        source.UsedPlayPhaseAlcoholThisTurn = true;
        BeginSimpleCardUse(resolutionId, new(alcohol.Id, SimpleCardUseEffect.Alcohol));
    }

    private void CompleteAlcoholUse(CharacterState source, Card alcohol, long resolutionId)
    {
        var use = _resolutionStack.OfType<CardUseFrame>().Single(frame => frame.Id == resolutionId);
        var target = (LifecycleCardUse(resolutionId)?.AdjustedSimpleContinuation is not null) ? _players[use.TargetSeats[use.TargetIndex]] : source;
        if (target.IsAlive && !IsCardEffectIneffective(resolutionId, target.Seat))
        {
            target.HasAlcoholEffect = true;
            AdvanceEventRulesAndQueueFact(new AlcoholAppliedEvent(resolutionId, target.Seat, DamageBonus: 1));
            AddLog("CardEffect", $"{source.Name} 使用【酒】，{target.Name} 的下一张直接杀造成的伤害 +1。", source.Seat, target.Seat);
        }
        if (!HasRemainingAdjustedSimpleTargets(resolutionId))
            FinishSingleBasicCardUseCost(use, alcohol);
        FinishCardUse(resolutionId, alcohol);
    }

    private void ResolveGroupCard(
        CharacterState source,
        Card groupCard,
        LegalActionKind actionKind,
        CardKind requiredCardKind)
    {
        var stillLegal = BuildLegalActions(source).Any(action =>
            action.Kind == actionKind &&
            action.CardId == groupCard.Id &&
            action.TargetSeat is null);
        if (!stillLegal)
        {
            throw new InvalidOperationException(
                $"{groupCard.Kind} became illegal before resolution.");
        }

        var targets = Enumerable.Range(1, _playerCount - 1)
            .Select(offset => _players[(source.Seat + offset) % _playerCount])
            .Where(player => player.IsAlive &&
                !HasCardPolicy(player, SkillProgramCardPolicyKind.ExcludeGlobalTarget,
                    groupCard.Kind) &&
                !IsCardTargetProhibited(player, groupCard.Kind, groupCard.Suit, SuitColor(EffectiveSuit(source, groupCard))) && !HasBeneficiarySuitShield(source.Seat, player.Seat, EffectiveSuit(source, groupCard)))
            .Select(player => player.Seat)
            .ToArray();
        var resolutionId = BeginCardUse(groupCard, source.Seat, targets);
        MoveCard(
            groupCard,
            FindOwnedCardLocation(source, groupCard),
            CardLocation.Processing,
            CardMoveReasons.Use);
        BeginJizhiOrNullificationWindow(
            resolutionId,
            groupCard,
            source.Seat,
            targets,
            actionKind,
            requiredCardKind: requiredCardKind);
    }

    private IReadOnlyList<Card> GetCardUsePhysicalCards(long resolutionId)
    {
        var frame = _resolutionStack.OfType<CardUseFrame>().Single(cardUse => cardUse.Id == resolutionId);
        var processing = _cardZones.CardsAt(CardLocation.Processing);
        return (frame.PhysicalCardIds ?? [frame.CardId])
            .Select(cardId => IsClaimedUseCardEntity(resolutionId,cardId) ? EntityAtCurrentLocation(cardId) : processing.Single(card => card.Id == cardId))
            .ToArray();
    }

    private void ResolvePeachGarden(CharacterState source, Card peachGarden)
    {
        var stillLegal = BuildLegalActions(source).Any(action =>
            action.Kind == LegalActionKind.PeachGarden &&
            action.CardId == peachGarden.Id &&
            action.TargetSeat is null);
        if (!stillLegal)
        {
            throw new InvalidOperationException("PeachGarden became illegal before resolution.");
        }

        var targets = ApplyTurnCardGroupTargetRestrictions(source, Enumerable.Range(0, _playerCount)
            .Select(offset => _players[(source.Seat + offset) % _playerCount])
            .Where(player => player.IsAlive &&
                !IsCardTargetProhibited(player, CardKind.PeachGarden, peachGarden.Suit, SuitColor(EffectiveSuit(source, peachGarden))) && !HasBeneficiarySuitShield(source.Seat, player.Seat, EffectiveSuit(source, peachGarden)))
            .Select(player => player.Seat)
            .ToArray());
        var resolutionId = BeginCardUse(peachGarden, source.Seat, targets);
        MoveCard(
            peachGarden,
            FindOwnedCardLocation(source, peachGarden),
            CardLocation.Processing,
            CardMoveReasons.Use);
        BeginJizhiOrNullificationWindow(
            resolutionId,
            peachGarden,
            source.Seat,
            targets,
            LegalActionKind.PeachGarden);
    }

    private void ResolveFiveGrains(CharacterState source, Card fiveGrains)
    {
        var stillLegal = BuildLegalActions(source).Any(action =>
            action.Kind == LegalActionKind.FiveGrains &&
            action.CardId == fiveGrains.Id &&
            action.TargetSeat is null);
        if (!stillLegal)
        {
            throw new InvalidOperationException("FiveGrains became illegal before resolution.");
        }

        var aliveSeats = ApplyTurnCardGroupTargetRestrictions(source, Enumerable.Range(0, _playerCount)
            .Select(offset => _players[(source.Seat + offset) % _playerCount])
            .Where(player => player.IsAlive &&
                !IsCardTargetProhibited(player, CardKind.FiveGrains, fiveGrains.Suit, SuitColor(EffectiveSuit(source, fiveGrains))) && !HasBeneficiarySuitShield(source.Seat, player.Seat, EffectiveSuit(source, fiveGrains)))
            .Select(player => player.Seat)
            .ToArray());
        var availableCards = _cardZones.Count(CardLocation.DrawPile) +
                             _cardZones.Count(CardLocation.DiscardPile);
        var targets = aliveSeats.Take(availableCards).ToArray();
        var resolutionId = BeginCardUse(fiveGrains, source.Seat, targets);
        MoveCard(
            fiveGrains,
            FindOwnedCardLocation(source, fiveGrains),
            CardLocation.Processing,
            CardMoveReasons.Use);
        BeginJizhiOrNullificationWindow(
            resolutionId,
            fiveGrains,
            source.Seat,
            targets,
            LegalActionKind.FiveGrains);
    }

    private void ResolveIronChain(
        CharacterState source,
        Card ironChain,
        IReadOnlyList<int> targetSeats,
        CardKind? playedCardKind = null)
    {
        var targets = targetSeats.ToArray();
        var stillLegal = BuildLegalActions(source).Any(action =>
            action.Kind == LegalActionKind.IronChain &&
            action.CardId == ironChain.Id &&
            action.TargetSeats.SequenceEqual(targets) &&
            action.PlayedCardKind == playedCardKind);
        if (!stillLegal)
        {
            throw new InvalidOperationException("IronChain became illegal before resolution.");
        }

        var resolutionId = BeginCardUse(ironChain, source.Seat, targets, playedCardKind);
        MoveCard(
            ironChain,
            FindOwnedCardLocation(source, ironChain),
            CardLocation.Processing,
            CardMoveReasons.IronChainUse);
        BeginJizhiOrNullificationWindow(
            resolutionId,
            ironChain,
            source.Seat,
            targets,
            LegalActionKind.IronChain,
            playedCardKind: playedCardKind);
    }

    private void ResolveDelayedCard(
        CharacterState source,
        CharacterState target,
        Card delayedCard,
        LegalActionKind actionKind,
        CardKind? playedCardKind = null)
    {
        var effectiveCardKind = playedCardKind ?? delayedCard.Kind;
        var stillLegal = BuildLegalActions(source).Any(action =>
            action.Kind == actionKind &&
            action.CardId == delayedCard.Id &&
            action.TargetSeat == target.Seat &&
            (action.PlayedCardKind ?? delayedCard.Kind) == effectiveCardKind);
        if (!stillLegal)
        {
            throw new InvalidOperationException(
                $"{CardCatalog.Get(effectiveCardKind).DisplayName} became illegal before resolution.");
        }

        var resolutionId = BeginCardUse(
            delayedCard,
            source.Seat,
            [target.Seat],
            effectiveCardKind);
        MoveCard(
            delayedCard,
            FindOwnedCardLocation(source, delayedCard),
            CardLocation.Processing,
            CardMoveReasons.Use);
        if (effectiveCardKind == CardKind.Lightning && TryBeginDelayedCardUsePrograms(resolutionId))
        {
            return;
        }
        BeginJizhiOrNullificationWindow(
            resolutionId,
            delayedCard,
            source.Seat,
            [target.Seat],
            actionKind,
            playedCardKind: effectiveCardKind);
    }

    private void ResolveDelayedCardEffect(NullificationWindowFrame pending)
    {
        var source = _players[pending.SourceSeat];
        var targetSeat = GetNullificationTargetSeat(pending) ??
            throw new InvalidOperationException("A delayed card must retain one target.");
        var target = _players[targetSeat];
        var expectedActionKind = pending.EffectCardKind switch
        {
            CardKind.Indulgence => LegalActionKind.Indulgence,
            CardKind.SupplyShortage => LegalActionKind.SupplyShortage,
            CardKind.Lightning => LegalActionKind.Lightning,
            _ => throw new InvalidOperationException(
                $"Card {GetNullificationEffectCard(pending).Kind} is not a delayed card.")
        };
        var canTargetSelf = pending.EffectCardKind == CardKind.Lightning;
        if (pending.EffectCardKind == CardKind.SupplyShortage &&
            false &&
            GetHand(target).Count == 0)
        {
            SkipUnavailableTargetCardEffect(pending, target, CardEffectSkipReason.TargetHandEmpty);
            return;
        }

        if (HasCardPolicy(target, SkillProgramCardPolicyKind.ProhibitDelayedTrickTarget))
        {
            SkipUnavailableTargetCardEffect(pending, target, CardEffectSkipReason.SkillNullified);
            return;
        }
        if (!target.IsAlive || target.JudgmentAreaAbolished ||
            (!canTargetSelf && target.Seat == source.Seat) ||
            pending.ActionKind != expectedActionKind ||
            HasJudgmentEffectiveCard(target, pending.EffectCardKind))
        {
            throw new InvalidOperationException(
                $"{CardCatalog.Get(pending.EffectCardKind).DisplayName} target is no longer legal.");
        }

        SetCardUseStep(pending.ParentFrameId, ResolutionFrameStep.ResolvingEffect);
        MoveCard(
            GetNullificationEffectCard(pending),
            CardLocation.Processing,
            CardLocation.Judgment(target.Seat),
            CardMoveReasons.DelayedCardPlace);
        if (pending.EffectCardKind != GetNullificationEffectCard(pending).Kind)
        {
            _judgmentEffectiveCardKinds[pending.EffectCardId] = pending.EffectCardKind;
        }
        AdvanceEventRulesAndQueueFact(new DelayedCardPlacedEvent(
            pending.ParentFrameId,
            pending.EffectCardId,
            pending.EffectCardKind,
            source.Seat,
            target.Seat));
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(
            pending.EffectCardId,
            pending.EffectCardKind,
            source.Seat,
            target.Seat));
        AddLog(
            "CardEffect",
            $"{source.Name} 对 {target.Name} 使用【{CardCatalog.Get(pending.EffectCardKind).DisplayName}】，置入其判定区。",
            source.Seat,
            target.Seat);
        FinishCardUse(pending.ParentFrameId, GetNullificationEffectCard(pending), pending.EffectCardKind);
    }

    private void ResolveDismantlement(
        CharacterState source,
        CharacterState target,
        Card dismantlement,
        int? targetCardId,
        CardKind? playedCardKind = null) =>
        BeginTargetCardEffect(
            source,
            target,
            dismantlement,
            LegalActionKind.Dismantlement,
            TargetCardEffect.Discard,
            targetCardId,
            playedCardKind);

    private void ResolveSnatch(
        CharacterState source,
        CharacterState target,
        Card snatch,
        int? targetCardId,
        CardKind? playedCardKind = null) =>
        BeginTargetCardEffect(
            source,
            target,
            snatch,
            LegalActionKind.Snatch,
            TargetCardEffect.Take,
            targetCardId,
            playedCardKind);

    private void BeginTargetCardEffect(
        CharacterState source,
        CharacterState target,
        Card effectCard,
        LegalActionKind actionKind,
        TargetCardEffect effect,
        int? targetCardId,
        CardKind? playedCardKind = null)
    {
        var effectiveCardKind = playedCardKind ?? effectCard.Kind;
        var stillLegal = BuildLegalActions(source).Any(action =>
            action.Kind == actionKind &&
            action.CardId == effectCard.Id &&
            action.TargetSeat == target.Seat &&
            action.TargetCardId == targetCardId &&
            (action.PlayedCardKind ?? effectCard.Kind) == effectiveCardKind);
        if (!stillLegal)
        {
            throw new InvalidOperationException(
                $"{effectCard.Kind} became illegal before resolution.");
        }

        if (targetCardId is { } requestedTargetCardId &&
            FindPublicTargetCard(target, requestedTargetCardId) is null)
        {
            throw new InvalidOperationException(
                $"{effectCard.Kind} cannot resolve against the requested public equipment or judgment card.");
        }

        if (targetCardId is null)
        {
            var targetHand = GetHand(target);
            if (targetHand.Count == 0)
            {
                throw new InvalidOperationException(
                    $"{effectCard.Kind} cannot resolve against an empty hand.");
            }
        }

        var sourceLocation = FindOwnedCardLocation(source, effectCard);
        var resolutionId = BeginCardUse(effectCard, source.Seat, [target.Seat], effectiveCardKind);
        MoveCard(
            effectCard,
            sourceLocation,
            CardLocation.Processing,
            CardMoveReasons.Use);
        BeginJizhiOrNullificationWindow(
            resolutionId,
            effectCard,
            source.Seat,
            [target.Seat],
            actionKind,
            targetCardId,
            playedCardKind: effectiveCardKind);
    }

    private void ResolveFireAttack(
        CharacterState source,
        CharacterState target,
        Card fireAttack,
        CardKind? playedCardKind = null)
    {
        var stillLegal = BuildLegalActions(source).Any(action =>
            action.Kind == LegalActionKind.FireAttack &&
            action.CardId == fireAttack.Id &&
            action.TargetSeat == target.Seat &&
            action.PlayedCardKind == playedCardKind);
        if (!stillLegal)
        {
            throw new InvalidOperationException("FireAttack became illegal before resolution.");
        }

        var resolutionId = BeginCardUse(fireAttack, source.Seat, [target.Seat], playedCardKind);
        MoveCard(
            fireAttack,
            FindOwnedCardLocation(source, fireAttack),
            CardLocation.Processing,
            CardMoveReasons.Use);
        BeginJizhiOrNullificationWindow(
            resolutionId,
            fireAttack,
            source.Seat,
            [target.Seat],
            LegalActionKind.FireAttack,
            playedCardKind: playedCardKind);
    }

    private void ResolveBorrowedSword(
        CharacterState source,
        CharacterState weaponOwner,
        CharacterState slashTarget,
        Card borrowedSword)
    {
        var targets = new[] { weaponOwner.Seat, slashTarget.Seat };
        var stillLegal = BuildLegalActions(source).Any(action =>
            action.Kind == LegalActionKind.BorrowedSword &&
            action.CardId == borrowedSword.Id &&
            action.TargetSeats.SequenceEqual(targets));
        if (!stillLegal)
        {
            throw new InvalidOperationException("Borrowed Sword became illegal before resolution.");
        }

        var resolutionId = BeginCardUse(borrowedSword, source.Seat, targets,
            designatedTargetSeats: _selectedNextCardTargetSeats is { Count: >= 4 } selectedTargets
                ? selectedTargets.Where((_,index)=>index%2==0).ToArray() : [targets[0]]);
        MoveCard(
            borrowedSword,
            FindOwnedCardLocation(source, borrowedSword),
            CardLocation.Processing,
            CardMoveReasons.Use);
        BeginJizhiOrNullificationWindow(
            resolutionId,
            borrowedSword,
            source.Seat,
            targets,
            LegalActionKind.BorrowedSword);
    }

    private void BeginFireAttackReveal(CardUseFrame pending)
    {
        if (TryBeginColorFireAttackReveal(pending)) return;
        if (!ReferenceEquals(ActiveFireAttack, pending) || pending.FireAttackSelection?.RevealedCardId is not null)
        {
            throw new InvalidOperationException("The FireAttack reveal is not the current resolution.");
        }

        var target = _players[GetFireAttackTargetSeat(pending)];
        var hand = GetHand(target);
        if (hand.Count == 0)
        {
            throw new InvalidOperationException("FireAttack cannot request a reveal from an empty hand.");
        }

        _pendingDecision = new PendingDecision(
            DecisionKind.FireAttackReveal,
            target.Seat,
            $"{_players[pending.SourceSeat].Name} 对你使用了【火攻】，请选择一张手牌展示。",
            hand.Select(card => card.Id).ToArray(),
            [],
            pending.SourceSeat,
            pending.CardKind)
        {
            PromptId = CreatePromptId(),
            Choices = hand
                .Select(card => new PromptChoice(
                    new ChoiceId($"fire-attack.reveal.card-{card.Id}"),
                    $"展示【{card.DisplayName}】（{card.RankText}）",
                    [card.Id],
                    [],
                    new Dictionary<string, string>
                    {
                        ["response"] = "fire-attack-reveal",
                        ["action"] = "reveal-card"
                    }))
                .ToArray(),
            TargetSeat = target.Seat
        };
        _status = target.IsHuman
            ? EngineStatus.AwaitingHumanCardSelection
            : EngineStatus.Running;
    }

    private void ResolveFireAttackReveal(
        CardUseFrame pending,
        int cardId)
    {
        if (!ReferenceEquals(ActiveFireAttack, pending) || pending.FireAttackSelection?.RevealedCardId is not null)
        {
            throw new InvalidOperationException("The FireAttack reveal is not the current resolution.");
        }

        var target = _players[GetFireAttackTargetSeat(pending)];
        var revealed = GetHand(target).SingleOrDefault(card => card.Id == cardId) ??
            throw new InvalidOperationException("The selected FireAttack reveal card is not in the target hand.");
        pending = SetFireAttackSelection(pending.Id, new FireAttackSelectionState(revealed.Id));
        SetCardUseStep(pending.Id, ResolutionFrameStep.AwaitingResponse);
        AdvanceEventRulesAndQueueFact(new FireAttackCardRevealedEvent(
            pending.Id,
            pending.SourceSeat,
            GetFireAttackTargetSeat(pending),
            revealed.Id,
            revealed.Kind,
            revealed.Suit));
        AddLog(
            "CardRevealed",
            $"{target.Name} 展示了【{revealed.DisplayName}】（{revealed.Suit}），等待攻击者弃置同花色手牌。",
            target.Seat,
            pending.SourceSeat);
        BeginFireAttackDiscard(ActiveFireAttack ??
            throw new InvalidOperationException("FireAttack lost its revealed selection."), revealed);
    }

    private void BeginFireAttackDiscard(
        CardUseFrame pending,
        Card revealed)
    {
        if (TryBeginColorFireAttackDiscard(pending, revealed)) return;
        if (!ReferenceEquals(ActiveFireAttack, pending) || pending.FireAttackSelection?.RevealedCardId != revealed.Id)
        {
            throw new InvalidOperationException("The FireAttack discard is not the current resolution.");
        }

        var source = _players[pending.SourceSeat];
        var matchingCards = GetHand(source)
            .Where(card => card.Suit == revealed.Suit)
            .ToArray();
        if (matchingCards.Length == 0)
        {
            ResolveFireAttackDiscard(pending, selectedCardId: null);
            return;
        }

        _pendingDecision = new PendingDecision(
            DecisionKind.FireAttackDiscard,
            source.Seat,
            $"目标展示了【{revealed.DisplayName}】（{revealed.Suit}），请选择一张同花色手牌弃置，或放弃造成火焰伤害。",
            matchingCards.Select(card => card.Id).ToArray(),
            [],
            source.Seat,
            pending.CardKind)
        {
            PromptId = CreatePromptId(),
            Choices = matchingCards
                .Select(card => new PromptChoice(
                    new ChoiceId($"fire-attack.discard.card-{card.Id}"),
                    $"弃置【{card.DisplayName}】（{card.RankText}），造成 1 点火焰伤害。",
                    [card.Id],
                    [],
                    new Dictionary<string, string>
                    {
                        ["response"] = "fire-attack-discard",
                        ["action"] = "discard-same-suit"
                    }))
                .Append(new PromptChoice(
                    new ChoiceId("fire-attack.skip"),
                    "不弃置同花色牌，火攻不造成伤害。",
                    [],
                    [],
                    new Dictionary<string, string>
                    {
                        ["response"] = "fire-attack-skip",
                        ["action"] = "skip-fire-attack"
                    }))
                .ToArray(),
            TargetSeat = GetFireAttackTargetSeat(pending)
        };
        _status = source.IsHuman
            ? EngineStatus.AwaitingHumanCardSelection
            : EngineStatus.Running;
    }

    private void ResolveFireAttackDiscard(
        CardUseFrame pending,
        int? selectedCardId)
    {
        if (TryResolveColorFireAttackDiscard(pending, selectedCardId)) return;
        if (!ReferenceEquals(ActiveFireAttack, pending) || pending.FireAttackSelection?.RevealedCardId is null)
        {
            throw new InvalidOperationException("The FireAttack discard is not the current resolution.");
        }

        var source = _players[pending.SourceSeat];
        var target = _players[GetFireAttackTargetSeat(pending)];
        var revealed = GetFireAttackRevealedCard(pending);
        var matchingDiscard = selectedCardId is { } cardId
            ? GetHand(source).SingleOrDefault(card => card.Id == cardId)
            : null;
        if (selectedCardId is not null &&
            (matchingDiscard is null || matchingDiscard.Suit != revealed.Suit))
        {
            throw new InvalidOperationException("FireAttack requires a same-suit source discard card.");
        }

        if (matchingDiscard is null)
        {
            AdvanceEventRulesAndQueueFact(new FireAttackResolvedEvent(
                pending.Id,
                source.Seat,
                target.Seat,
                revealed.Id,
                revealed.Suit,
                MatchingDiscardCardId: null,
                CausedDamage: false));
            AddLog(
                "CardEffect",
                $"{source.Name} 未弃置同花色牌，{target.Name} 的【火攻】未造成伤害。",
                source.Seat,
                target.Seat);
            var effectCard = GetFireAttackEffectCard(pending);
            MoveFinishedTrickCard(pending.Id, effectCard);
            SetFireAttackSelection(pending.Id, null);
            FinishCardUse(pending.Id, effectCard, pending.CardKind);
            return;
        }

        MoveCard(
            matchingDiscard,
            CardLocation.Hand(source.Seat),
            CardLocation.Processing,
            CardMoveReasons.FireAttackDiscard);
        MoveCard(
            matchingDiscard,
            CardLocation.Processing,
            CardLocation.DiscardPile,
            CardMoveReasons.FireAttackDiscardFinished);
        AdvanceEventRulesAndQueueFact(new FireAttackResolvedEvent(
            pending.Id,
            source.Seat,
            target.Seat,
            revealed.Id,
            revealed.Suit,
            matchingDiscard.Id,
            CausedDamage: true));
        AddLog(
            "CardEffect",
            $"{source.Name} 弃置【{matchingDiscard.DisplayName}】，{target.Name} 受到 1 点火焰伤害。",
            source.Seat,
            target.Seat);

        var attackCard = GetFireAttackEffectCard(pending);
        SetFireAttackSelection(pending.Id, null);
        var attack = new CardAttackHandle(this,
            pending.Id,
            source.Seat,
            target.Seat,
            IsTieredRoundZeroFireAttackUse(pending.Id) ? null : attackCard,
            damageAmount: 1,
            playedCardKind: CardKind.FireAttack,
            physicalCards: GetCardUsePhysicalCards(pending.Id));
        ActiveCardAttack = attack;
        if (!ApplyAttackDamage(attack))
        {
            CompleteAttack(attack);
        }
    }

    private Card GetFireAttackRevealedCard(CardUseFrame pending)
    {
        var revealedCardId = pending.FireAttackSelection?.RevealedCardId ??
            throw new InvalidOperationException("FireAttack has no revealed card.");
        var location = CardLocation.Hand(GetFireAttackTargetSeat(pending));
        return _cardZones.CardsAt(location).Single(card => card.Id == revealedCardId);
    }

    private static int GetFireAttackTargetSeat(CardUseFrame pending) =>
        pending.TargetIndex >= 0 && pending.TargetIndex < pending.TargetSeats.Count
            ? pending.TargetSeats[pending.TargetIndex]
            : throw new InvalidOperationException("FireAttack lost its current target.");

    private Card GetFireAttackEffectCard(CardUseFrame pending) =>
        GetTrickRepresentation(pending.Id, pending.CardId, requireProcessing: true);

    private CardUseFrame SetFireAttackSelection(long frameId, FireAttackSelectionState? selection)
    {
        if (_resolutionStack.LastOrDefault() is not CardUseFrame cardUse ||
            cardUse.Id != frameId || cardUse.CardKind != CardKind.FireAttack ||
            (selection is null && cardUse.FireAttackSelection is null) ||
            (selection is not null && cardUse.TargetIndex >= cardUse.TargetSeats.Count))
        {
            throw new InvalidOperationException("FireAttack selection lost its current CardUse frame.");
        }

        var next = cardUse with { FireAttackSelection = selection };
        ReplaceRuntimeTop(next);
        return next;
    }

    private void BeginHarvestSelection(GroupCardHandle group)
    {
        if (!SameContinuationOwner(ActiveGroupCard, group) || group.Effect != GroupCardEffect.PublicDraft)
        {
            throw new InvalidOperationException("The FiveGrains draft is not the current card resolution.");
        }

        while (group.TargetIndex < group.TargetSeats.Count &&
               (!_players[group.TargetSeats[group.TargetIndex]].IsAlive ||
                IsCardEffectIneffective(group.ResolutionId, group.TargetSeats[group.TargetIndex])))
        {
            group.TargetIndex++;
            SetCardUseTargetIndex(group.ResolutionId, group.TargetIndex);
        }

        if (group.TargetIndex >= group.TargetSeats.Count)
        {
            FinishFiveGrains(group);
            return;
        }

        var picker = _players[group.TargetSeats[group.TargetIndex]];
        var processing = _cardZones.CardsAt(CardLocation.Processing);
        var revealed = group.RevealedCardIds
            .Select(cardId => processing.Single(card => card.Id == cardId))
            .ToArray();
        if (revealed.Length == 0)
        {
            throw new InvalidOperationException("A FiveGrains picker cannot choose from an empty reveal.");
        }

        _pendingDecision = new PendingDecision(
            DecisionKind.SelectHarvestCard,
            picker.Seat,
            $"{_players[group.SourceSeat].Name} 使用了【五谷丰登】，请选择一张公开牌。",
            group.RevealedCardIds.ToArray(),
            [],
            group.SourceSeat,
            group.Card.Kind)
        {
            PromptId = CreatePromptId(),
            Choices = revealed
                .Select(card => new PromptChoice(
                    new ChoiceId($"harvest.card-{card.Id}"),
                    $"选择公开牌【{card.DisplayName}】（{card.RankText}）",
                    [card.Id],
                    [],
                    new Dictionary<string, string>
                    {
                        ["action"] = "harvest-pick"
                    }))
                .ToArray()
        };
        _status = picker.IsHuman
            ? EngineStatus.AwaitingHumanCardSelection
            : EngineStatus.Running;
    }

    private void ResolveHarvestSelection(
        GroupCardHandle group,
        CharacterState picker,
        int cardId)
    {
        if (!SameContinuationOwner(ActiveGroupCard, group) || group.Effect != GroupCardEffect.PublicDraft)
        {
            throw new InvalidOperationException("The FiveGrains draft is not the current card resolution.");
        }

        if (group.TargetIndex >= group.TargetSeats.Count ||
            group.TargetSeats[group.TargetIndex] != picker.Seat)
        {
            throw new InvalidOperationException("The selected FiveGrains picker is not current.");
        }

        if (!group.RevealedCardIds.Contains(cardId))
        {
            throw new InvalidOperationException("The selected card is not in the public FiveGrains reveal.");
        }

        var card = _cardZones.CardsAt(CardLocation.Processing)
            .Single(candidate => candidate.Id == cardId);
        MoveCard(
            card,
            CardLocation.Processing,
            CardLocation.Hand(picker.Seat),
            CardMoveReasons.HarvestPick);
        group.RemoveRevealedCard(cardId);
        AddLog(
            "CardSelected",
            $"{picker.Name} 从五谷丰登的公开牌中选择了【{card.DisplayName}】。",
            picker.Seat);
        AdvanceEventRulesAndQueueFact(new HarvestCardSelectedEvent(group.ResolutionId, picker.Seat, cardId));

        group.TargetIndex++;
        SetCardUseTargetIndex(group.ResolutionId, group.TargetIndex);
        if (group.TargetIndex >= group.TargetSeats.Count)
        {
            FinishFiveGrains(group);
        }
        else
        {
            BeginHarvestSelection(group);
        }
    }

    private void FinishFiveGrains(GroupCardHandle group)
    {
        if (!SameContinuationOwner(ActiveGroupCard, group) || group.Effect != GroupCardEffect.PublicDraft)
        {
            throw new InvalidOperationException("The FiveGrains draft is not the current card resolution.");
        }

        if (ActiveDying is not null || ActiveCardAttack is not null ||
            group.TargetIndex < group.TargetSeats.Count)
        {
            throw new InvalidOperationException("FiveGrains cannot finish while a picker or child resolution is pending.");
        }

        foreach (var cardId in group.RevealedCardIds.ToArray())
        {
            var card = _cardZones.CardsAt(CardLocation.Processing)
                .Single(candidate => candidate.Id == cardId);
            MoveCard(
                card,
                CardLocation.Processing,
                CardLocation.DiscardPile,
                CardMoveReasons.HarvestDiscard);
        }
        group.ClearRevealedCards();

        if (!SkipTieredRoundZeroFinishedMovement(group.ResolutionId, group.Card))
        {
            var cardLocation = _cardZones.GetLocation(group.Card.Id);
            if (cardLocation == CardLocation.Processing)
                MoveCard(group.Card, CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.UseFinished);
            else if (cardLocation != CardLocation.DiscardPile)
                throw new InvalidOperationException($"A resolved {group.Card.Kind} left Processing through an unsupported destination: {cardLocation}.");
        }

        SetCardUseTargetIndex(group.ResolutionId, group.TargetSeats.Count);
        ActiveGroupCard = null;
        ActiveCardAttack = null;
        ActiveDuel = null;
        _pendingDecision = null;
        FinishCardUse(group.ResolutionId, group.Card);

        if (_winner != Winner.None && _status != EngineStatus.Completed)
        {
            CompleteGame();
        }
    }

    private void RunOneGroupRecoveryStep()
    {
        var group = ActiveGroupCard ??
            throw new InvalidOperationException("A group recovery step has no pending card.");
        if (group.Effect != GroupCardEffect.Recovery)
        {
            throw new InvalidOperationException("The pending group card is not a recovery effect.");
        }

        if (group.TargetIndex >= group.TargetSeats.Count)
        {
            FinishGroupRecovery(group);
            AdvanceRulesAndPublishState();
            return;
        }

        var target = _players[group.TargetSeats[group.TargetIndex]];
        if (target.IsAlive &&
            !IsCardEffectIneffective(group.ResolutionId, target.Seat) &&
            target.Hp < target.MaxHp)
        {
            if (TryQueueRecoveryReplacement(group.ResolutionId, group.SourceSeat, target.Seat, 1,
                new(RecoveryAttemptProducer.GroupCard)))
            {
                TryBeginHpChangedProgramWindow(group.ResolutionId, PostEventContinuation.GroupRecovery);
                AdvanceRulesAndPublishState(); return;
            }
            var recoveryFrameId = BeginRecovery(
                group.ResolutionId,
                group.SourceSeat,
                target.Seat,
                1);
            try
            {
                target.Hp++;
                AddLog(
                    "Recovered",
                    $"{target.Name} 因【桃园结义】回复至 {target.Hp}/{target.MaxHp} 点体力。",
                    group.SourceSeat,
                    target.Seat);
                AdvanceEventRulesAndQueueFact(new RecoveryAppliedEvent(
                    group.SourceSeat,
                    target.Seat,
                    1,
                    target.Hp));
            }
            finally
            {
                PopResolutionFrame(recoveryFrameId, ResolutionFrameKind.Recovery);
            }
        }

        if (!TryBeginHpChangedProgramWindow(group.ResolutionId, PostEventContinuation.GroupRecovery))
            CompleteGroupRecoveryTarget();
        AdvanceRulesAndPublishState();
    }

    private void CompleteGroupRecoveryTarget()
    {
        var group = ActiveGroupCard ?? throw new InvalidOperationException("A recovery continuation lost its group card.");
        group.TargetIndex++;
        SetCardUseTargetIndex(group.ResolutionId, group.TargetIndex);
        if (group.TargetIndex >= group.TargetSeats.Count) FinishGroupRecovery(group);
    }

    private void BeginGroupAttackResponse(GroupCardHandle group)
    {
        if (!SameContinuationOwner(ActiveGroupCard, group) || group.Effect != GroupCardEffect.ResponseAttack)
        {
            throw new InvalidOperationException("The group attack is not the current card resolution.");
        }

        if (_winner != Winner.None)
        {
            FinishGroupAttack(group);
            return;
        }

        if (group.TargetIndex >= group.TargetSeats.Count)
        {
            FinishGroupAttack(group);
            return;
        }

        var targetSeat = group.TargetSeats[group.TargetIndex];
        var target = _players[targetSeat];
        if (!target.IsAlive || IsCardEffectIneffective(group.ResolutionId, targetSeat))
        {
            group.TargetIndex++;
            SetCardUseTargetIndex(group.ResolutionId, group.TargetIndex);
            BeginGroupAttackResponse(group);
            return;
        }

        var attack = new CardAttackHandle(this,
            group.ResolutionId,
            group.DamageSourceSeat,
            target.Seat,
            IsTieredRoundZeroUse(group.ResolutionId) ? null : group.Card,
            playedCardKind: group.Card.Kind,
            physicalCards: group.PhysicalCards,
            ignoresArmor: HasDirectedCardArmorBypass(group.ResolutionId, target.Seat));
        group.CurrentAttack = attack;
        ActiveCardAttack = attack;
        if (UsesFormalTengjia && !attack.IgnoresArmor && HasTengjia(target) &&
            group.Card.Kind is CardKind.BarbarianAssault or CardKind.ArrowBarrage)
        {
            AddLog("ArmorEffect", $"{target.Name} 的【藤甲】令【{CardCatalog.Get(group.Card.Kind).DisplayName}】对其无效。", target.Seat, group.SourceSeat);
            AdvanceEventRulesAndQueueFact(new ArmorEffectAppliedEvent(
                group.ResolutionId, CardKind.Tengjia, group.SourceSeat, target.Seat, group.Card.Kind));
            SetCardUseStep(group.ResolutionId, ResolutionFrameStep.ResolvingEffect);
            CompleteAttack(attack);
            return;
        }
        if (IsYingboUnrespondable(group.ResolutionId) || IsIssuedCardUnrespondable(group.ResolutionId) ||
            IsNearbyTargetResponseProhibited(group.SourceSeat, target.Seat,
                group.Card.Kind, group.TargetSeats))
        {
            SetCardUseStep(group.ResolutionId, ResolutionFrameStep.ResolvingEffect);
            ResolveGroupResponse(group, target, selectedResponse: null);
            return;
        }
        var requiredCardKind = group.RequiredCardKind ??
            throw new InvalidOperationException("A group response attack must declare a required card kind.");
        var responseCards = GetResponseCards(target, requiredCardKind);
        var zhangbaPairs = requiredCardKind == CardKind.Slash
            ? GetZhangbaSlashPairs(target)
            : [];
        var programMultiCardResponses = requiredCardKind == CardKind.Slash
            ? GetProgramMultiCardViewAsSelections(target, CardKind.Slash, forResponse: true)
            : [];
        var hasBagua = requiredCardKind == CardKind.Dodge &&
                       !attack.IgnoresArmor &&
                       HasBagua(target);
        var canRequestFactionDefense = requiredCardKind == CardKind.Dodge && CanRequestFactionDefense(target, attack);
        var canRequestFactionSlash = requiredCardKind == CardKind.Slash && CanRequestFactionSlashResponse(target, attack);
        if (responseCards.Count == 0 && zhangbaPairs.Count == 0 && programMultiCardResponses.Count == 0 &&
            !hasBagua && !canRequestFactionDefense && !canRequestFactionSlash &&
            !(requiredCardKind == CardKind.Dodge && ProgramDodgeResponseChoices(target).Any()) &&
            !HasRequestedDeckBasicSource(target, requiredCardKind))
        {
            SetCardUseStep(group.ResolutionId, ResolutionFrameStep.ResolvingEffect);
            ResolveGroupResponse(group, target, selectedResponse: null);
            return;
        }

        PushResponseWindow(
            group.ResolutionId,
            group.SourceSeat,
            target.Seat,
            group.Card.Kind,
            requiredCardKind);
        var incomingName = CardCatalog.Get(group.Card.Kind).DisplayName;
        var requiredName = CardCatalog.Get(requiredCardKind).DisplayName;
        var decisionKind = requiredCardKind == CardKind.Dodge
            ? DecisionKind.RespondDodge
            : DecisionKind.RespondSlash;
        _pendingDecision = new PendingDecision(
            decisionKind,
            target.Seat,
            $"{_players[group.SourceSeat].Name} 使用了【{incomingName}】，是否打出【{requiredName}】？",
            responseCards.Select(card => card.Id)
                .Concat(zhangbaPairs.SelectMany(pair => pair.Select(card => card.Id)))
                .Concat(programMultiCardResponses.SelectMany(item => item.Cards.Select(card => card.Id)))
                .Distinct()
                .ToArray(),
            [],
            group.SourceSeat,
            group.Card.Kind)
        {
            PromptId = CreatePromptId(),
            Choices = CreateResponseChoices(
                responseCards,
                requiredCardKind,
                group.Card.Kind,
                card => GetEffectiveResponseKind(target, card, requiredCardKind),
                includeBagua: hasBagua,
                includeFactionDefense: canRequestFactionDefense,
                includeFactionSlash: canRequestFactionSlash,
                responder: target),
            RequiredCardKind = requiredCardKind
        };
        AdvanceEventRulesAndQueueFact(new ResponseRequestedEvent(
            group.SourceSeat,
            target.Seat,
            group.Card.Kind,
            requiredCardKind));
        _status = target.IsHuman
            ? EngineStatus.AwaitingHumanResponse
            : EngineStatus.Running;
    }

    private Card? GetWeapon(CharacterState player) =>
        GetEquipment(player).FirstOrDefault(card =>
            EquipmentCatalog.Get(card.Kind).Slot == EquipmentSlot.Weapon);

    private bool IsLegalBorrowedSwordSlashTarget(
        CharacterState weaponOwner,
        CharacterState target,
        CardKind? effectiveKind = null, Suit? physicalSuit = null, bool allowAnyPhysicalSuit = true, bool? effectiveColor = null, int? effectiveRank = null, IReadOnlyList<int>? physicalCardIds = null) =>
        effectiveKind is { } kind
            ? target.IsAlive && target.Seat != weaponOwner.Seat &&
              !IsCardUseForbidden(weaponOwner.Seat, kind, CardActionType.Use) &&
              ((HasProvenanceUseDistance(weaponOwner, physicalCardIds) || HasGrantedPhaseEntityDistance(weaponOwner, physicalCardIds)) || allowAnyPhysicalSuit && (HasPotentialProvenanceSlash(weaponOwner) || HasPotentialGrantedPhaseSlash(weaponOwner)) || HasSlashUseDistanceBySuit(weaponOwner,kind,physicalSuit) || allowAnyPhysicalSuit && GetSlashUseCards(weaponOwner).Any(c=>HasSlashUseDistanceBySuit(weaponOwner,kind,EffectiveSuit(weaponOwner,c))) || HasTurnRedSlashPolicyForColor(weaponOwner.Seat,kind,effectiveColor ?? SuitColor(physicalSuit)) || (allowAnyPhysicalSuit && GetSlashUseCards(weaponOwner).Any(c=>HasTurnRedSlashPolicy(weaponOwner.Seat,kind,EffectiveSuit(weaponOwner,c)))) || HasPhaseSuitAllowance(weaponOwner.Seat,physicalSuit) || (allowAnyPhysicalSuit && GetSlashUseCards(weaponOwner).Any(c=>HasPhaseSuitAllowance(weaponOwner,c))) || HasCardDistanceExemption(weaponOwner, target, kind) ||
               IsWithinSpecificSlashRange(weaponOwner,target,kind,effectiveRank) || allowAnyPhysicalSuit && HasPotentialRankSlashRange(weaponOwner,target,kind)) &&
              !IsDirectedCardTargetProhibited(weaponOwner.Seat, target.Seat, kind) &&
              !IsSlashProhibited(target)
            : SlashKinds.Any(candidate => IsLegalBorrowedSwordSlashTarget(weaponOwner, target, candidate, physicalSuit, allowAnyPhysicalSuit, effectiveColor,effectiveRank,physicalCardIds));

    private IReadOnlyList<Card> GetBorrowedSwordSlashCards(
        CharacterState weaponOwner,
        CharacterState slashTarget)
    {
        if (!IsLegalBorrowedSwordSlashTarget(weaponOwner, slashTarget))
        {
            return [];
        }

        return GetSlashUseCards(weaponOwner)
            .Where(card => !HasBeneficiarySuitShield(weaponOwner.Seat, slashTarget.Seat, EffectiveSuit(weaponOwner, card)))
            .Where(card =>
            {
                var baseKind = IsSlashCard(card.Kind) ? card.Kind : CardKind.Slash;
                if (!GetSlashUseVariants(weaponOwner, baseKind).Any(variant =>
                        ((HasProvenanceUseDistance(weaponOwner,card) || HasGrantedPhaseEntityDistance(weaponOwner,[card.Id])) || HasSlashUseDistanceBySuit(weaponOwner,variant.EffectiveKind,EffectiveSuit(weaponOwner,card)) || HasTurnRedSlashPolicy(weaponOwner.Seat,variant.EffectiveKind,EffectiveSuit(weaponOwner,card)) || HasPhaseSuitAllowance(weaponOwner,card) || IsWithinSpecificSlashRange(weaponOwner,slashTarget,variant.EffectiveKind,SpecificSlashRank(weaponOwner,card,variant.EffectiveKind)) || HasCardDistanceExemption(weaponOwner,slashTarget,variant.EffectiveKind)) && IsLegalBorrowedSwordSlashTarget(weaponOwner, slashTarget, variant.EffectiveKind, EffectiveSuit(weaponOwner,card), false, SuitColor(EffectiveSuit(weaponOwner,card)), SpecificSlashRank(weaponOwner,card,variant.EffectiveKind), [card.Id])))
                    return false;
                var location = _cardZones.GetLocation(card.Id);
                if (location != CardLocation.Equipment(weaponOwner.Seat) ||
                    EquipmentCatalog.Get(card.Kind).Slot != EquipmentSlot.Weapon)
                {
                    return true;
                }

                // When Wusheng spends the equipped weapon itself, that weapon
                // cannot also provide the range needed for the forced Slash.
                return (HasProvenanceUseDistance(weaponOwner,card) || HasGrantedPhaseEntityDistance(weaponOwner,[card.Id])) || HasRankSlashRange(weaponOwner,CardKind.Slash) && card.Rank > 0 || GetCombatDistance(weaponOwner.Seat, slashTarget.Seat) <= 1;
            })
            .ToArray();
    }

    private bool CanRequestBorrowedSwordFactionSlash(BorrowedSwordHandle pending)
    {
        var owner = _players[pending.WeaponOwnerSeat];
        return !pending.FactionSlashAttempted &&
               owner.IsAlive &&
               GetFactionResponsePolicy(owner, CardKind.Slash) is not null &&
               IsLegalBorrowedSwordSlashTarget(owner, _players[pending.SlashTargetSeat]) &&
               GetFactionSlashCandidateSeats(owner.Seat).Count > 0;
    }

    private void BeginBorrowedSwordSlashChoice(
        BorrowedSwordHandle pending,
        bool includeFactionSlash)
    {
        if (!SameContinuationOwner(ActiveBorrowedSword, pending) || pending.ActiveAttack is not null)
        {
            throw new InvalidOperationException("Borrowed Sword is not awaiting its forced Slash choice.");
        }

        var source = _players[pending.SourceSeat];
        var weaponOwner = _players[pending.WeaponOwnerSeat];
        var slashTarget = _players[pending.SlashTargetSeat];
        var weapon = GetWeapon(weaponOwner);
        if (!weaponOwner.IsAlive || weapon is null)
        {
            CompleteBorrowedSwordWithoutSlash(pending, transferWeapon: false);
            return;
        }

        if (IsIssuedCardUnrespondable(pending.ResolutionId) || !IsLegalBorrowedSwordSlashTarget(weaponOwner, slashTarget))
        {
            CompleteBorrowedSwordWithoutSlash(pending, transferWeapon: true);
            return;
        }

        var slashes = GetBorrowedSwordSlashCards(weaponOwner, slashTarget);
        var zhangbaPairs = GetZhangbaSlashPairs(weaponOwner).Where(cards => !HasRankSlashRange(weaponOwner,CardKind.Slash) || IsLegalBorrowedSwordSlashTarget(weaponOwner,slashTarget,CardKind.Slash,PhysicalGroupSuit(weaponOwner,cards),false,PhysicalGroupColor(weaponOwner,cards),ZhangbaSpecificSlashRank(weaponOwner,cards))).Where(cards => !HasBeneficiarySuitShield(weaponOwner.Seat, slashTarget.Seat, cards.Select(card => EffectiveSuit(weaponOwner, card)).Distinct().ToArray() is [var suit] ? suit : null)).ToArray();
        var programMultiCardUses = GetProgramMultiCardViewAsSelections(
            weaponOwner, CardKind.Slash, forResponse: false).Where(selection => !HasBeneficiarySuitShield(weaponOwner.Seat, slashTarget.Seat, selection.Cards.Select(card => EffectiveSuit(weaponOwner, card)).Distinct().ToArray() is [var suit] ? suit : null)).ToArray();
        var canRequestFactionSlash = includeFactionSlash && CanRequestBorrowedSwordFactionSlash(pending);
        if (slashes.Count == 0 && zhangbaPairs.Length == 0 && programMultiCardUses.Length == 0 && !canRequestFactionSlash &&
            TieredRoundZeroForcedSlashChoices(weaponOwner, slashTarget, pending.ResolutionId, false).Count == 0 &&
            !HasRequestedDeckBasicSource(weaponOwner, CardKind.Slash))
        {
            CompleteBorrowedSwordWithoutSlash(pending, transferWeapon: true);
            return;
        }

        PushResponseWindow(
            pending.ResolutionId,
            source.Seat,
            weaponOwner.Seat,
            CardKind.BorrowedSword,
            CardKind.Slash);
        var choices = slashes.SelectMany(card =>
        {
            var baseEffectiveKind = IsSlashCard(card.Kind) ? card.Kind : CardKind.Slash;
            return GetSlashUseKinds(weaponOwner, baseEffectiveKind)
                .Where(effectiveKind => effectiveKind == baseEffectiveKind ||
                                        IsNativeResponseCard(card, CardKind.Slash))
                .SelectMany(effectiveKind =>
            {
                var usesZhuqueFan = effectiveKind != baseEffectiveKind;
                var cardDescription = usesZhuqueFan
                    ? $"发动【朱雀羽扇】，将【杀】改为【火杀】使用"
                    : IsNativeResponseCard(card, CardKind.Slash)
                        ? $"使用【{CardCatalog.Get(effectiveKind).DisplayName}】"
                        : $"将【{card.DisplayName}】当作【杀】使用";
                var suffix = usesZhuqueFan ? ".as-FireSlash" : string.Empty;
                return CreateConversionChoiceVariants(
                    weaponOwner, card, baseEffectiveKind, forResponse: false,
                    $"borrowed-sword.slash.card-{card.Id}{suffix}",
                    $"{cardDescription}攻击 {slashTarget.Name}。",
                    [card.Id],
                    [slashTarget.Seat],
                    new Dictionary<string, string>
                    {
                        ["response"] = "borrowed-sword-slash",
                        ["response-card-kind"] = effectiveKind.ToString(),
                        ["target-seat"] = slashTarget.Seat.ToString(
                            System.Globalization.CultureInfo.InvariantCulture)
                    });
            });
        }).ToList();
        foreach (var pair in zhangbaPairs)
        {
            choices.Add(new PromptChoice(
                new ChoiceId($"borrowed-sword.zhangba-slash.cards-{pair[0].Id}-{pair[1].Id}"),
                $"发动【丈八蛇矛】，将两张手牌当【杀】攻击 {slashTarget.Name}。",
                [pair[0].Id, pair[1].Id],
                [slashTarget.Seat],
                new Dictionary<string, string>
                {
                    ["response"] = "zhangba-slash",
                    ["response-card-kind"] = CardKind.Slash.ToString(),
                    ["equipment"] = CardKind.ZhangbaSerpentSpear.ToString(),
                    ["target-seat"] = slashTarget.Seat.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                }));
        }
        foreach (var selection in programMultiCardUses)
        {
            var cardIds = selection.Cards.Select(card => card.Id).ToArray();
            var parameters = new Dictionary<string, string>
            {
                ["response"] = "program-view-as-slash",
                ["response-card-kind"] = CardKind.Slash.ToString(),
                ["target-seat"] = slashTarget.Seat.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
            };
            AddConversionParameters(parameters, selection.Source);
            choices.Add(new PromptChoice(
                new ChoiceId($"borrowed-sword.program-view-as-slash.{selection.Source.SkillId.Length}:" +
                             $"{selection.Source.SkillId}.{selection.Source.BindingId}.cards-{string.Join('-', cardIds)}"),
                $"发动【{ProgramConversionName(selection.Source)}】，将 {cardIds.Length} 张手牌当【杀】攻击 {slashTarget.Name}。",
                cardIds,
                [slashTarget.Seat],
                parameters));
        }
        if (canRequestFactionSlash)
        {
            choices.Add(new PromptChoice(
                new ChoiceId("borrowed-sword.jijiang"),
                $"发动主公技【激将】，请求其他蜀势力角色提供【杀】攻击 {slashTarget.Name}。",
                [],
                [slashTarget.Seat],
                new Dictionary<string, string>
                {
                    ["response"] = "faction-slash-request",
                    ["skill"] = GetFactionResponsePolicy(weaponOwner, CardKind.Slash)!.SkillId
                }));
        }

        choices.AddRange(TieredRoundZeroForcedSlashChoices(weaponOwner, slashTarget, pending.ResolutionId, false));
        choices.Add(new PromptChoice(
            new ChoiceId("borrowed-sword.give-weapon"),
            $"不使用【杀】，将【{weapon.DisplayName}】交给 {source.Name}。",
            [],
            [source.Seat],
            new Dictionary<string, string>
            {
                ["response"] = "borrowed-sword-give-weapon",
                ["weapon-card-id"] = weapon.Id.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
            }));

        _pendingDecision = new PendingDecision(
            DecisionKind.RespondSlash,
            weaponOwner.Seat,
            $"{source.Name} 对你使用【借刀杀人】：请对 {slashTarget.Name} 使用【杀】，否则交出武器。",
            slashes.Select(card => card.Id)
                .Concat(zhangbaPairs.SelectMany(pair => pair.Select(card => card.Id)))
                .Concat(programMultiCardUses.SelectMany(item => item.Cards.Select(card => card.Id)))
                .Distinct()
                .ToArray(),
            [slashTarget.Seat],
            source.Seat,
            CardKind.BorrowedSword)
        {
            PromptId = CreatePromptId(),
            TargetSeat = slashTarget.Seat,
            RequiredCardKind = CardKind.Slash,
            Choices = choices
        };
        pending.AwaitingSlashChoice = true;
        _status = weaponOwner.IsHuman
            ? EngineStatus.AwaitingHumanResponse
            : EngineStatus.Running;
        AdvanceEventRulesAndQueueFact(new ResponseRequestedEvent(
            source.Seat,
            weaponOwner.Seat,
            CardKind.BorrowedSword,
            CardKind.Slash));
    }

    private void ResolveBorrowedSwordSlashChoice(
        BorrowedSwordHandle pending,
        Card slash,
        CardKind? requestedEffectiveKind = null)
    {
        var declaredOwner = _players[pending.WeaponOwnerSeat];
        if (TryBeginCardDeclaration(declaredOwner, slash, requestedEffectiveKind ?? CardKind.Slash,
            PeekDeclarationConversion(declaredOwner, slash, requestedEffectiveKind ?? CardKind.Slash, true),
            DeclarationReturn(CardDeclarationPurpose.BorrowedSword, declaredOwner.Seat, finalKind: requestedEffectiveKind ?? CardKind.Slash), [pending.SlashTargetSeat])) return;
        var weaponOwner = _players[pending.WeaponOwnerSeat];
        var slashTarget = _players[pending.SlashTargetSeat];
        var selected = GetBorrowedSwordSlashCards(weaponOwner, slashTarget)
            .SingleOrDefault(card => card.Id == slash.Id) ??
            throw new InvalidOperationException("The selected Borrowed Sword Slash is no longer legal.");
        var baseEffectiveKind = IsSlashCard(selected.Kind) ? selected.Kind : CardKind.Slash;
        var effectiveKind = requestedEffectiveKind ?? baseEffectiveKind;
        var usesZhuqueFan = IsZhuqueFanConversion(
            weaponOwner,
            baseEffectiveKind,
            effectiveKind);
        if (effectiveKind != baseEffectiveKind && !usesZhuqueFan)
        {
            throw new InvalidOperationException("The selected Borrowed Sword Slash conversion is no longer legal.");
        }
        if (!IsLegalBorrowedSwordSlashTarget(weaponOwner, slashTarget, effectiveKind, EffectiveSuit(weaponOwner,selected), false, SuitColor(EffectiveSuit(weaponOwner,selected)), SpecificSlashRank(weaponOwner,selected,effectiveKind)) || !(HasSlashUseDistanceBySuit(weaponOwner,effectiveKind,EffectiveSuit(weaponOwner,selected)) || HasTurnRedSlashPolicy(weaponOwner.Seat,effectiveKind,EffectiveSuit(weaponOwner,selected)) || HasPhaseSuitAllowance(weaponOwner,selected) || HasCardDistanceExemption(weaponOwner,slashTarget,effectiveKind) || IsWithinSpecificSlashRange(weaponOwner,slashTarget,effectiveKind,SpecificSlashRank(weaponOwner,selected,effectiveKind))))
        {
            throw new InvalidOperationException("The selected Borrowed Sword Slash target is no longer legal.");
        }

        PopResponseWindow(pending.ResolutionId);
        SetCardUseStep(pending.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        ClearPendingDecision();
        pending.AwaitingSlashChoice = false;
        pending.SlashCardId = selected.Id;
        pending.EffectiveSlashKind = effectiveKind;
        ResolveSlashCore(
            weaponOwner,
            slashTarget,
            selected,
            effectiveKind,
            weaponOwner.Seat,
            borrowedSword: pending,
            usesZhuqueFan: usesZhuqueFan);
    }

    private void ResolveBorrowedSwordZhangbaSlashChoice(
        BorrowedSwordHandle pending,
        IReadOnlyList<Card> pair)
    {
        var weaponOwner = _players[pending.WeaponOwnerSeat];
        var slashTarget = _players[pending.SlashTargetSeat];
        if (!SameContinuationOwner(ActiveBorrowedSword, pending) ||
            !pending.AwaitingSlashChoice ||
            !IsLegalBorrowedSwordSlashTarget(weaponOwner, slashTarget, physicalSuit:PhysicalGroupSuit(weaponOwner,pair), allowAnyPhysicalSuit:false, effectiveColor:PhysicalGroupColor(weaponOwner,pair), effectiveRank:ZhangbaSpecificSlashRank(weaponOwner,pair)) ||
            FindZhangbaSlashPair(weaponOwner, pair.Select(card => card.Id).ToArray()) is null ||
            HasRankSlashRange(weaponOwner,CardKind.Slash) && !IsLegalBorrowedSwordSlashTarget(weaponOwner,slashTarget,CardKind.Slash,PhysicalGroupSuit(weaponOwner,pair),false,PhysicalGroupColor(weaponOwner,pair),ZhangbaSpecificSlashRank(weaponOwner,pair)))
        {
            throw new InvalidOperationException("The selected Borrowed Sword Zhangba Slash is no longer legal.");
        }

        PopResponseWindow(pending.ResolutionId);
        SetCardUseStep(pending.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        ClearPendingDecision();
        pending.AwaitingSlashChoice = false;
        pending.SlashCardId = pair[0].Id;
        pending.EffectiveSlashKind = CardKind.Slash;
        ResolveSlashCore(
            weaponOwner,
            slashTarget,
            pair[0],
            CardKind.Slash,
            weaponOwner.Seat,
            borrowedSword: pending,
            physicalCards: pair);
    }

    private void ResolveBorrowedSwordProgramMultiCardSlashChoice(
        BorrowedSwordHandle pending,
        ProgramMultiCardViewAsSelection selection)
    {
        var weaponOwner = _players[pending.WeaponOwnerSeat];
        var slashTarget = _players[pending.SlashTargetSeat];
        if (!SameContinuationOwner(ActiveBorrowedSword, pending) ||
            !pending.AwaitingSlashChoice ||
            !IsLegalBorrowedSwordSlashTarget(weaponOwner, slashTarget) ||
            FindProgramMultiCardViewAsSelection(
                weaponOwner,
                selection.Cards.Select(card => card.Id).ToArray(),
                CardKind.Slash,
                forResponse: false,
                selection.Source) is null)
        {
            throw new InvalidOperationException("The selected Borrowed Sword configured Slash is no longer legal.");
        }

        PopResponseWindow(pending.ResolutionId);
        SetCardUseStep(pending.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        ClearPendingDecision();
        pending.AwaitingSlashChoice = false;
        pending.SlashCardId = selection.Cards[0].Id;
        pending.EffectiveSlashKind = CardKind.Slash;
        ResolveProgramMultiCardSlash(
            weaponOwner,
            slashTarget,
            selection,
            borrowedSword: pending,
            enforceOwnTurnSlashLimit: false);
    }

    private void CompleteBorrowedSwordWithoutSlash(
        BorrowedSwordHandle pending,
        bool transferWeapon)
    {
        if (!SameContinuationOwner(ActiveBorrowedSword, pending) || pending.ActiveAttack is not null)
        {
            throw new InvalidOperationException("Borrowed Sword fallback is not current.");
        }

        if (_resolutionStack.LastOrDefault() is ResponseWindowFrame response &&
            response.ParentFrameId == pending.ResolutionId)
        {
            PopResponseWindow(pending.ResolutionId);
        }

        SetCardUseStep(pending.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        ClearPendingDecision();
        pending.AwaitingSlashChoice = false;
        var source = _players[pending.SourceSeat];
        var weaponOwner = _players[pending.WeaponOwnerSeat];
        var weapons = transferWeapon ? GetEquipment(weaponOwner).Where(card =>
            EquipmentCatalog.Get(card.Kind).Slot == EquipmentSlot.Weapon).ToArray() : [];
        var transferred = new List<Card>();
        if (weapons.Length > 0)
        {
            foreach (var weapon in weapons)
            {
                MoveCard(weapon, CardLocation.Equipment(weaponOwner.Seat), CardLocation.Processing,
                    CardMoveReasons.BorrowedSwordGive);
                var obtained = MoveProcessingCardUnlessDestroyed(weapon, CardLocation.Hand(source.Seat), CardMoveReasons.BorrowedSwordGive);
                if (obtained) transferred.Add(weapon);
                AddLog("CardEffect",
                    obtained ? $"{weaponOwner.Name} 未使用【杀】，将【{weapon.DisplayName}】交给 {source.Name}。" :
                        $"{weaponOwner.Name} 未使用【杀】，【{weapon.DisplayName}】离开装备区后销毁。",
                    weaponOwner.Seat, source.Seat);
            }
        }
        else
        {
            AddLog(
                "CardEffect",
                $"{weaponOwner.Name} 已没有可交出的武器，【借刀杀人】结束。",
                pending.SourceSeat,
                weaponOwner.Seat);
        }

        AdvanceEventRulesAndQueueFact(new BorrowedSwordResolvedEvent(
            pending.ResolutionId,
            pending.SourceSeat,
            pending.WeaponOwnerSeat,
            pending.SlashTargetSeat,
            UsedSlash: false,
            TransferredWeaponCardId: transferred.FirstOrDefault()?.Id,
            TransferredWeaponKind: transferred.FirstOrDefault()?.Kind));
        FinishBorrowedSword(pending);
    }

    private void CompleteBorrowedSwordAfterSlash(BorrowedSwordHandle pending)
    {
        if (!SameContinuationOwner(ActiveBorrowedSword, pending) ||
            pending.ActiveAttack is null ||
            pending.SlashCardId is null ||
            pending.EffectiveSlashKind is null)
        {
            throw new InvalidOperationException("Borrowed Sword Slash completion is not current.");
        }

        AdvanceEventRulesAndQueueFact(new BorrowedSwordResolvedEvent(
            pending.ResolutionId,
            pending.SourceSeat,
            pending.WeaponOwnerSeat,
            pending.SlashTargetSeat,
            UsedSlash: true,
            SlashCardId: pending.SlashCardId,
            EffectiveSlashKind: pending.EffectiveSlashKind));
        AddLog(
            "CardEffect",
            $"{_players[pending.WeaponOwnerSeat].Name} 已按【借刀杀人】要求对 {_players[pending.SlashTargetSeat].Name} 使用【{CardCatalog.Get(pending.EffectiveSlashKind.Value).DisplayName}】。",
            pending.WeaponOwnerSeat,
            pending.SlashTargetSeat);
        FinishBorrowedSword(pending);
    }

    private void FinishBorrowedSword(BorrowedSwordHandle pending)
    {
        if (!HasRemainingAdjustedBorrowedSwordTargets(pending.ResolutionId))
            SetCardUseTargetIndex(pending.ResolutionId, _resolutionStack.OfType<CardUseFrame>().Single(frame => frame.Id == pending.ResolutionId).TargetSeats.Count);
        MoveFinishedTrickCard(pending.ResolutionId, pending.Card);
        ActiveBorrowedSword = null;
        if (SameContinuationOwner(ActiveFactionCardRequest?.BorrowedSword, pending))
        {
            ActiveFactionCardRequest = null;
        }
        FinishCardUse(pending.ResolutionId, pending.Card, CardKind.BorrowedSword);

        if (_winner != Winner.None && _status != EngineStatus.Completed)
        {
            CompleteGame();
        }
    }

    private void ResolveSlash(
        CharacterState source,
        CharacterState target,
        Card slash,
        CardKind playedCardKind,
        CardConversionSource? conversionSource = null,
        IReadOnlyList<CardConversionSource>? additionalConversionSources = null)
    {
        var stillLegal = BuildLegalActions(source).Any(action =>
            action.Kind == LegalActionKind.Slash &&
            action.CardId == slash.Id &&
            action.TargetSeat == target.Seat &&
            (action.PlayedCardKind ?? slash.Kind) == playedCardKind &&
            action.ConversionSource == conversionSource &&
            ConversionSourcesEqual(action.AdditionalConversionSources, additionalConversionSources));
        if (!stillLegal)
        {
            throw new InvalidOperationException("Slash became illegal before resolution.");
        }

        ResolveSlashCore(
            source,
            target,
            slash,
            playedCardKind,
            source.Seat,
            usesZhuqueFan: playedCardKind == CardKind.FireSlash &&
                           (additionalConversionSources is null || additionalConversionSources.Count == 0) &&
                           !IsDirectProgramFireSlashConversion(source, slash, conversionSource) &&
                           (slash.Kind == CardKind.Slash ||
                            GetProgramCardIdentityMatches(source, slash).Any(match =>
                                match.Identity.OutputKind == CardKind.Slash)),
            conversionSource: conversionSource,
            additionalConversionSources: additionalConversionSources);
    }

    private void ResolveFangtianHalberdSlash(
        CharacterState source,
        IReadOnlyList<CharacterState> targets,
        Card slash,
        CardKind playedCardKind,
        CardConversionSource? conversionSource = null,
        IReadOnlyList<CardConversionSource>? additionalConversionSources = null)
    {
        var targetSeats = targets.Select(target => target.Seat).ToArray();
        var stillLegal = targetSeats.Length >= 2 && (targetSeats.Length <= 5 || HasHpLossSlashTargets(source) && targetSeats.Length < _players.Count(p => p.IsAlive)) &&
            BuildLegalActions(source).Any(action =>
                action.Kind == LegalActionKind.Slash &&
                action.CardId == slash.Id &&
                action.TargetSeats.SequenceEqual(targetSeats) &&
                (action.PlayedCardKind ?? slash.Kind) == playedCardKind &&
                action.ConversionSource == conversionSource &&
                ConversionSourcesEqual(action.AdditionalConversionSources, additionalConversionSources));
        var usesFangtian = UsesFormalFangtianHalberd &&
            GetHand(source).Count == 1 && GetHand(source)[0].Id == slash.Id &&
            HasWeaponAbility(source, CardKind.FangtianHalberd);
        var usesProgramTargetCount = HasNextCardTargetAdjustment(source) || UsesProgramCardTargetCount(
            source, slash, playedCardKind, targetSeats.Length);
        if (!stillLegal || (!usesFangtian && !usesProgramTargetCount))
        {
            throw new InvalidOperationException("Fangtian Halberd Slash became illegal before resolution.");
        }

        var ignoresArmor = HasArmorBypass(source);
        var resolutionId = BeginCardUse(
            slash,
            source.Seat,
            targetSeats,
            playedCardKind,
            ignoresArmor,
            conversionSource: conversionSource,
            additionalConversionSources: additionalConversionSources);
        var nuzhan = GetNuzhanModifiers(resolutionId, source);
        MoveCard(
            slash,
            FindOwnedCardLocation(source, slash),
            CardLocation.Processing,
            CardMoveReasons.Use);
        var countedTowardSlashLimit =
            !(LifecycleCardUse(resolutionId)?.UnlimitedUse == true) && !nuzhan.IgnoresSlashLimit && !IgnoresProgramSlashLimit(source, conversionSource) && _phase == TurnPhase.Play && source.Seat == _currentSeat;
        if (countedTowardSlashLimit)
        {
            RecordSlashUseDebit(resolutionId, source.Seat);
        }
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(source.Seat, playedCardKind);
        var damageAmount = (source.HasAlcoholEffect ? 2 : 1) + nuzhan.DamageBonus;
        CaptureProgramAlcoholConsumption(resolutionId, source);
        source.HasAlcoholEffect = false;

        var pending = new FangtianHalberdHandle(this,
            resolutionId,
            source.Seat,
            slash,
            playedCardKind,
            ignoresArmor,
            damageAmount,
            targetSeats,
            usesFangtian,
            countedTowardSlashLimit,
            conversionSource);
        ActiveFangtianHalberd = pending;
        if (usesFangtian)
        {
            AdvanceEventRulesAndQueueFact(new FangtianHalberdUsedEvent(
                resolutionId,
                source.Seat,
                slash.Id,
                playedCardKind,
                Array.AsReadOnly(targetSeats)));
        }
        var usesZhuqueFan = playedCardKind == CardKind.FireSlash &&
                            (additionalConversionSources is null || additionalConversionSources.Count == 0) &&
                            !IsDirectProgramFireSlashConversion(source, slash, conversionSource) &&
                            (slash.Kind == CardKind.Slash ||
                             GetProgramCardIdentityMatches(source, slash).Any(match =>
                                 match.Identity.OutputKind == CardKind.Slash));
        if (usesZhuqueFan)
        {
            AdvanceEventRulesAndQueueFact(new ZhuqueFanConvertedEvent(
                resolutionId,
                source.Seat,
                Array.AsReadOnly(new[] { slash.Id }),
                Array.AsReadOnly(targetSeats)));
        }
        if (usesProgramTargetCount)
        {
            var targetCountRule = EvaluateCardTargetCount(source, playedCardKind);
            AdvanceEventRulesAndQueueFact(new ProgramCardTargetCountAppliedEvent(
                resolutionId,
                source.Seat,
                playedCardKind,
                Array.AsReadOnly(targetSeats),
                Array.AsReadOnly(targetCountRule.Value.Contributions
                    .Select(contribution => contribution.SourceId)
                    .ToArray())));
        }
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(
            slash.Id,
            playedCardKind,
            source.Seat,
            targetSeats[0],
            IgnoresArmor: ignoresArmor));
        var slashName = CardCatalog.Get(playedCardKind).DisplayName;
        AddLog(
            usesFangtian && !usesProgramTargetCount ? "EquipmentEffect" : "SkillTriggered",
            $"{source.Name} 以【{slashName}】指定 {string.Join("、", targets.Select(target => target.Name))}。",
            source.Seat,
            targetSeats[0]);
        foreach (var target in targets)
        {
            NotifyAiOfSlash(source, target);
        }

        BeginNextFangtianHalberdTarget(pending);
    }

    private void BeginNextFangtianHalberdTarget(FangtianHalberdHandle pending)
    {
        if (!SameContinuationOwner(ActiveFangtianHalberd, pending))
        {
            throw new InvalidOperationException("The Fangtian Halberd continuation is no longer current.");
        }

        while (pending.TargetIndex < pending.TargetSeats.Count &&
               !_players[GetFangtianEffectiveTarget(pending, pending.TargetIndex)].IsAlive)
        {
            pending.TargetIndex++;
        }

        if (pending.TargetIndex >= pending.TargetSeats.Count)
        {
            throw new InvalidOperationException("A Fangtian Halberd use must finish through its final target attack.");
        }

        SetCardUseTargetIndex(pending.ResolutionId, pending.TargetIndex);
        SetCardUseStep(pending.ResolutionId, ResolutionFrameStep.Declared);
        var targetSeat = GetFangtianEffectiveTarget(pending, pending.TargetIndex);
        var targetIgnoresArmor = pending.IgnoresArmor ||
            HasCardArmorBypass(_players[pending.SourceSeat], _players[targetSeat], pending.EffectiveCardKind);
        var attack = new CardAttackHandle(this,
            pending.ResolutionId,
            pending.SourceSeat,
            targetSeat,
            pending.Card,
            pending.DamageAmount,
            pending.EffectiveCardKind,
            targetIgnoresArmor,
            conversionSource: pending.ConversionSource);
        if ((LifecycleCardUse(pending.ResolutionId)?.ProgramUseAccepted == true) &&
            PreparedTargetAttacks(pending.ResolutionId) is { } prepared)
            UpdateCardAttackState(pending.ResolutionId, _ => prepared[pending.TargetIndex]);
        if (LifecycleCardUse(pending.ResolutionId)?.AdjustedSlashReturn is { } adjustedReturn)
            UpdateCardAttackState(pending.ResolutionId, state => state! with { AdjustedSlashReturn = adjustedReturn,
                PhysicalCardIds = LifecycleCardUse(pending.ResolutionId)!.PhysicalCardIds! });
        if (LifecycleCardUse(pending.ResolutionId) is { HpLossMaterialSlashReturn: not null, PhysicalCardIds: { } hpMaterials })
            UpdateCardAttackState(pending.ResolutionId, state => state! with { PhysicalCardIds = hpMaterials });
        pending.CurrentAttack = attack;
        ActiveCardAttack = attack;
        AddLog(
            "CardEffect",
            $"【{(pending.UsesFangtian ? "方天画戟" : "天义")}】的【{CardCatalog.Get(pending.EffectiveCardKind).DisplayName}】开始结算 {_players[targetSeat].Name}。",
            pending.SourceSeat,
            targetSeat);
        var committedAction = _resolutionStack.OfType<CardUseFrame>()
            .Single(frame => frame.Id == attack.ResolutionId).Action;
        if (TryPauseRecoveryPaidCardUse(attack.ResolutionId, new(RecoveryPaidCardUseKind.CommittedSlash, attack.SourceSeat))) return;
        if (committedAction is not null && TryMarkProgramUseCommitted(attack.ResolutionId) &&
            TryBeginProgramCardWindow(attack, committedAction, SkillProgramTriggerWindow.CardUseCommitted,
                committedAction.TargetSeats, ProgramCardContinuation.CommittedSlash))
        {
            AdvanceRulesAndPublishState();
            return;
        }
        BeginSlashTargetResolution(attack);
    }

    private void ResolveZhangbaSlash(
        CharacterState source,
        CharacterState target,
        IReadOnlyList<Card> physicalCards)
    {
        if (!CanUseZhangbaSerpentSpear(source) ||
            physicalCards.Count != 2 ||
            physicalCards.Select(card => card.Id).Distinct().Count() != 2 ||
            physicalCards.Any(card => !IsOwnedPlayableLocation(source, _cardZones.GetLocation(card.Id))) ||
            !CanUseVirtualSlashTarget(source, target, physicalSuit:PhysicalGroupSuit(source,physicalCards), effectiveColor:PhysicalGroupColor(source,physicalCards), effectiveRank:ZhangbaSpecificSlashRank(source,physicalCards), physicalCardIds:physicalCards.Select(c=>c.Id).ToArray()))
        {
            throw new InvalidOperationException("Zhangba Serpent Spear became illegal before resolution.");
        }

        ResolveSlashCore(
            source,
            target,
            physicalCards[0],
            CardKind.Slash,
            source.Seat,
            physicalCards: physicalCards);
    }






    private void ResolveSlashCore(
        CharacterState source,
        CharacterState target,
        Card slash,
        CardKind playedCardKind,
        int physicalOwnerSeat,
        FactionCardRequestHandle? factionRequest = null,
        BorrowedSwordHandle? borrowedSword = null,
        IReadOnlyList<Card>? physicalCards = null,
        bool countsTowardSlashLimit = true,
        bool usesZhuqueFan = false,
        CardConversionSource? conversionSource = null,
        IReadOnlyList<CardConversionSource>? additionalConversionSources = null,
        long? programSkillCardUseFrameId = null,
        CardLocation? physicalSourceLocation = null)
    {
        if (physicalSourceLocation is { } deckSource &&
            (deckSource != CardLocation.DrawPile || physicalCards is not null ||
             programSkillCardUseFrameId is not { } deckParent ||
             _resolutionStack.LastOrDefault() is not ProgramSkillFrame { DeckSlashSequence: { } sequence } deckFrame ||
             deckFrame.Id != deckParent || deckFrame.OwnerSeat != source.Seat || sequence.TargetSeat != target.Seat ||
             sequence.ActiveCardId != slash.Id || sequence.DeclaredIds.LastOrDefault() != slash.Id ||
             slash.Kind != playedCardKind || !IsSlashCard(playedCardKind) || countsTowardSlashLimit ||
             _cardZones.GetLocation(slash.Id) != deckSource))
            throw new InvalidOperationException("An external Slash source requires its exact active deck sequence.");
        var slashCards = physicalCards?.ToArray() ?? [slash];
        if (slashCards.Length == 0 || slashCards[0].Id != slash.Id)
        {
            throw new InvalidOperationException("A Slash use must retain its primary physical card.");
        }
        if (usesZhuqueFan &&
            (!HasZhuqueFan(source) || playedCardKind != CardKind.FireSlash))
        {
            throw new InvalidOperationException("Zhuque Fan is no longer available for this Fire Slash conversion.");
        }
        var ignoresArmor = HasCardArmorBypass(source, target, playedCardKind);
        var resolutionId = BeginCardUse(
            slash,
            source.Seat,
            [target.Seat],
            playedCardKind,
            ignoresArmor,
            slashCards.Select(card => card.Id).ToArray(),
            conversionSource,
            additionalConversionSources, isTrueZhangbaSlash: slashCards.Length == 2 && conversionSource is null && additionalConversionSources is null && HasWeaponAbility(_players[physicalOwnerSeat], CardKind.ZhangbaSerpentSpear));
        var nuzhan = GetNuzhanModifiers(resolutionId, source);
        if (usesZhuqueFan)
        {
            AdvanceEventRulesAndQueueFact(new ZhuqueFanConvertedEvent(
                resolutionId,
                source.Seat,
                Array.AsReadOnly(slashCards.Select(card => card.Id).ToArray()),
                Array.AsReadOnly(new[] { target.Seat })));
            AddLog(
                "EquipmentEffect",
                $"{source.Name} 发动【朱雀羽扇】，将本次普通【杀】改为【火杀】使用。",
                source.Seat,
                target.Seat);
        }
        foreach (var physicalCard in slashCards)
        {
            MoveCard(
                physicalCard,
                physicalSourceLocation ?? FindOwnedCardLocation(_players[physicalOwnerSeat], physicalCard),
                CardLocation.Processing,
                CardMoveReasons.Use);
        }
        var countedTowardSlashLimit = countsTowardSlashLimit && !(LifecycleCardUse(resolutionId)?.UnlimitedUse == true) && !nuzhan.IgnoresSlashLimit && !IgnoresProgramSlashLimit(source, conversionSource) &&
                                     _phase == TurnPhase.Play && source.Seat == _currentSeat;
        if (countedTowardSlashLimit)
        {
            RecordSlashUseDebit(resolutionId, source.Seat);
        }
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(source.Seat, playedCardKind);
        var damageAmount = (source.HasAlcoholEffect ? 2 : 1) + nuzhan.DamageBonus;
        CaptureProgramAlcoholConsumption(resolutionId, source);
        source.HasAlcoholEffect = false;
        var attack = new CardAttackHandle(this,
            resolutionId,
            source.Seat,
            target.Seat,
            slash,
            damageAmount,
            playedCardKind,
            ignoresArmor,
            physicalCards: slashCards,
            conversionSource: conversionSource,
            programSkillCardUseFrameId: factionRequest?.ProgramSkillFrameId ?? programSkillCardUseFrameId);
        if (factionRequest is not null)
        {
            factionRequest.ActiveAttack = attack;
        }
        if (borrowedSword is not null)
        {
            borrowedSword.ActiveAttack = attack;
        }
        ActiveCardAttack = attack;
        var slashName = CardCatalog.Get(playedCardKind).DisplayName;
        var useDescription = PublicDeclarationDescription(slash, conversionSource is not null && slashCards.Length > 1
            ? $"发动【{ProgramConversionName(conversionSource)}】，将 {slashCards.Length} 张手牌当作【杀】使用"
            : slashCards.Length == 2
            ? "发动【丈八蛇矛】，将两张手牌当作【杀】使用"
            : playedCardKind == slash.Kind
            ? $"使用【{slashName}】"
            : $"将【{CardCatalog.Get(slash.Kind).DisplayName}】当作【{slashName}】使用");
        AddLog("CardUsed", $"{source.Name} 对 {target.Name}{useDescription}。", source.Seat, target.Seat);
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(
            slash.Id,
            playedCardKind,
            source.Seat,
            target.Seat,
            IgnoresArmor: ignoresArmor));
        if (conversionSource is not null && slashCards.Length > 1)
        {
            AdvanceEventRulesAndQueueFact(new ProgramViewAsConvertedEvent(
                resolutionId,
                conversionSource.SkillId,
                conversionSource.BindingId,
                source.Seat,
                Array.AsReadOnly(slashCards.Select(card => card.Id).ToArray()),
                playedCardKind,
                IsUse: true,
                [target.Seat]));
        }
        else if (slashCards.Length == 2)
        {
            AdvanceEventRulesAndQueueFact(new ZhangbaSerpentSpearConvertedEvent(
                resolutionId,
                source.Seat,
                Array.AsReadOnly(slashCards.Select(card => card.Id).ToArray()),
                IsUse: true,
                target.Seat));
        }
        NotifyAiOfSlash(source, target);

        var committedAction = _resolutionStack.OfType<CardUseFrame>()
            .Single(frame => frame.Id == attack.ResolutionId).Action;
        if (TryPauseRecoveryPaidCardUse(attack.ResolutionId, new(RecoveryPaidCardUseKind.CommittedSlash, attack.SourceSeat))) return;
        if (committedAction is not null && TryMarkProgramUseCommitted(attack.ResolutionId) &&
            TryBeginProgramCardWindow(attack, committedAction, SkillProgramTriggerWindow.CardUseCommitted,
                committedAction.TargetSeats, ProgramCardContinuation.CommittedSlash))
        {
            AdvanceRulesAndPublishState();
            return;
        }
        BeginSlashTargetResolution(attack);
    }

    private void BeginSlashTargetResolution(CardAttackHandle attack)
    {
        if (TryPauseRecoveryPaidCardUse(attack.ResolutionId, new(RecoveryPaidCardUseKind.SlashTarget, attack.SourceSeat))) return;
        CaptureProgramAdjustedSlashBaseDamage(attack);
        if (TryBeginProgramSlashStage(attack, SkillProgramTriggerWindow.SlashTargetRedirecting,
                ProgramCardContinuation.SlashTargetRedirecting))
        {
            AdvanceRulesAndPublishState();
            return;
        }

        ContinueSlashAfterRedirectPrograms(attack);
    }

    private void ContinueSlashAfterRedirectPrograms(CardAttackHandle attack)
    {
        if (PrepareProgramSlashTargets(attack)) return;
        ContinueSlashAfterFinalizedTargets(attack);
    }

    private void ContinueSlashAfterFinalizedTargets(CardAttackHandle attack)
    {
        FreezeNextSlashDamage(attack.ResolutionId);
        var currentUse = _resolutionStack.OfType<CardUseFrame>().Single(frame => frame.Id == attack.ResolutionId);
        if (currentUse.SourceSeat != attack.CardUserSeat) attack.ReplaceCardUser(currentUse.SourceSeat);
        if (HasCurrentCardEnhancement(attack.ResolutionId, CurrentCardEnhancement.IgnoreArmor)) attack.SetIgnoresArmor(true);
        if (TryBeginSlashTargetPenalties(attack)) { AdvanceRulesAndPublishState(); return; }
        if (TryBeginSlashTargetBenefits(attack)) { AdvanceRulesAndPublishState(); return; }
        if (TryBeginActualUseTargetPrograms(attack, ActualUseTargetReturnKind.Slash))
        { AdvanceRulesAndPublishState(); return; }
        if (TryBeginProgramCardUseBeforeTargetEffects(attack))
        {
            AdvanceRulesAndPublishState();
            return;
        }

        ContinueSlashAfterProgramTargetEffects(attack);
    }

    private void ContinueSlashAfterProgramTargetEffects(CardAttackHandle attack)
    {
        if (HasDirectedCardArmorBypass(attack.ResolutionId, attack.TargetSeat))
            attack.SetIgnoresArmor(true);
        if (IsCardEffectIneffective(attack.ResolutionId, attack.TargetSeat))
        {
            SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect);
            CompleteAttack(attack);
            return;
        }

        if (TryBeginProgramSlashStage(attack, SkillProgramTriggerWindow.SlashBeforeResponse,
                ProgramCardContinuation.SlashBeforeResponse))
        {
            AdvanceRulesAndPublishState();
            return;
        }
        ContinueSlashAfterResponsePrograms(attack);
    }

    private void ContinueSlashAfterResponsePrograms(CardAttackHandle attack) =>
        ContinueSlashDefense(attack);






    private void ContinueSlashDefense(CardAttackHandle attack)
    {
        if (!SameAttackOwner(ActiveCardAttack, attack))
        {
            throw new InvalidOperationException("The response-stage continuation does not own the current Slash.");
        }

        if (HasWeaponAbility(_players[attack.CardUserSeat], CardKind.ScarletBloodSword))
            attack.ProhibitTargetHandResponses();

        if (TryBeginCixiongDoubleSwordsChoice(attack))
        {
            return;
        }

        var source = _players[attack.SourceSeat];
        var target = _players[attack.TargetSeat];
        var slash = attack.Card;
        var playedCardKind = attack.EffectiveCardKind ?? slash?.Kind ??
            throw new InvalidOperationException("A Slash continuation must retain an effective card kind.");
        var ignoresArmor = attack.IgnoresArmor;
        var resolutionId = attack.ResolutionId;
        var slashName = CardCatalog.Get(playedCardKind).DisplayName;

        if (ApplyComparedBlackSlashPolicies(attack))
        {
            SetCardUseStep(resolutionId, ResolutionFrameStep.ResolvingEffect);
            CompleteAttack(attack); return;
        }

        if (attack.SuccessfulDodgeResponses == 0)
        {
            attack.SetRequiredDodgeResponses(GetRequiredResponseCount(
                source,
                source.Seat,
                target.Seat,
                playedCardKind,
                CardKind.Dodge));
        }

        if (!ignoresArmor &&
            !attack.IsTwoCardVirtualSlash &&
            slash?.Suit is Suit.Spade or Suit.Club &&
            HasBlackSlashBarrier(target))
        {
            AddLog(
                "ArmorEffect",
                $"{target.Name} 的【仁王盾】令这次黑色【{slashName}】无效。",
                target.Seat,
                source.Seat);
            AdvanceEventRulesAndQueueFact(new ArmorEffectAppliedEvent(
                resolutionId,
                CardKind.RenwangShield,
                source.Seat,
                target.Seat,
                playedCardKind));
            SetCardUseStep(resolutionId, ResolutionFrameStep.ResolvingEffect);
            CompleteAttack(attack);
            return;
        }

        if (HasCardPolicy(target, SkillProgramCardPolicyKind.NullifyBlackSlashWithoutArmor,
                playedCardKind) &&
            CanYizhongNullify(attack.PhysicalCards, HasArmor(target)))
        {
            AddLog(
                "SkillTriggered",
                $"{target.Name} 的【毅重】令这次黑色【{slashName}】无效。",
                target.Seat,
                source.Seat);
            AdvanceEventRulesAndQueueFact(new YizhongNullifiedEvent(
                resolutionId,
                source.Seat,
                target.Seat,
                playedCardKind));
            SetCardUseStep(resolutionId, ResolutionFrameStep.ResolvingEffect);
            CompleteAttack(attack);
            return;
        }

        if (UsesFormalTengjia &&
            !ignoresArmor &&
            playedCardKind == CardKind.Slash &&
            HasTengjia(target))
        {
            AddLog("ArmorEffect", $"{target.Name} 的【藤甲】令这次普通【杀】无效。", target.Seat, source.Seat);
            AdvanceEventRulesAndQueueFact(new ArmorEffectAppliedEvent(
                resolutionId, CardKind.Tengjia, source.Seat, target.Seat, playedCardKind));
            SetCardUseStep(resolutionId, ResolutionFrameStep.ResolvingEffect);
            CompleteAttack(attack);
            return;
        }

        if (IsYingboUnrespondable(resolutionId) || IsIssuedCardUnrespondable(resolutionId))
        {
            SetCardUseStep(resolutionId, ResolutionFrameStep.ResolvingEffect);
            if (!ApplyAttackDamage(attack))
            {
                CompleteAttack(attack);
            }
            return;
        }

        var nearbyResponseProhibited = IsNearbyTargetResponseProhibited(
            attack.CardUserSeat, target.Seat, playedCardKind, [target.Seat]);
        var suitResponseProhibited = IsSuitSlashResponseProhibited(attack);
        var ghostBladeProhibited =
            HasWeaponAbility(_players[attack.CardUserSeat], CardKind.GhostDragonCrescentBlade) &&
            attack.PhysicalCards.Count > 0 && attack.PhysicalCards.All(card =>
                IsRedSuit(EffectiveSuit(_players[attack.CardUserSeat], card)));
        if (attack.ProhibitsDodge || nearbyResponseProhibited || suitResponseProhibited || ghostBladeProhibited)
        {
            var prohibitingSkills = attack.ResponseProhibitingSkillNames
                .Concat(nearbyResponseProhibited ? ["伏骑"] : [])
                .Concat(suitResponseProhibited ? ["武神"] : [])
                .Concat(ghostBladeProhibited ? ["鬼龙斩月刀"] : [])
                .Distinct(StringComparer.Ordinal)
                .Select(name => $"【{name}】");
            AddLog(
                "SkillTriggered",
                $"{source.Name} 的{string.Join("、", prohibitingSkills)}令 {target.Name} 不能使用【闪】响应此【{slashName}】。",
                source.Seat,
                target.Seat);
            SetCardUseStep(resolutionId, ResolutionFrameStep.ResolvingEffect);
            if (!ApplyAttackDamage(attack))
            {
                CompleteAttack(attack);
            }
            return;
        }

        var dodges = GetResponseCards(target, CardKind.Dodge);
        if (ConvertedSlashSameColorResponseColor(attack) is { } requireRed)
            dodges = dodges.Where(card => IsRedSuit(EffectiveSuit(target, card)) == requireRed).ToList();
        var hasBagua = !ignoresArmor && HasBagua(target) && !HasIssuedPlayPhaseUseBan(target.Seat);
        var dodge = dodges.FirstOrDefault();
        var canRequestFactionDefense = CanRequestFactionDefense(target, attack);
        var responseOrdinal = attack.SuccessfulDodgeResponses + 1;
        var responsePrompt = attack.RequiredDodgeResponses > 1
            ? $"{source.Name} 的【无双】要求你打出第 {responseOrdinal} 张【闪】，是否响应？"
            : $"{source.Name} 对你使用了【{slashName}】，是否打出【闪】？";
        if ((dodges.Count > 0 || hasBagua || canRequestFactionDefense || ProgramDodgeResponseChoices(target).Any() || HasRequestedDeckBasicSource(target, CardKind.Dodge)) && target.IsHuman)
        {
            PushResponseWindow(
                resolutionId,
                source.Seat,
                target.Seat,
                playedCardKind,
                CardKind.Dodge);
            _pendingDecision = new PendingDecision(
                DecisionKind.RespondDodge,
                target.Seat,
                responsePrompt,
                dodges.Select(card => card.Id).ToArray(),
                [],
                source.Seat,
                playedCardKind)
            {
                PromptId = CreatePromptId(),
                Choices = CreateResponseChoices(
                    dodges,
                    CardKind.Dodge,
                    playedCardKind,
                    card => GetEffectiveResponseKind(target, card, CardKind.Dodge),
                    includeBagua: hasBagua,
                    includeFactionDefense: canRequestFactionDefense,
                    responder: target),
                RequiredCardKind = CardKind.Dodge
            };
            _status = EngineStatus.AwaitingHumanResponse;
            AdvanceEventRulesAndQueueFact(new ResponseRequestedEvent(
                source.Seat,
                target.Seat,
                playedCardKind,
                CardKind.Dodge));
            AdvanceRulesAndPublishState();
            return;
        }

        if (dodge is not null || hasBagua || canRequestFactionDefense || ProgramDodgeResponseChoices(target).Any() || HasRequestedDeckBasicSource(target, CardKind.Dodge))
        {
            // Choosing to respond is a separate continuation. Advance() consumes it
            // immediately, while AdvanceOneStep() exposes it as the next AI decision.
            PushResponseWindow(
                resolutionId,
                source.Seat,
                target.Seat,
                playedCardKind,
                CardKind.Dodge);
            _pendingDecision = new PendingDecision(
                DecisionKind.RespondDodge,
                target.Seat,
                attack.RequiredDodgeResponses > 1
                    ? $"{source.Name} 的【无双】要求 {target.Name} 打出第 {responseOrdinal} 张【闪】，AI 将选择是否响应。"
                    : $"{source.Name} 对 {target.Name} 使用了【{slashName}】，AI 将选择是否打出【闪】。",
                dodges.Select(card => card.Id).ToArray(),
                [],
                source.Seat,
                playedCardKind)
            {
                PromptId = CreatePromptId(),
                Choices = CreateResponseChoices(
                    dodges,
                    CardKind.Dodge,
                    playedCardKind,
                    card => GetEffectiveResponseKind(target, card, CardKind.Dodge),
                    includeBagua: hasBagua,
                    includeFactionDefense: canRequestFactionDefense,
                    responder: target),
                RequiredCardKind = CardKind.Dodge
            };
            AdvanceEventRulesAndQueueFact(new ResponseRequestedEvent(
                source.Seat,
                target.Seat,
                playedCardKind,
                CardKind.Dodge));
            return;
        }

        SetCardUseStep(resolutionId, ResolutionFrameStep.ResolvingEffect);
        if (!ApplyAttackDamage(attack))
        {
            CompleteAttack(attack);
        }
    }

    private bool TryBeginCixiongDoubleSwordsChoice(CardAttackHandle attack)
    {
        var source = _players[attack.SourceSeat];
        var target = _players[attack.TargetSeat];
        if (!UsesFormalCixiongDoubleSwords ||
            attack.CixiongDoubleSwordsResolved ||
            !source.IsAlive ||
            !target.IsAlive ||
            source.Gender == target.Gender ||
            !HasWeaponAbility(source, CardKind.CixiongDoubleSwords))
        {
            return false;
        }

        if (ActiveCixiongDoubleSwords is not null)
        {
            throw new InvalidOperationException("Only one Cixiong Double Swords choice may be active.");
        }

        ActiveCixiongDoubleSwords = new CixiongDoubleSwordsHandle(this, attack);
        _pendingDecision = new PendingDecision(
            DecisionKind.CixiongDoubleSwords,
            source.Seat,
            $"你对异性角色 {target.Name} 使用了【杀】，是否发动【雌雄双股剑】？",
            [],
            [],
            source.Seat,
            attack.EffectiveCardKind)
        {
            PromptId = CreatePromptId(),
            TargetSeat = target.Seat,
            Choices =
            [
                new PromptChoice(
                    new ChoiceId("cixiong.activate"),
                    $"发动【雌雄双股剑】，令 {target.Name} 弃一张手牌或令你摸一张牌。",
                    [],
                    [],
                    new Dictionary<string, string> { ["action"] = "cixiong-use" }),
                new PromptChoice(
                    new ChoiceId("cixiong.skip"),
                    "不发动【雌雄双股剑】。",
                    [],
                    [],
                    new Dictionary<string, string> { ["action"] = "cixiong-skip" })
            ]
        };
        _status = source.IsHuman
            ? EngineStatus.AwaitingHumanResponse
            : EngineStatus.Running;
        return true;
    }

    private void ResolveCixiongDoubleSwordsChoice(PromptChoice selected)
    {
        var pending = ActiveCixiongDoubleSwords ??
            throw new InvalidOperationException("There is no Cixiong Double Swords choice to resolve.");
        var attack = pending.Attack;
        if (!SameAttackOwner(ActiveCardAttack, attack) ||
            _pendingDecision is not { Kind: DecisionKind.CixiongDoubleSwords } decision ||
            !selected.Parameters.TryGetValue("action", out var action))
        {
            throw new InvalidOperationException("The Cixiong Double Swords continuation is inconsistent.");
        }

        var source = _players[attack.SourceSeat];
        var target = _players[attack.TargetSeat];
        if (pending.Stage == CixiongDoubleSwordsStage.SourceActivation)
        {
            if (decision.PlayerSeat != source.Seat || action is not ("cixiong-use" or "cixiong-skip"))
            {
                throw new InvalidOperationException("The Cixiong activation choice is not current.");
            }

            if (action == "cixiong-skip")
            {
                CompleteCixiongDoubleSwords(
                    pending,
                    activated: false,
                    targetDiscarded: false,
                    discardedCardId: null,
                    sourceDrawCount: 0);
                return;
            }

            pending.Stage = CixiongDoubleSwordsStage.TargetChoice;
            var hand = GetHand(target).OrderBy(card => card.Id).ToArray();
            var choices = hand.Select(card => new PromptChoice(
                    new ChoiceId($"cixiong.discard.card-{card.Id}"),
                    $"弃置【{card.DisplayName}】（{card.RankText}）。",
                    [card.Id],
                    [],
                    new Dictionary<string, string> { ["action"] = "cixiong-discard" }))
                .Append(new PromptChoice(
                    new ChoiceId("cixiong.allow-draw"),
                    $"令 {source.Name} 摸一张牌。",
                    [],
                    [],
                    new Dictionary<string, string> { ["action"] = "cixiong-draw" }))
                .ToArray();
            _pendingDecision = new PendingDecision(
                DecisionKind.CixiongDoubleSwords,
                target.Seat,
                $"{source.Name} 发动【雌雄双股剑】，请选择弃置一张手牌，或令其摸一张牌。",
                hand.Select(card => card.Id).ToArray(),
                [],
                source.Seat,
                attack.EffectiveCardKind)
            {
                PromptId = CreatePromptId(),
                TargetSeat = target.Seat,
                Choices = choices
            };
            _status = target.IsHuman
                ? EngineStatus.AwaitingHumanResponse
                : EngineStatus.Running;
            return;
        }

        if (decision.PlayerSeat != target.Seat ||
            pending.Stage != CixiongDoubleSwordsStage.TargetChoice)
        {
            throw new InvalidOperationException("The Cixiong target choice is not current.");
        }

        if (action == "cixiong-discard")
        {
            if (selected.Cards.Count != 1)
            {
                throw new InvalidOperationException("Cixiong requires exactly one discarded hand card.");
            }
            var card = GetHand(target).SingleOrDefault(candidate => candidate.Id == selected.Cards[0]) ??
                throw new InvalidOperationException("The selected Cixiong discard is no longer in hand.");
            MoveCard(
                card,
                CardLocation.Hand(target.Seat),
                CardLocation.DiscardPile,
                CardMoveReasons.CixiongDiscard);
            CompleteCixiongDoubleSwords(
                pending,
                activated: true,
                targetDiscarded: true,
                discardedCardId: card.Id,
                sourceDrawCount: 0);
            return;
        }

        if (action != "cixiong-draw" || selected.Cards.Count != 0)
        {
            throw new InvalidOperationException("The Cixiong target choice is invalid.");
        }
        var drawn = DrawCards(source, 1, log: true, CardMoveReasons.CixiongDraw);
        CompleteCixiongDoubleSwords(
            pending,
            activated: true,
            targetDiscarded: false,
            discardedCardId: null,
            sourceDrawCount: drawn.Count);
    }

    private void CompleteCixiongDoubleSwords(
        CixiongDoubleSwordsHandle pending,
        bool activated,
        bool targetDiscarded,
        int? discardedCardId,
        int sourceDrawCount)
    {
        var attack = pending.Attack;
        var source = _players[attack.SourceSeat];
        var target = _players[attack.TargetSeat];
        attack.MarkCixiongDoubleSwordsResolved();
        ActiveCixiongDoubleSwords = null;
        ClearPendingDecision();
        AdvanceEventRulesAndQueueFact(new CixiongDoubleSwordsResolvedEvent(
            attack.ResolutionId,
            source.Seat,
            target.Seat,
            activated,
            targetDiscarded,
            discardedCardId,
            sourceDrawCount));
        AddLog(
            activated ? "EquipmentEffect" : "EquipmentSkipped",
            !activated
                ? $"{source.Name} 未发动【雌雄双股剑】。"
                : targetDiscarded
                    ? $"{source.Name} 发动【雌雄双股剑】，{target.Name} 弃置了一张手牌。"
                    : $"{source.Name} 发动【雌雄双股剑】并摸了一张牌。",
            source.Seat,
            target.Seat);
        ContinueSlashDefense(attack);
    }

    private void ResolveDuel(CharacterState source, CharacterState target, Card duel, CardKind? playedCardKind = null)
    {
        var stillLegal = BuildLegalActions(source).Any(action =>
            action.Kind == LegalActionKind.Duel &&
            action.CardId == duel.Id &&
            action.TargetSeat == target.Seat &&
            action.PlayedCardKind == playedCardKind);
        if (!stillLegal)
        {
            throw new InvalidOperationException("Duel became illegal before resolution.");
        }

        var resolutionId = BeginCardUse(duel, source.Seat, [target.Seat], playedCardKind);
        MoveCard(
            duel,
            FindOwnedCardLocation(source, duel),
            CardLocation.Processing,
            CardMoveReasons.Use);
        BeginJizhiOrNullificationWindow(
            resolutionId,
            duel,
            source.Seat,
            [target.Seat],
            LegalActionKind.Duel,
            playedCardKind: playedCardKind);
    }

    private void BeginDuelResponse(DuelHandle duel)
    {
        if (!SameContinuationOwner(ActiveDuel, duel))
        {
            throw new InvalidOperationException("The Duel response is not the current card resolution.");
        }

        var responder = _players[duel.ResponderSeat];
        var opponent = _players[duel.OpponentSeat];
        if (IsYingboUnrespondable(duel.ResolutionId) || IsIssuedCardUnrespondable(duel.ResolutionId) ||
            IsNearbyTargetResponseProhibited(duel.Attack.CardUserSeat, responder.Seat,
                CardKind.Duel, [duel.TargetSeat]))
        {
            SetResponseParentStep(duel.ResolutionId, ResolutionFrameStep.ResolvingEffect);
            _pendingDecision = null;
            ResolveDuelResponse(duel, responder, selectedSlash: null);
            return;
        }

        var requiredSlashResponses = GetRequiredResponseCount(
            opponent,
            opponent.Seat,
            responder.Seat,
            CardKind.Duel,
            CardKind.Slash);
        var responseOrdinal = duel.SuccessfulSlashResponses + 1;
        var slashes = GetResponseCards(responder, CardKind.Slash);
        var zhangbaPairs = GetZhangbaSlashPairs(responder);
        var programMultiCardResponses = GetProgramMultiCardViewAsSelections(
            responder, CardKind.Slash, forResponse: true);
        var canRequestFactionSlash = CanRequestFactionSlashResponse(responder, duel.Attack);
        if (slashes.Count == 0 && zhangbaPairs.Count == 0 && programMultiCardResponses.Count == 0 && !canRequestFactionSlash && !HasRequestedDeckBasicSource(responder, CardKind.Slash))
        {
            if (ActiveCardAttack is null)
            {
                throw new InvalidOperationException("A Duel response has no active damage source.");
            }
            SetResponseParentStep(duel.ResolutionId, ResolutionFrameStep.ResolvingEffect);
            _pendingDecision = null;
            ResolveDuelResponse(duel, responder, selectedSlash: null);
            return;
        }

        PushResponseWindow(
            duel.ResolutionId,
            duel.OpponentSeat,
            responder.Seat,
            CardKind.Duel,
            CardKind.Slash);
        _pendingDecision = new PendingDecision(
            DecisionKind.RespondSlash,
            responder.Seat,
            requiredSlashResponses > 1
                ? $"{opponent.Name} 的【无双】要求你打出第 {responseOrdinal} 张【杀】响应【决斗】，是否响应？"
                : $"{opponent.Name} 对你使用了【决斗】，是否打出【杀】？",
            slashes.Select(card => card.Id)
                .Concat(zhangbaPairs.SelectMany(pair => pair.Select(card => card.Id)))
                .Concat(programMultiCardResponses.SelectMany(item => item.Cards.Select(card => card.Id)))
                .Distinct()
                .ToArray(),
            [],
            duel.OpponentSeat,
            CardKind.Duel)
        {
            PromptId = CreatePromptId(),
            Choices = CreateResponseChoices(
                slashes,
                CardKind.Slash,
                effectiveCardKindSelector: card =>
                    GetEffectiveResponseKind(responder, card, CardKind.Slash),
                includeFactionSlash: canRequestFactionSlash,
                responder: responder),
            RequiredCardKind = CardKind.Slash
        };
        AdvanceEventRulesAndQueueFact(new ResponseRequestedEvent(
            duel.OpponentSeat,
            responder.Seat,
            CardKind.Duel,
            CardKind.Slash));
        _status = responder.IsHuman
            ? EngineStatus.AwaitingHumanResponse
            : EngineStatus.Running;
    }

    private void ResolveDuelResponse(
        DuelHandle duel,
        CharacterState responder,
        Card? selectedSlash)
    {
        if (selectedSlash is {} declaredSlash && TryBeginCardDeclaration(responder, declaredSlash, CardKind.Slash,
            PeekDeclarationConversion(responder, declaredSlash, CardKind.Slash, false),
            DeclarationReturn(CardDeclarationPurpose.Duel, responder.Seat), [duel.OpponentSeat])) return;
        if (!SameContinuationOwner(ActiveDuel, duel) || responder.Seat != duel.ResponderSeat)
        {
            throw new InvalidOperationException("The Duel response does not belong to the current responder.");
        }

        if (selectedSlash is { } slash)
        {
            var responseCardKind = GetEffectiveResponseKind(
                responder,
                slash,
                CardKind.Slash);
            var responseConversion = GetSelectedResponseConversion(responder, slash, responseCardKind);
            var responseFrom = FindOwnedCardLocation(responder, slash);
        var responseCostIsRed = CapturePhysicalCardColor(responder.Seat,slash);
            MoveCard(
                slash,
                responseFrom,
                CardLocation.Processing,
                CardMoveReasons.Respond);
            var responseName = CardCatalog.Get(responseCardKind).DisplayName;
            var responseDescription = PublicDeclarationDescription(slash, IsNativeResponseCard(slash, CardKind.Slash)
                ? $"打出【{responseName}】"
                : $"将【{slash.DisplayName}】当作【{responseName}】");
            AddLog(
                "CardResponded",
                $"{responder.Name} {responseDescription}应战【决斗】。",
                responder.Seat,
                duel.OpponentSeat);
            AdvanceEventRulesAndQueueFact(new CardRespondedEvent(
                slash.Id,
                responder.Seat,
                duel.OpponentSeat,
                responseCardKind));
            MarkSlashUsedOrPlayedDuringCurrentPlayPhase(responder.Seat, responseCardKind);
            AdvanceEventRulesAndQueueFact(new DuelResponseEvent(
                duel.ResolutionId,
                responder.Seat,
                UsedSlash: true,
                SlashCardId: slash.Id,
                ResponseCardKind: responseCardKind));
            var responseCost = new CardActionCost(slash.Id, slash.Kind, responseFrom,responseCostIsRed);
            if (TryBeginCardResponsePrograms(
                    duel.Attack,
                    responder,
                    responder,
                    requesterSeat: null,
                    duel.OpponentSeat,
                    responseCardKind,
                    [responseCost],
                    ProgramCardContinuation.DuelSlash,
                    responseConversion))
            {
                return;
            }
            MoveCard(
                slash,
                CardLocation.Processing,
                CardLocation.DiscardPile,
                CardMoveReasons.ResponseFinished);
            ContinueDuelAfterSuccessfulSlash(duel, responder.Seat);
            return;
        }

        AdvanceEventRulesAndQueueFact(new DuelResponseEvent(
            duel.ResolutionId,
            responder.Seat,
            UsedSlash: false,
            SlashCardId: null));
        var attack = ActiveCardAttack ??
            throw new InvalidOperationException("A failed Duel response has no active damage source.");
        {
            attack.SetDamageParticipants(
                sourceSeat: duel.OpponentSeat,
                targetSeat: responder.Seat);
        }
        if (!ApplyAttackDamage(attack))
        {
            CompleteAttack(attack);
        }
    }

    private void ResolveDuelZhangbaResponse(
        DuelHandle duel,
        CharacterState responder,
        IReadOnlyList<Card> pair)
    {
        if (!SameContinuationOwner(ActiveDuel, duel) || responder.Seat != duel.ResponderSeat)
        {
            throw new InvalidOperationException("The Zhangba Duel response does not belong to the current responder.");
        }

        MoveZhangbaResponseCards(responder, pair, duel.ResolutionId, duel.OpponentSeat);
        AddLog(
            "CardResponded",
            $"{responder.Name} 发动【丈八蛇矛】，将两张手牌当【杀】应战【决斗】。",
            responder.Seat,
            duel.OpponentSeat);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(responder.Seat, CardKind.Slash);
        AdvanceEventRulesAndQueueFact(new DuelResponseEvent(
            duel.ResolutionId,
            responder.Seat,
            UsedSlash: true,
            SlashCardId: pair[0].Id,
            ResponseCardKind: CardKind.Slash));
        FinishZhangbaResponseCards(pair);
        ContinueDuelAfterSuccessfulSlash(duel, responder.Seat);
    }

    private void ContinueDuelAfterSuccessfulSlash(DuelHandle duel, int responderSeat)
    {
        var skillOwner = _players[duel.OpponentSeat];
        var requiredSlashResponses = GetRequiredResponseCount(
            skillOwner,
            skillOwner.Seat,
            responderSeat,
            CardKind.Duel,
            CardKind.Slash);
        var completedResponseSet = duel.RegisterSlashResponse(requiredSlashResponses);
        if (requiredSlashResponses > 1)
        {
            AdvanceEventRulesAndQueueFact(new RequiredResponseProgressEvent(
                duel.ResolutionId,
                skillOwner.Seat,
                responderSeat,
                CardKind.Duel,
                CardKind.Slash,
                completedResponseSet ? requiredSlashResponses : duel.SuccessfulSlashResponses,
                requiredSlashResponses));
        }

        BeginDuelResponse(duel);
    }

    private void ResolveGroupResponse(
        GroupCardHandle group,
        CharacterState responder,
        Card? selectedResponse)
    {
        if (selectedResponse is {} declaredResponse && TryBeginCardDeclaration(responder, declaredResponse, group.RequiredCardKind!.Value,
            PeekDeclarationConversion(responder, declaredResponse, group.RequiredCardKind.Value, false),
            DeclarationReturn(CardDeclarationPurpose.Group, responder.Seat), [group.SourceSeat])) return;
        if (!SameContinuationOwner(ActiveGroupCard, group) ||
            group.Effect != GroupCardEffect.ResponseAttack ||
            group.CurrentAttack is not { } attack ||
            responder.Seat != attack.TargetSeat)
        {
            throw new InvalidOperationException(
                "The group attack response does not belong to the current target.");
        }

        var requiredCardKind = group.RequiredCardKind ??
            throw new InvalidOperationException("A group response attack must declare a required card kind.");

        if (selectedResponse is { } responseCard)
        {
            var responseCardKind = GetEffectiveResponseKind(
                responder,
                responseCard,
                requiredCardKind);
            var responseConversion = GetSelectedResponseConversion(responder, responseCard, responseCardKind);
            var responseFrom = FindOwnedCardLocation(responder, responseCard);
        var responseCostIsRed = CapturePhysicalCardColor(responder.Seat,responseCard);

            PaySingleCardResponse(responseCard, responder, responseCardKind, responseConversion);
            var incomingName = CardCatalog.Get(group.Card.Kind).DisplayName;
            var responseName = CardCatalog.Get(responseCardKind).DisplayName;
            var responseDescription = PublicDeclarationDescription(responseCard, IsNativeResponseCard(responseCard, requiredCardKind)
                ? $"打出【{responseName}】"
                : $"将【{responseCard.DisplayName}】当作【{responseName}】");
            AddLog(
                "CardResponded",
                $"{responder.Name} {responseDescription}响应【{incomingName}】。",
                responder.Seat,
                group.SourceSeat);
            AdvanceEventRulesAndQueueFact(new CardRespondedEvent(
                responseCard.Id,
                responder.Seat,
                group.SourceSeat,
                responseCardKind));
            MarkSlashUsedOrPlayedDuringCurrentPlayPhase(responder.Seat, responseCardKind);
            AdvanceEventRulesAndQueueFact(new GroupResponseEvent(
                group.ResolutionId,
                group.Card.Kind,
                requiredCardKind,
                responder.Seat,
                UsedResponse: true,
                ResponseCardId: responseCard.Id,
                ResponseCardKind: responseCardKind));
            var responseCost = new CardActionCost(responseCard.Id, responseCard.Kind, responseFrom,responseCostIsRed);
            if (TryBeginCardResponsePrograms(
                    attack,
                    responder,
                    responder,
                    requesterSeat: null,
                    group.SourceSeat,
                    responseCardKind,
                    [responseCost],
                    ProgramCardContinuation.GroupResponse,
                    responseConversion))
            {
                return;
            }
            FinishSingleCardResponse(responseCard, responseConversion);
            CompleteAttack(attack);
            return;
        }

        AdvanceEventRulesAndQueueFact(new GroupResponseEvent(
            group.ResolutionId,
            group.Card.Kind,
            requiredCardKind,
            responder.Seat,
            UsedResponse: false,
            ResponseCardId: null));
        if (!ApplyAttackDamage(attack))
        {
            CompleteAttack(attack);
        }
    }

    private void ResolveGroupZhangbaResponse(
        GroupCardHandle group,
        CharacterState responder,
        IReadOnlyList<Card> pair)
    {
        if (!SameContinuationOwner(ActiveGroupCard, group) ||
            group.Effect != GroupCardEffect.ResponseAttack ||
            group.RequiredCardKind != CardKind.Slash ||
            group.CurrentAttack is not { } attack ||
            responder.Seat != attack.TargetSeat)
        {
            throw new InvalidOperationException("The Zhangba group response is not current.");
        }

        MoveZhangbaResponseCards(responder, pair, group.ResolutionId, group.SourceSeat);
        AddLog(
            "CardResponded",
            $"{responder.Name} 发动【丈八蛇矛】，将两张手牌当【杀】响应【{group.Card.DisplayName}】。",
            responder.Seat,
            group.SourceSeat);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(responder.Seat, CardKind.Slash);
        AdvanceEventRulesAndQueueFact(new GroupResponseEvent(
            group.ResolutionId,
            group.Card.Kind,
            CardKind.Slash,
            responder.Seat,
            UsedResponse: true,
            ResponseCardId: pair[0].Id,
            ResponseCardKind: CardKind.Slash));
        FinishZhangbaResponseCards(pair);
        CompleteAttack(attack);
    }

    private void MoveZhangbaResponseCards(
        CharacterState responder,
        IReadOnlyList<Card> pair,
        long resolutionId,
        int responseTargetSeat)
    {
        if (FindZhangbaSlashPair(responder, pair.Select(card => card.Id).ToArray()) is null)
        {
            throw new InvalidOperationException("The Zhangba Slash pair is no longer legal.");
        }

        foreach (var card in pair)
        {
            MoveCard(
                card,
                FindOwnedCardLocation(responder, card),
                CardLocation.Processing,
                CardMoveReasons.Respond);
            AdvanceEventRulesAndQueueFact(new CardRespondedEvent(
                card.Id,
                responder.Seat,
                responseTargetSeat,
                CardKind.Slash));
        }
        AdvanceEventRulesAndQueueFact(new ZhangbaSerpentSpearConvertedEvent(
            resolutionId,
            responder.Seat,
            Array.AsReadOnly(pair.Select(card => card.Id).ToArray()),
            IsUse: false,
            responseTargetSeat));
    }

    private void FinishZhangbaResponseCards(IReadOnlyList<Card> pair)
    {
        foreach (var card in pair)
        {
            MoveCard(
                card,
                CardLocation.Processing,
                CardLocation.DiscardPile,
                CardMoveReasons.ResponseFinished);
        }
    }

    private void BeginFactionDefenseRequest(CardAttackHandle attack)
    {
        var owner = _players[attack.TargetSeat];
        if (!CanRequestFactionDefense(owner, attack) || ActiveFactionDefense is not null)
        {
            throw new InvalidOperationException("FactionDefense is not available for the current Dodge response.");
        }

        var policySource = GetFactionResponsePolicy(owner, CardKind.Dodge)!;
        var candidateSeats = GetFactionProviderSeats(owner.Seat, policySource.FactionId);
        attack.MarkFactionDefenseAttempted();
        ActiveFactionDefense = new FactionDefenseHandle(this, attack, owner.Seat, candidateSeats, policySource);
        ClearPendingDecision();
        _status = EngineStatus.Running;
        AdvanceEventRulesAndQueueFact(new FactionDefenseRequestedEvent(attack.ResolutionId, owner.Seat, candidateSeats, policySource.SkillId));
        AddLog(
            "SkillTriggered",
            $"{owner.Name} 发动【{FactionPolicyName(policySource)}】，依次询问其他{GetFactionName(policySource.FactionId)}势力角色是否替其打出【闪】。",
            owner.Seat,
            attack.SourceSeat);
        AdvanceFactionDefenseCandidate();
    }

    private void AdvanceFactionDefenseCandidate()
    {
        var pending = ActiveFactionDefense ??
            throw new InvalidOperationException("There is no active FactionDefense request.");
        if (!SameAttackOwner(ActiveCardAttack, pending.Attack))
        {
            throw new InvalidOperationException("The FactionDefense request no longer belongs to the active attack.");
        }

        while (pending.CandidateIndex < pending.CandidateSeats.Count)
        {
            var provider = _players[pending.CurrentCandidateSeat];
            var responseCards = provider.IsAlive &&
                                string.Equals(GetEffectiveFactionId(provider), pending.Source.FactionId, StringComparison.Ordinal)
                ? GetResponseCards(provider, CardKind.Dodge)
                : [];
            var hasBagua = provider.IsAlive &&
                           HasBagua(provider);
            if (responseCards.Count == 0 && !hasBagua && !ProgramDodgeResponseChoices(provider).Any() &&
                !(provider.IsAlive && GetEffectiveFactionId(provider)==pending.Source.FactionId && HasRequestedDeckBasicSource(provider, CardKind.Dodge)))
            {
                pending.CandidateIndex++;
                continue;
            }

            _pendingDecision = new PendingDecision(
                DecisionKind.RespondDodge,
                provider.Seat,
                $"{_players[pending.OwnerSeat].Name} 发动了【{FactionPolicyName(pending.Source)}】，是否替其打出【闪】？",
                responseCards.Select(card => card.Id).ToArray(),
                [],
                pending.Attack.SourceSeat,
                pending.Attack.EffectiveCardKind)
            {
                PromptId = CreatePromptId(),
                TargetSeat = pending.OwnerSeat,
                RequiredCardKind = CardKind.Dodge,
                Choices = CreateFactionDefenseResponseChoices(provider, pending, responseCards, hasBagua)
            };
            _status = provider.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
            return;
        }

        var attack = pending.Attack;
        var ownerSeat = pending.OwnerSeat;
        ActiveFactionDefense = null;
        AdvanceEventRulesAndQueueFact(new FactionDefenseResolvedEvent(
            attack.ResolutionId,
            ownerSeat,
            Succeeded: false,
            ProviderSeat: null,
            ResponseCardId: null));
        AddLog(
            "SkillResolved",
            $"没有魏势力角色响应 {_players[ownerSeat].Name} 的【护驾】。",
            ownerSeat,
            attack.SourceSeat);
        PublishOwnerDodgeDecision(attack);
    }

    private IReadOnlyList<PromptChoice> CreateFactionDefenseResponseChoices(
        CharacterState provider,
        FactionDefenseHandle pending,
        IReadOnlyList<Card> responseCards,
        bool includeBagua)
    {
        var choices = responseCards.SelectMany(card =>
        {
            var responseCardKind = GetEffectiveResponseKind(provider, card, CardKind.Dodge);
            var responseDescription = IsNativeResponseCard(card, CardKind.Dodge)
                ? "打出【闪】"
                : $"将【{card.DisplayName}】当作【闪】";
            return CreateConversionChoiceVariants(
                provider, card, responseCardKind, forResponse: true,
                $"faction-defense.dodge.card-{card.Id}",
                $"{responseDescription}，替 {_players[pending.OwnerSeat].Name} 响应【护驾】。",
                [card.Id],
                [pending.OwnerSeat],
                new Dictionary<string, string>
                {
                    ["response"] = "faction-defense-dodge",
                    ["response-card-kind"] = responseCardKind.ToString(),
                    ["skill"] = pending.Source.SkillId
                });
        }).ToList();
        choices.AddRange(ProgramDodgeResponseChoices(provider));
        if (includeBagua)
        {
            choices.Add(new PromptChoice(
                new ChoiceId("faction-defense.bagua"),
                "发动【八卦阵】判定；若为红色，视为替护驾发起者打出【闪】。",
                [],
                [pending.OwnerSeat],
                new Dictionary<string, string>
                {
                    ["response"] = "faction-defense-bagua",
                    ["skill"] = pending.Source.SkillId,
                    ["equipment"] = CardKind.BaguaFormation.ToString()
                }));
        }

        choices.Add(new PromptChoice(
            new ChoiceId("faction-defense.decline"),
            "不响应【护驾】。",
            [],
            [pending.OwnerSeat],
            new Dictionary<string, string>
            {
                ["response"] = "faction-defense-decline",
                ["skill"] = pending.Source.SkillId
            }));
        return choices;
    }

    private void ResolveFactionDefenseCandidateResponse(
        FactionDefenseHandle pending,
        bool useDodge,
        bool useBagua,
        int? requestedCardId)
    {
        if (!SameContinuationOwner(ActiveFactionDefense, pending) ||
            !SameAttackOwner(ActiveCardAttack, pending.Attack) ||
            _pendingDecision is not { Kind: DecisionKind.RespondDodge } decision ||
            decision.PlayerSeat != pending.CurrentCandidateSeat ||
            useDodge && useBagua)
        {
            throw new InvalidOperationException("The FactionDefense response is not the current continuation.");
        }

        var provider = _players[pending.CurrentCandidateSeat];
        var selectedDodge = useDodge
            ? GetResponseCards(provider, CardKind.Dodge)
                .FirstOrDefault(card => card.Id == requestedCardId)
            : null;
        if (useDodge && selectedDodge is null)
        {
            throw new InvalidOperationException("The FactionDefense responder has no matching Dodge response card.");
        }

        if (useBagua && (!HasBagua(provider)))
        {
            throw new InvalidOperationException("The FactionDefense responder cannot use Bagua in this response window.");
        }

        ClearPendingDecision();
        _status = EngineStatus.Running;
        if (selectedDodge is not null)
        {
            CompleteFactionDefenseResponse(pending, provider, selectedDodge, usedBagua: false);
            return;
        }

        if (useBagua)
        {
            var judgmentResult = BeginJudgment(
                pending.Attack,
                provider.Seat,
                JudgmentReasons.BaguaDefense,
                pending.Attack.ResolutionId,
                pending.Attack.EffectiveCardKind,
                JudgmentContinuationKind.FactionDefenseBagua);
            if (judgmentResult is { } succeeded)
            {
                CompleteFactionDefenseBaguaResponse(pending.Attack, succeeded);
            }
            return;
        }

        AddLog(
            "SkillSkipped",
            $"{provider.Name} 选择不响应 {_players[pending.OwnerSeat].Name} 的【护驾】。",
            provider.Seat,
            pending.OwnerSeat);
        pending.CandidateIndex++;
        AdvanceFactionDefenseCandidate();
    }

    private void CompleteFactionDefenseResponse(
        FactionDefenseHandle pending,
        CharacterState provider,
        Card? selectedDodge,
        bool usedBagua,
        CardConversionSource? virtualResponseSource = null)
    {
        if (selectedDodge is {} declaredDodge && TryBeginCardDeclaration(provider, declaredDodge, CardKind.Dodge,
            PeekDeclarationConversion(provider, declaredDodge, CardKind.Dodge, false),
            DeclarationReturn(CardDeclarationPurpose.FactionDefense, pending.OwnerSeat), [pending.Attack.SourceSeat])) return;
        var attack = pending.Attack;
        var owner = _players[pending.OwnerSeat];
        if (!SameContinuationOwner(ActiveFactionDefense, pending) ||
            !SameAttackOwner(ActiveCardAttack, attack) ||
            provider.Seat != pending.CurrentCandidateSeat)
        {
            throw new InvalidOperationException("The completed FactionDefense response is not current.");
        }

        PopResponseWindow(attack.ResolutionId);
        SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        ActiveFactionDefense = null;
        ClearPendingDecision();
        CardKind? responseCardKind = null;
        CardConversionSource? responseConversion = null;
        CardLocation? responseFrom = null;
        bool? responseCostIsRed = null;
        if (selectedDodge is not null)
        {
            responseCardKind = GetEffectiveResponseKind(provider, selectedDodge, CardKind.Dodge);
            responseConversion = GetSelectedResponseConversion(provider, selectedDodge, responseCardKind.Value);
            responseFrom = FindOwnedCardLocation(provider, selectedDodge);
            responseCostIsRed = CapturePhysicalCardColor(provider.Seat,selectedDodge);
            PaySingleCardResponse(selectedDodge, provider, responseCardKind.Value, responseConversion);
        }

        AddLog(
            "CardResponded",
            usedBagua
                ? $"{provider.Name} 发动【八卦阵】响应【护驾】，视为 {owner.Name} 打出【闪】。"
                : $"{provider.Name} 响应【护驾】，替 {owner.Name} 打出【闪】。",
            provider.Seat,
            owner.Seat);
        AdvanceEventRulesAndQueueFact(new FactionDefenseResolvedEvent(
            attack.ResolutionId,
            owner.Seat,
            Succeeded: true,
            provider.Seat,
            selectedDodge?.Id,
            usedBagua));
        if (selectedDodge is not null)
        {
            AdvanceEventRulesAndQueueFact(new CardRespondedEvent(
                selectedDodge.Id,
                owner.Seat,
                attack.SourceSeat,
                responseCardKind ?? CardKind.Dodge));
        }
        else if (virtualResponseSource is not null)
        {
            AdvanceEventRulesAndQueueFact(new CardRespondedEvent(-1, owner.Seat, attack.SourceSeat, CardKind.Dodge));
        }

        if (ActiveGroupCard is { Effect: GroupCardEffect.ResponseAttack } group &&
            SameAttackOwner(group.CurrentAttack, attack))
        {
            AdvanceEventRulesAndQueueFact(new GroupResponseEvent(
                group.ResolutionId,
                group.Card.Kind,
                CardKind.Dodge,
                owner.Seat,
                UsedResponse: true,
                ResponseCardId: selectedDodge?.Id,
                ResponseCardKind: CardKind.Dodge));
        }

        if (selectedDodge is not null)
        {
            var responseCost = new CardActionCost(selectedDodge.Id, selectedDodge.Kind, responseFrom!.Value,responseCostIsRed);
            if (TryBeginCardResponsePrograms(
                    attack,
                    owner,
                    provider,
                    requesterSeat: owner.Seat,
                    attack.SourceSeat,
                    responseCardKind ?? CardKind.Dodge,
                    [responseCost],
                    ProgramCardContinuation.FactionDefenseDodge,
                    responseConversion))
            {
                return;
            }
            FinishSingleCardResponse(selectedDodge, responseConversion);
        }
        else if ((usedBagua || virtualResponseSource is not null) && TryBeginCardResponsePrograms(
                     attack,
                     owner,
                     provider,
                     requesterSeat: owner.Seat,
                     attack.SourceSeat,
                     CardKind.Dodge,
                     [],
                     ProgramCardContinuation.FactionDefenseDodge, virtualResponseSource))
        {
            return;
        }

        CompleteSuccessfulDodgeResponse(attack);
    }

    private void CompleteSuccessfulDodgeResponse(CardAttackHandle attack)
    {
        ClearTieredRoundDodgeResponse(attack);
        FinishSuccessfulDodgeResponse(attack);
    }

    private void FinishSuccessfulDodgeResponse(CardAttackHandle attack)
    {
        var completed = attack.RegisterDodgeResponse();
        if (attack.RequiredDodgeResponses > 1)
        {
            AdvanceEventRulesAndQueueFact(new RequiredResponseProgressEvent(
                attack.ResolutionId,
                attack.SourceSeat,
                attack.TargetSeat,
                RequireAttackCardKind(attack),
                CardKind.Dodge,
                attack.SuccessfulDodgeResponses,
                attack.RequiredDodgeResponses));
        }

        if (completed)
        {
            if (TryBeginProgramSlashStage(attack, SkillProgramTriggerWindow.SlashFullyDodged,
                    ProgramCardContinuation.SlashFullyDodged))
            {
                return;
            }

            ContinueSlashAfterDodgePrograms(attack);
            return;
        }

        SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.AwaitingResponse);
        ContinueSlashDefense(attack);
    }

    private void ContinueSlashAfterDodgePrograms(CardAttackHandle attack)
    {
        if (IsSlashDodgeCancellationPrevented(attack))
        {
            SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect);
            if (!ApplyAttackDamage(attack)) CompleteAttack(attack);
            return;
        }
        if (TryBeginDodgeCancelledSlashBenefits(attack)) return;
        ContinueSlashAfterDodgeCancellation(attack);
    }

    private void ContinueSlashAfterDodgeCancellation(CardAttackHandle attack)
    {
        if (TryBeginQinglongCrescentBladeChoice(attack))
        {
            return;
        }

        if (TryBeginStoneAxeChoice(attack))
        {
            return;
        }

        CompleteAttack(attack);
    }






    private bool CanUseQinglongCrescentBladeTarget(
        CharacterState source,
        CharacterState target,
        CardKind? effectiveKind = null, bool? effectiveColor = null, bool allowAnyColor = true, Suit? physicalSuit = null, int? effectiveRank = null) =>
        source.IsAlive &&
        target.IsAlive &&
        source.Seat != target.Seat &&
        HasWeaponAbility(source, CardKind.QinglongCrescentBlade) &&
        (effectiveKind is { } kind
            ? !IsCardUseForbidden(source.Seat, kind, CardActionType.Use) &&
              (HasSlashUseDistanceBySuit(source,kind,physicalSuit) || allowAnyColor && HasSlashUseDistanceBySuit(source,kind,Suit.Diamond) || HasTurnRedSlashPolicyForColor(source.Seat,kind,effectiveColor) || allowAnyColor && _turnCardUseEffects.HasRedSlashPolicy(_turnNumber,_currentSeat,source.Seat) || HasCardDistanceExemption(source, target, kind) ||
               IsWithinSpecificSlashRange(source,target,kind,effectiveRank) || allowAnyColor && HasPotentialRankSlashRange(source,target,kind)) &&
              !IsDirectedCardTargetProhibited(source.Seat, target.Seat, kind) &&
              !IsSlashProhibited(target)
            : SlashKinds.Any(candidate => CanUseQinglongCrescentBladeTarget(source, target, candidate,effectiveColor,allowAnyColor,physicalSuit)));

    private IReadOnlyList<Card> GetQinglongCrescentBladeSlashCards(
        CharacterState source,
        CharacterState target) =>
        CanUseQinglongCrescentBladeTarget(source, target)
            ? GetSlashUseCards(source).Where(card =>
                GetSlashUseVariants(source, IsSlashCard(card.Kind) ? card.Kind : CardKind.Slash)
                    .Any(variant => CanUseQinglongCrescentBladeTarget(
                        source, target, variant.EffectiveKind,SuitColor(EffectiveSuit(source,card)),allowAnyColor:false,physicalSuit:EffectiveSuit(source,card),effectiveRank:SpecificSlashRank(source,card,variant.EffectiveKind)))).ToArray()
            : [];

    private bool CanRequestQinglongCrescentBladeFactionSlash(
        QinglongCrescentBladeHandle pending)
    {
        var source = _players[pending.Attack.SourceSeat];
        var target = _players[pending.Attack.TargetSeat];
        return ActiveFactionCardRequest is null &&
               !pending.FactionSlashAttempted &&
               GetFactionResponsePolicy(source, CardKind.Slash) is not null &&
               CanUseQinglongCrescentBladeTarget(source, target) &&
               GetFactionSlashCandidateSeats(source.Seat).Count > 0;
    }

    // A multi-target continuation (Fangtian Halberd, group response attacks)
    // publishes its next target's response window inside CompleteAttack; a
    // Qinglong follow-up Slash must not start on top of that freshly published
    // window, so the blade waits until the current target is the last living
    // one and its chain finalizes the whole use.
    private bool MultiTargetContinuationDefersQinglong(CardAttackHandle attack) =>
        (ActiveFangtianHalberd is { } fangtian &&
         SameAttackOwner(fangtian.CurrentAttack, attack) &&
         fangtian.TargetSeats.Skip(fangtian.TargetIndex + 1)
             .Any(seat => _players[seat].IsAlive)) ||
        (ActiveGroupCard is { Effect: GroupCardEffect.ResponseAttack } group &&
         SameAttackOwner(group.CurrentAttack, attack) &&
         group.TargetSeats.Skip(group.TargetIndex + 1)
             .Any(seat => _players[seat].IsAlive));

    private bool TryBeginQinglongCrescentBladeChoice(CardAttackHandle attack)
    {
        var source = _players[attack.SourceSeat];
        var target = _players[attack.TargetSeat];
        var effectiveKind = RequireAttackCardKind(attack);
        if (!UsesFormalQinglongCrescentBlade ||
            effectiveKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash))
        {
            return false;
        }

        if (MultiTargetContinuationDefersQinglong(attack))
        {
            return false;
        }

        if (ActiveQinglongCrescentBlade is not null)
        {
            throw new InvalidOperationException("The engine cannot open two Qinglong Crescent Blade choices at once.");
        }

        var pending = new QinglongCrescentBladeHandle(this, attack);
        var slashes = GetQinglongCrescentBladeSlashCards(source, target);
        if (slashes.Count == 0 && !CanRequestQinglongCrescentBladeFactionSlash(pending) &&
            !TieredRoundZeroForcedSlashRules(source, target, true).Any() && !HasRequestedDeckBasicSource(source, CardKind.Slash))
        {
            return false;
        }

        ActiveQinglongCrescentBlade = pending;
        SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.AwaitingResponse);
        PublishQinglongCrescentBladeChoice(pending, includeFactionSlash: true);
        return true;
    }

    private void PublishQinglongCrescentBladeChoice(
        QinglongCrescentBladeHandle pending,
        bool includeFactionSlash)
    {
        if (!SameContinuationOwner(ActiveQinglongCrescentBlade, pending) ||
            !SameAttackOwner(ActiveCardAttack, pending.Attack))
        {
            throw new InvalidOperationException("The Qinglong Crescent Blade choice has no current Slash continuation.");
        }

        var attack = pending.Attack;
        var source = _players[attack.SourceSeat];
        var target = _players[attack.TargetSeat];
        var effectiveKind = RequireAttackCardKind(attack);
        var slashes = GetQinglongCrescentBladeSlashCards(source, target);
        var choices = slashes.SelectMany(card =>
        {
            var followupKind = IsSlashCard(card.Kind) ? card.Kind : CardKind.Slash;
            var cardDescription = IsSlashCard(card.Kind)
                ? $"使用【{CardCatalog.Get(card.Kind).DisplayName}】"
                : $"将【{card.DisplayName}】当作【杀】使用";
            return CreateConversionChoiceVariants(
                source, card, followupKind, forResponse: false,
                $"qinglong.slash.resolution-{attack.ResolutionId}.card-{card.Id}",
                $"{cardDescription}，继续攻击 {target.Name}。",
                [card.Id],
                [target.Seat],
                new Dictionary<string, string>
                {
                    ["action"] = "qinglong-slash",
                    ["response-card-kind"] = followupKind.ToString()
                });
        }).ToList();
        if (includeFactionSlash && CanRequestQinglongCrescentBladeFactionSlash(pending))
        {
            choices.Add(new PromptChoice(
                new ChoiceId($"qinglong.faction-slash.resolution-{attack.ResolutionId}"),
                $"发动【激将】，请求其他蜀势力角色为你对 {target.Name} 提供一张【杀】。",
                [],
                [target.Seat],
                new Dictionary<string, string>
                {
                    ["action"] = "qinglong-jijiang",
                    ["skill"] = GetFactionResponsePolicy(source, CardKind.Slash)!.SkillId
                }));
        }
        choices.AddRange(TieredRoundZeroForcedSlashChoices(source, target, attack.ResolutionId, true));
        choices.Add(new PromptChoice(
            new ChoiceId($"qinglong.skip.resolution-{attack.ResolutionId}"),
            "不发动【青龙偃月刀】，此【杀】被【闪】抵消。",
            [],
            [],
            new Dictionary<string, string> { ["action"] = "qinglong-skip" }));

        _pendingDecision = new PendingDecision(
            DecisionKind.QinglongCrescentBlade,
            source.Seat,
            $"{target.Name} 已用【闪】抵消你的【{CardCatalog.Get(effectiveKind).DisplayName}】，是否发动【青龙偃月刀】对其再使用一张【杀】？",
            slashes.Select(card => card.Id).ToArray(),
            [target.Seat],
            SourceSeat: source.Seat,
            IncomingCard: effectiveKind)
        {
            PromptId = source.IsHuman ? CreatePromptId() : default,
            IsPrivate = true,
            TargetSeat = target.Seat,
            Choices = choices
        };
        _status = source.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveQinglongCrescentBladeChoice(PromptChoice selected)
    {
        var pending = ActiveQinglongCrescentBlade ??
            throw new InvalidOperationException("There is no Qinglong Crescent Blade choice to resolve.");
        var attack = pending.Attack;
        if (!SameAttackOwner(ActiveCardAttack, attack) ||
            _pendingDecision is not { Kind: DecisionKind.QinglongCrescentBlade } decision ||
            decision.PlayerSeat != attack.SourceSeat)
        {
            throw new InvalidOperationException("The Qinglong Crescent Blade choice is not the current Slash continuation.");
        }

        var source = _players[attack.SourceSeat];
        var target = _players[attack.TargetSeat];
        if (TryResolveTieredRoundZeroForcedSlash(selected, false)) return;
        var action = selected.Parameters.GetValueOrDefault("action");
        var usesSlash = action == "qinglong-slash";
        var requestsFactionSlash = action == "qinglong-jijiang";
        Card? slash = null;
        CardKind? effectiveSlashKind = null;
        if (usesSlash)
        {
            if (!CanUseQinglongCrescentBladeTarget(source, target) ||
                selected.Cards.Count != 1 ||
                !selected.Targets.SequenceEqual([target.Seat]))
            {
                throw new InvalidOperationException("Qinglong Crescent Blade must retain its source, weapon and same target.");
            }

            slash = GetQinglongCrescentBladeSlashCards(source, target)
                .SingleOrDefault(card => card.Id == selected.Cards[0]) ??
                throw new InvalidOperationException("The selected Qinglong Crescent Blade Slash is no longer available.");
            effectiveSlashKind = IsSlashCard(slash.Kind) ? slash.Kind : CardKind.Slash;
            if (!CanUseQinglongCrescentBladeTarget(source, target, effectiveSlashKind, SuitColor(EffectiveSuit(source,slash)), false, EffectiveSuit(source,slash), SpecificSlashRank(source,slash,effectiveSlashKind ?? CardKind.Slash)))
                throw new InvalidOperationException(
                    "The selected Qinglong Crescent Blade Slash kind is prohibited for its target.");
        }
        else if (requestsFactionSlash)
        {
            if (selected.Cards.Count != 0 ||
                !selected.Targets.SequenceEqual([target.Seat]) ||
                !CanRequestQinglongCrescentBladeFactionSlash(pending))
            {
                throw new InvalidOperationException("Qinglong Crescent Blade FactionSlash is no longer available.");
            }

            BeginQinglongCrescentBladeFactionSlashRequest(pending);
            return;
        }
        else if (action != "qinglong-skip" || selected.Cards.Count != 0 || selected.Targets.Count != 0)
        {
            throw new InvalidOperationException("The Qinglong Crescent Blade choice is malformed.");
        }

        if (usesSlash)
        {
            CaptureSelectedResponseConversion(selected);
            BeginQinglongCrescentBladeFollowup(
                pending,
                slash!,
                effectiveSlashKind!.Value,
                source.Seat,
                [slash!]);
            return;
        }

        ActiveQinglongCrescentBlade = null;
        ClearPendingDecision();
        SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        AdvanceEventRulesAndQueueFact(new QinglongCrescentBladeResolvedEvent(
            attack.ResolutionId,
            source.Seat,
            target.Seat,
            Used: false,
            SlashCardIds: [],
            EffectiveSlashKind: null));
        AddLog(
            "EquipmentSkipped",
            $"{source.Name} 未发动【青龙偃月刀】，对 {target.Name} 的【杀】被【闪】抵消。",
            source.Seat,
            target.Seat);

        CompleteAttack(attack);
    }

    private void BeginQinglongCrescentBladeFollowup(
        QinglongCrescentBladeHandle pending,
        Card slash,
        CardKind effectiveSlashKind,
        int physicalOwnerSeat,
        IReadOnlyList<Card> physicalCards,
        FactionCardRequestHandle? jijiang = null,
        CardConversionSource? conversionSource = null)
    {
        var attack = pending.Attack;
        if (!SameContinuationOwner(ActiveQinglongCrescentBlade, pending) ||
            !SameAttackOwner(ActiveCardAttack, attack))
        {
            throw new InvalidOperationException("The Qinglong Crescent Blade follow-up is not current.");
        }

        var source = _players[attack.SourceSeat];
        var target = _players[attack.TargetSeat];
        ActiveQinglongCrescentBlade = null;
        ClearPendingDecision();
        SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        AdvanceEventRulesAndQueueFact(new QinglongCrescentBladeResolvedEvent(
            attack.ResolutionId,
            source.Seat,
            target.Seat,
            Used: true,
            Array.AsReadOnly(physicalCards.Select(card => card.Id).ToArray()),
            effectiveSlashKind));
        AddLog(
            "EquipmentEffect",
            $"{source.Name} 发动【青龙偃月刀】，对 {target.Name} 再使用一张【{CardCatalog.Get(effectiveSlashKind).DisplayName}】。",
            source.Seat,
            target.Seat);

        CompleteAttack(attack);
        SuspendContinuationForQinglongFollowup(attack.ResolutionId);
        ResolveSlashCore(
            source,
            target,
            slash,
            effectiveSlashKind,
            physicalOwnerSeat,
            factionRequest: jijiang,
            physicalCards: physicalCards,
            countsTowardSlashLimit: false,
            conversionSource: conversionSource);
    }

    private void SuspendContinuationForQinglongFollowup(long outerResolutionId)
    {
        if (ActiveCardAttack is null && _pendingDecision is null && ActiveFangtianHalberd is null)
        {
            return;
        }

        var suspension = new QinglongFollowupState(ActiveCardAttack?.ResolutionId,
            _pendingDecision is { } decision ? CaptureSuspendedDecision(decision) : null,
            ActiveFangtianHalberd?.OwnerFrameId, _status);
        UpdateCardContinuations(outerResolutionId, state => state with { QinglongFollowup = suspension });
        ActiveCardAttack = null;
        ClearPendingDecision();
        ActiveFangtianHalberd = null;
    }

    private bool RestoreContinuationAfterQinglongFollowup(long completedOwnerId)
    {
        if (ActiveQinglongFollowup is not { } suspension ||
            completedOwnerId == suspension.OuterResolutionId)
        {
            return false;
        }

        // The follow-up restores the outer continuation only after every nested
        // card use has unwound back to the suspended parent's frame.
        if (_resolutionStack.Any(frame => frame is CardUseFrame use && use.Id != suspension.OuterResolutionId))
        {
            return false;
        }

        ActiveQinglongFollowup = null;
        if (_status == EngineStatus.Completed)
        {
            // The match ended during the nested follow-up; a completed game
            // must not resurrect the outer continuation's pending state.
            ActiveFangtianHalberd = null;
            return true;
        }

        ActiveCardAttack = suspension.NextAttack;
        _pendingDecision = suspension.NextDecision;
        ActiveFangtianHalberd = suspension.FangtianContinuation;
        _status = suspension.Status;
        return true;
    }

    private bool TryBeginStoneAxeChoice(CardAttackHandle attack)
    {
        var source = _players[attack.SourceSeat];
        var target = _players[attack.TargetSeat];
        var effectiveKind = RequireAttackCardKind(attack);
        var discardableCards = GetStoneAxeDiscardCards(source);
        if (!UsesFormalStoneAxe ||
            effectiveKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) ||
            !source.IsAlive ||
            !target.IsAlive ||
            !HasWeaponAbility(source, CardKind.StoneAxe) ||
            discardableCards.Count < 2)
        {
            return false;
        }

        if (ActiveStoneAxe is not null)
        {
            throw new InvalidOperationException("The engine cannot open two Stone Axe choices at once.");
        }

        var choices = new List<PromptChoice>();
        for (var first = 0; first < discardableCards.Count - 1; first++)
        {
            for (var second = first + 1; second < discardableCards.Count; second++)
            {
                var firstCard = discardableCards[first];
                var secondCard = discardableCards[second];
                choices.Add(new PromptChoice(
                    new ChoiceId($"stone-axe.use.resolution-{attack.ResolutionId}.cards-{firstCard.Id}-{secondCard.Id}"),
                    $"弃置{DescribeStoneAxeCost(source, firstCard)}与{DescribeStoneAxeCost(source, secondCard)}，令此【{CardCatalog.Get(effectiveKind).DisplayName}】仍造成伤害。",
                    [firstCard.Id, secondCard.Id],
                    [],
                    new Dictionary<string, string> { ["action"] = "stone-axe-use" }));
            }
        }

        choices.Add(new PromptChoice(
            new ChoiceId($"stone-axe.skip.resolution-{attack.ResolutionId}"),
            "不发动【贯石斧】，此【杀】被【闪】抵消。",
            [],
            [],
            new Dictionary<string, string> { ["action"] = "stone-axe-skip" }));

        ActiveStoneAxe = new StoneAxeHandle(this,
            attack,
            discardableCards.Select(card => card.Id).ToArray());
        SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.AwaitingResponse);
        _pendingDecision = new PendingDecision(
            DecisionKind.StoneAxe,
            source.Seat,
            $"{target.Name} 已用【闪】抵消你的【{CardCatalog.Get(effectiveKind).DisplayName}】，是否弃置两张牌发动【贯石斧】？",
            discardableCards.Select(card => card.Id).ToArray(),
            [],
            SourceSeat: source.Seat,
            IncomingCard: effectiveKind)
        {
            PromptId = source.IsHuman ? CreatePromptId() : default,
            IsPrivate = true,
            TargetSeat = target.Seat,
            Choices = choices
        };
        _status = source.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return true;
    }

    private IReadOnlyList<Card> GetStoneAxeDiscardCards(CharacterState source) =>
        GetHand(source).Concat(GetEquipment(source)).Where(card => !IsSelfHandCategoryDiscardForbidden(source.Seat, card, _cardZones.GetLocation(card.Id), OwnedCardMoveIntent.Discard)).ToArray();

    private string DescribeStoneAxeCost(CharacterState source, Card card)
    {
        var zone = _cardZones.GetLocation(card.Id) == CardLocation.Equipment(source.Seat)
            ? "装备区"
            : "手牌";
        return $"【{card.DisplayName}】（{zone}）";
    }

    private void ResolveStoneAxeChoice(IReadOnlyList<int> discardedCardIds)
    {
        var pending = ActiveStoneAxe ??
            throw new InvalidOperationException("There is no Stone Axe choice to resolve.");
        var attack = pending.Attack;
        if (!SameAttackOwner(ActiveCardAttack, attack) ||
            _pendingDecision is not { Kind: DecisionKind.StoneAxe } decision ||
            decision.PlayerSeat != attack.SourceSeat)
        {
            throw new InvalidOperationException("The Stone Axe choice is not the current Slash continuation.");
        }

        var source = _players[attack.SourceSeat];
        var target = _players[attack.TargetSeat];
        var distinctIds = discardedCardIds.Distinct().ToArray();
        var useStoneAxe = distinctIds.Length > 0;
        if (useStoneAxe &&
            (distinctIds.Length != 2 ||
             distinctIds.Any(id => !pending.CandidateCardIds.Contains(id))))
        {
            throw new InvalidOperationException("Stone Axe requires two distinct cards from the published candidate set.");
        }

        var currentCards = GetStoneAxeDiscardCards(source);
        var selectedCards = distinctIds
            .Select(id => currentCards.SingleOrDefault(card => card.Id == id) ??
                throw new InvalidOperationException("A selected Stone Axe cost card is no longer owned by its source."))
            .ToArray();
        ActiveStoneAxe = null;
        ClearPendingDecision();
        SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect);

        if (useStoneAxe)
        {
            foreach (var card in selectedCards)
            {
                MoveCard(
                    card,
                    _cardZones.GetLocation(card.Id),
                    CardLocation.Processing,
                    CardMoveReasons.StoneAxeDiscard);
            }
            foreach (var card in selectedCards)
            {
                MoveCard(
                    card,
                    CardLocation.Processing,
                    CardLocation.DiscardPile,
                    CardMoveReasons.StoneAxeDiscard);
            }
        }

        AdvanceEventRulesAndQueueFact(new StoneAxeResolvedEvent(
            attack.ResolutionId,
            source.Seat,
            target.Seat,
            useStoneAxe,
            Array.AsReadOnly(distinctIds)));
        AddLog(
            useStoneAxe ? "EquipmentEffect" : "EquipmentSkipped",
            useStoneAxe
                ? $"{source.Name} 弃置两张牌发动【贯石斧】，令对 {target.Name} 的【杀】仍造成伤害。"
                : $"{source.Name} 未发动【贯石斧】，对 {target.Name} 的【杀】被【闪】抵消。",
            source.Seat,
            target.Seat);

        if (useStoneAxe)
        {
            if (!ApplyAttackDamage(attack))
            {
                CompleteAttack(attack);
            }
            return;
        }

        CompleteAttack(attack);
    }

    private void CompleteFactionDefenseBaguaResponse(CardAttackHandle attack, bool succeeded)
    {
        var pending = ActiveFactionDefense ??
            throw new InvalidOperationException("A FactionDefense Bagua judgment has no FactionDefense continuation.");
        if (!SameAttackOwner(pending.Attack, attack))
        {
            throw new InvalidOperationException("The FactionDefense Bagua judgment belongs to another attack.");
        }

        var provider = _players[pending.CurrentCandidateSeat];
        if (succeeded)
        {
            CompleteFactionDefenseResponse(pending, provider, selectedDodge: null, usedBagua: true);
            return;
        }

        AddLog(
            "SkillResolved",
            $"{provider.Name} 以【八卦阵】响应【护驾】，但判定失败。",
            provider.Seat,
            pending.OwnerSeat);
        pending.CandidateIndex++;
        AdvanceFactionDefenseCandidate();
    }

    private void PublishOwnerDodgeDecision(CardAttackHandle attack)
    {
        var owner = _players[attack.TargetSeat];
        var dodges = GetResponseCards(owner, CardKind.Dodge);
        var hasBagua = !attack.IgnoresArmor && HasBagua(owner);
        _pendingDecision = new PendingDecision(
            DecisionKind.RespondDodge,
            owner.Seat,
            "【护驾】无人响应，是否由你自己打出【闪】？",
            dodges.Select(card => card.Id).ToArray(),
            [],
            attack.SourceSeat,
            attack.EffectiveCardKind)
        {
            PromptId = CreatePromptId(),
            RequiredCardKind = CardKind.Dodge,
            Choices = CreateResponseChoices(
                dodges,
                CardKind.Dodge,
                attack.EffectiveCardKind,
                card => GetEffectiveResponseKind(owner, card, CardKind.Dodge),
                includeBagua: hasBagua,
                includeFactionDefense: false,
                responder: owner)
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void BeginProgramFactionCardRequest(
        long frameId,
        int ownerSeat,
        int targetSeat,
        string providerFactionId,
        CardKind requiredKind)
    {
        if (requiredKind != CardKind.Slash || string.IsNullOrWhiteSpace(providerFactionId))
            throw new InvalidOperationException("This faction-card request supports a Slash and a named faction.");
        if (ActiveFactionCardRequest is not null ||
            _resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != frameId ||
            frame.OwnerSeat != ownerSeat ||
            !IsValidPlayerSeat(targetSeat) ||
            !CanUseProvidedSlashTarget(_players[ownerSeat], _players[targetSeat], CardKind.Slash))
            throw new InvalidOperationException("The program faction-card request has no valid parent or target.");

        ActiveFactionCardRequest = new FactionCardRequestHandle(this,
            frameId,
            FactionCardRequestPurpose.ProgramSkillUse,
            ownerSeat,
            GetFactionProviderSeats(ownerSeat, providerFactionId),
            targetSeat: targetSeat,
            programSkillFrameId: frameId,
            providerFactionId: providerFactionId,
            requiredKind: requiredKind,
            policySource: GetFactionResponsePolicy(_players[ownerSeat], requiredKind));
        ClearPendingDecision();
        _status = EngineStatus.Running;
        AdvanceFactionSlashCandidate();
    }

    private void BeginFactionSlashResponseRequest(CardAttackHandle attack)
    {
        if (ActiveFactionCardRequest is not null)
        {
            throw new InvalidOperationException("Another FactionSlash request is already active.");
        }

        FactionCardRequestPurpose purpose;
        CharacterState owner;
        if (ActiveDuel is { } duel && SameAttackOwner(duel.Attack, attack))
        {
            purpose = FactionCardRequestPurpose.DuelResponse;
            owner = _players[duel.ResponderSeat];
            if (!CanRequestFactionSlashResponse(owner, attack))
            {
                throw new InvalidOperationException("FactionSlash is not available for this Duel response.");
            }
            duel.FactionSlashAttempted = true;
        }
        else if (ActiveGroupCard is { Effect: GroupCardEffect.ResponseAttack, RequiredCardKind: CardKind.Slash } group &&
                 SameAttackOwner(group.CurrentAttack, attack))
        {
            purpose = FactionCardRequestPurpose.GroupResponse;
            owner = _players[attack.TargetSeat];
            if (!CanRequestFactionSlashResponse(owner, attack))
            {
                throw new InvalidOperationException("FactionSlash is not available for this group response.");
            }
            attack.MarkFactionSlashAttempted();
        }
        else
        {
            throw new InvalidOperationException("FactionSlash requires a Duel or Barbarian Assault Slash response.");
        }

        var requestPolicy = GetFactionResponsePolicy(owner, CardKind.Slash)!;
        var candidateSeats = GetFactionProviderSeats(owner.Seat, requestPolicy.FactionId);
        ActiveFactionCardRequest = new FactionCardRequestHandle(this,
            attack.ResolutionId,
            purpose,
            owner.Seat,
            candidateSeats,
            responseAttack: attack, providerFactionId: requestPolicy.FactionId, policySource: requestPolicy);
        ClearPendingDecision();
        _status = EngineStatus.Running;
        AdvanceEventRulesAndQueueFact(new FactionSlashRequestedEvent(
            attack.ResolutionId,
            owner.Seat,
            candidateSeats,
            SkillId: requestPolicy.SkillId,
            IsActiveUse: false));
        AddLog(
            "SkillTriggered",
            $"{owner.Name} 发动主公技【激将】，依次询问其他蜀势力角色是否替其打出【杀】。",
            owner.Seat,
            attack.SourceSeat);
        AdvanceFactionSlashCandidate();
    }

    private void BeginBorrowedSwordFactionSlashRequest(BorrowedSwordHandle borrowedSword)
    {
        if (ActiveFactionCardRequest is not null ||
            !SameContinuationOwner(ActiveBorrowedSword, borrowedSword) ||
            !borrowedSword.AwaitingSlashChoice ||
            !CanRequestBorrowedSwordFactionSlash(borrowedSword))
        {
            throw new InvalidOperationException("FactionSlash is not available for this Borrowed Sword use.");
        }

        var owner = _players[borrowedSword.WeaponOwnerSeat];
        var requestPolicy = GetFactionResponsePolicy(owner, CardKind.Slash)!;
        var candidateSeats = GetFactionProviderSeats(owner.Seat, requestPolicy.FactionId);
        borrowedSword.FactionSlashAttempted = true;
        borrowedSword.AwaitingSlashChoice = false;
        ActiveFactionCardRequest = new FactionCardRequestHandle(this,
            borrowedSword.ResolutionId,
            FactionCardRequestPurpose.BorrowedSwordUse,
            owner.Seat,
            candidateSeats,
            targetSeat: borrowedSword.SlashTargetSeat,
            borrowedSword: borrowedSword, providerFactionId: requestPolicy.FactionId, policySource: requestPolicy);
        ClearPendingDecision();
        _status = EngineStatus.Running;
        AdvanceEventRulesAndQueueFact(new FactionSlashRequestedEvent(
            borrowedSword.ResolutionId,
            owner.Seat,
            candidateSeats,
            SkillId: requestPolicy.SkillId,
            IsActiveUse: true,
            borrowedSword.SlashTargetSeat));
        AddLog(
            "SkillTriggered",
            $"{owner.Name} 在【借刀杀人】结算中发动主公技【激将】，请求其他蜀势力角色为其对 {_players[borrowedSword.SlashTargetSeat].Name} 提供【杀】。",
            owner.Seat,
            borrowedSword.SlashTargetSeat);
        AdvanceFactionSlashCandidate();
    }

    private void BeginQinglongCrescentBladeFactionSlashRequest(
        QinglongCrescentBladeHandle qinglong)
    {
        if (ActiveFactionCardRequest is not null ||
            !SameContinuationOwner(ActiveQinglongCrescentBlade, qinglong) ||
            !CanRequestQinglongCrescentBladeFactionSlash(qinglong))
        {
            throw new InvalidOperationException("FactionSlash is not available for this Qinglong Crescent Blade use.");
        }

        var attack = qinglong.Attack;
        var owner = _players[attack.SourceSeat];
        var requestPolicy = GetFactionResponsePolicy(owner, CardKind.Slash)!;
        var candidateSeats = GetFactionProviderSeats(owner.Seat, requestPolicy.FactionId);
        qinglong.FactionSlashAttempted = true;
        ActiveFactionCardRequest = new FactionCardRequestHandle(this,
            attack.ResolutionId,
            FactionCardRequestPurpose.QinglongCrescentBladeUse,
            owner.Seat,
            candidateSeats,
            targetSeat: attack.TargetSeat,
            qinglongCrescentBlade: qinglong, providerFactionId: requestPolicy.FactionId, policySource: requestPolicy);
        ClearPendingDecision();
        _status = EngineStatus.Running;
        AdvanceEventRulesAndQueueFact(new FactionSlashRequestedEvent(
            attack.ResolutionId,
            owner.Seat,
            candidateSeats,
            SkillId: requestPolicy.SkillId,
            IsActiveUse: true,
            attack.TargetSeat));
        AddLog(
            "SkillTriggered",
            $"{owner.Name} 在【青龙偃月刀】结算中发动主公技【激将】，请求其他蜀势力角色为其对 {_players[attack.TargetSeat].Name} 提供【杀】。",
            owner.Seat,
            attack.TargetSeat);
        AdvanceFactionSlashCandidate();
    }

    private void AdvanceFactionSlashCandidate()
    {
        var pending = ActiveFactionCardRequest ??
            throw new InvalidOperationException("There is no active FactionSlash request.");
        if (!pending.AwaitingProviders)
        {
            throw new InvalidOperationException("The FactionSlash provider cursor has already completed.");
        }
        if (TryPublishFactionRequestCost(pending)) return;

        while (pending.CandidateIndex < pending.CandidateSeats.Count)
        {
            var provider = _players[pending.CurrentCandidateSeat];
            var slashes = provider.IsAlive &&
                          string.Equals(GetEffectiveFactionId(provider), pending.ProviderFactionId, StringComparison.Ordinal)
                ? GetFactionSlashSlashCards(pending, provider)
                : [];
            var zhangbaPairs = provider.IsAlive &&
                               string.Equals(GetEffectiveFactionId(provider), pending.ProviderFactionId, StringComparison.Ordinal)
                ? GetFactionRequestZhangbaPairs(pending, provider)
                : [];
            var isResponse = !pending.IsProgramSkillUse && !pending.IsBorrowedSwordUse &&
                             !pending.IsQinglongCrescentBladeUse;
            var programMultiCardConversions = provider.IsAlive &&
                                              string.Equals(GetEffectiveFactionId(provider), pending.ProviderFactionId, StringComparison.Ordinal)
                ? GetFactionRequestMultiCardSelections(pending, provider)
                : [];
            if (slashes.Count == 0 && zhangbaPairs.Count == 0 && programMultiCardConversions.Count == 0 &&
                !(provider.IsAlive && GetEffectiveFactionId(provider)==pending.ProviderFactionId && HasRequestedDeckBasicSource(provider, CardKind.Slash)))
            {
                pending.CandidateIndex++;
                continue;
            }

            _pendingDecision = new PendingDecision(
                DecisionKind.RespondSlash,
                provider.Seat,
                $"{_players[pending.OwnerSeat].Name} 发动了【{GetFactionRequestDisplayName(pending)}】，是否替其打出【杀】？",
                slashes.Select(card => card.Id)
                    .Concat(zhangbaPairs.SelectMany(pair => pair.Select(card => card.Id)))
                    .Concat(programMultiCardConversions.SelectMany(item => item.Cards.Select(card => card.Id)))
                    .Distinct()
                    .ToArray(),
                [],
                SourceSeat: pending.OwnerSeat,
                IncomingCard: CardKind.Slash)
            {
                PromptId = CreatePromptId(),
                TargetSeat = pending.OwnerSeat,
                RequiredCardKind = CardKind.Slash,
                Choices = CreateFactionSlashResponseChoices(provider, pending, slashes)
            };
            _status = provider.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
            return;
        }

        CompleteFactionSlashFailure(pending);
    }

    private string GetFactionRequestSkillId(FactionCardRequestHandle pending) =>
        pending.IsProgramSkillUse && !pending.IsAssistedProgramUse && pending.ProgramSkillFrameId is { } frameId
            ? _resolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.Id == frameId).SkillId
            : pending.PolicySource!.SkillId;

    private string GetFactionRequestDisplayName(FactionCardRequestHandle pending) =>
        _contentRegistry!.GetSkill(GetFactionRequestSkillId(pending)).Name;

    private IReadOnlyList<PromptChoice> CreateFactionSlashResponseChoices(
        CharacterState provider,
        FactionCardRequestHandle pending,
        IReadOnlyList<Card> slashes)
    {
        if (pending.IsAssistedProgramUse) return CreateAssistedFactionSlashChoices(provider, pending);
        var responseTargets = pending.TargetSeat is { } responseTargetSeat
            ? new[] { responseTargetSeat }
            : new[] { pending.OwnerSeat };
        var isResponse = !pending.IsProgramSkillUse && !pending.IsBorrowedSwordUse &&
                         !pending.IsQinglongCrescentBladeUse;
        var choices = slashes.SelectMany(card =>
        {
            var effectiveKind = GetFactionSlashEffectiveSlashKind(pending, provider, card);
            var description = IsNativeResponseCard(card, CardKind.Slash)
                ? $"打出【{CardCatalog.Get(effectiveKind).DisplayName}】"
                : $"将【{card.DisplayName}】当作【杀】";
            return CreateConversionChoiceVariants(
                provider, card, effectiveKind, isResponse,
                $"faction-slash.slash.card-{card.Id}",
                $"{description}，替 {_players[pending.OwnerSeat].Name} 响应【{GetFactionRequestDisplayName(pending)}】。",
                [card.Id],
                responseTargets,
                new Dictionary<string, string>
                {
                    ["response"] = "faction-slash-slash",
                    ["response-card-kind"] = effectiveKind.ToString(),
                    ["skill"] = GetFactionRequestSkillId(pending)
                });
        }).ToList();
        foreach (var pair in GetFactionRequestZhangbaPairs(pending, provider))
        {
            choices.Add(new PromptChoice(
                new ChoiceId($"faction-slash.zhangba-slash.cards-{pair[0].Id}-{pair[1].Id}"),
                $"发动【丈八蛇矛】，将两张手牌当【杀】替 {_players[pending.OwnerSeat].Name} 响应【{GetFactionRequestDisplayName(pending)}】。",
                [pair[0].Id, pair[1].Id],
                responseTargets,
                new Dictionary<string, string>
                {
                    ["response"] = "zhangba-slash",
                    ["response-card-kind"] = CardKind.Slash.ToString(),
                    ["skill"] = GetFactionRequestSkillId(pending),
                    ["equipment"] = CardKind.ZhangbaSerpentSpear.ToString()
                }));
        }
        foreach (var selection in GetFactionRequestMultiCardSelections(pending, provider))
        {
            var cardIds = selection.Cards.Select(card => card.Id).ToArray();
            var parameters = new Dictionary<string, string>
            {
                ["response"] = "program-view-as-slash",
                ["response-card-kind"] = CardKind.Slash.ToString(),
                ["skill"] = GetFactionRequestSkillId(pending)
            };
            AddConversionParameters(parameters, selection.Source);
            choices.Add(new PromptChoice(
                new ChoiceId($"faction-slash.program-view-as-slash.{selection.Source.SkillId.Length}:" +
                             $"{selection.Source.SkillId}.{selection.Source.BindingId}.cards-{string.Join('-', cardIds)}"),
                $"发动【{ProgramConversionName(selection.Source)}】，将 {cardIds.Length} 张手牌当【杀】替 {_players[pending.OwnerSeat].Name} 响应【{GetFactionRequestDisplayName(pending)}】。",
                cardIds,
                responseTargets,
                parameters));
        }
        choices.Add(new PromptChoice(
            new ChoiceId("faction-slash.decline"),
            $"不响应【{GetFactionRequestDisplayName(pending)}】。",
            [],
            responseTargets,
            new Dictionary<string, string>
            {
                ["response"] = "faction-slash-decline",
                ["skill"] = GetFactionRequestSkillId(pending)
            }));
        return choices;
    }

    private void ResolveFactionSlashCandidateResponse(
        FactionCardRequestHandle pending,
        bool useSlash,
        int? requestedCardId,
        CardKind? requestedEffectiveKind = null)
    {
        if (!SameContinuationOwner(ActiveFactionCardRequest, pending) ||
            !pending.AwaitingProviders ||
            _pendingDecision is not { Kind: DecisionKind.RespondSlash } decision ||
            decision.PlayerSeat != pending.CurrentCandidateSeat)
        {
            throw new InvalidOperationException("The FactionSlash response is not the current continuation.");
        }

        var provider = _players[pending.CurrentCandidateSeat];
        var selectedSlash = useSlash
            ? GetFactionSlashSlashCards(pending, provider)
                .FirstOrDefault(card => card.Id == requestedCardId)
            : null;
        if (useSlash && selectedSlash is null)
        {
            throw new InvalidOperationException("The FactionSlash responder has no matching Slash response card.");
        }

        ClearPendingDecision();
        _status = EngineStatus.Running;
        if (selectedSlash is not null)
        {
            var baseEffectiveKind = GetFactionSlashEffectiveSlashKind(pending, provider, selectedSlash);
            var effectiveKind = requestedEffectiveKind ?? baseEffectiveKind;
            if (effectiveKind != baseEffectiveKind)
            {
                throw new InvalidOperationException("A FactionSlash provider cannot choose the owner's weapon conversion.");
            }

            if (TryBeginFactionSlashZhuqueFanChoice(
                    pending,
                    provider,
                    [selectedSlash],
                    baseEffectiveKind))
            {
                return;
            }

            if (pending.IsBorrowedSwordUse)
            {
                BeginBorrowedSwordFactionSlashSlash(
                    pending,
                    provider,
                    [selectedSlash],
                    effectiveKind,
                    usesZhuqueFan: false);
            }
            else if (pending.IsQinglongCrescentBladeUse)
            {
                BeginQinglongCrescentBladeFactionSlashSlash(
                    pending,
                    provider,
                    [selectedSlash],
                    effectiveKind,
                    usesZhuqueFan: false);
            }
            else if (pending.IsProgramSkillUse)
            {
                BeginProvidedFactionSlashSlash(
                    pending,
                    provider,
                    [selectedSlash],
                    effectiveKind,
                    usesZhuqueFan: false);
            }
            else
            {
                CompleteFactionSlashResponse(pending, provider, selectedSlash);
            }
            return;
        }

        AddLog(
            "SkillSkipped",
            $"{provider.Name} 选择不响应 {_players[pending.OwnerSeat].Name} 的【激将】。",
            provider.Seat,
            pending.OwnerSeat);
        pending.CandidateIndex++;
        AdvanceFactionSlashCandidate();
    }

    private void ResolveFactionSlashZhangbaCandidateResponse(
        FactionCardRequestHandle pending,
        CharacterState provider,
        IReadOnlyList<Card> pair)
    {
        var providerReward = CaptureFactionProviderReward(pending);
        if (!SameContinuationOwner(ActiveFactionCardRequest, pending) ||
            !pending.AwaitingProviders ||
            provider.Seat != pending.CurrentCandidateSeat ||
            FindZhangbaSlashPair(provider, pair.Select(card => card.Id).ToArray()) is null)
        {
            throw new InvalidOperationException("The Zhangba FactionSlash response is not current.");
        }

        var owner = _players[pending.OwnerSeat];
        if (TryBeginFactionSlashZhuqueFanChoice(
                pending,
                provider,
                pair,
                CardKind.Slash))
        {
            return;
        }

        if (pending.IsBorrowedSwordUse)
        {
            ClearPendingDecision();
            BeginBorrowedSwordFactionSlashSlash(
                pending,
                provider,
                pair,
                CardKind.Slash,
                usesZhuqueFan: false);
            return;
        }

        if (pending.IsQinglongCrescentBladeUse)
        {
            ClearPendingDecision();
            BeginQinglongCrescentBladeFactionSlashSlash(
                pending,
                provider,
                pair,
                CardKind.Slash,
                usesZhuqueFan: false);
            return;
        }

        if (pending.IsProgramSkillUse)
        {
            ClearPendingDecision();
            BeginProvidedFactionSlashSlash(
                pending,
                provider,
                pair,
                CardKind.Slash,
                usesZhuqueFan: false);
            return;
        }

        var attack = pending.ResponseAttack ??
            throw new InvalidOperationException("A response FactionSlash has no attack continuation.");
        var duel = pending.Purpose == FactionCardRequestPurpose.DuelResponse
            ? ActiveDuel ?? throw new InvalidOperationException("A Duel FactionSlash response has no Duel continuation.")
            : null;
        var responseOpponentSeat = duel?.OpponentSeat ?? attack.SourceSeat;
        PopResponseWindow(attack.ResolutionId);
        SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        ActiveFactionCardRequest = null;
        ClearPendingDecision();
        MoveZhangbaResponseCards(provider, pair, pending.ResolutionId, responseOpponentSeat);
        AdvanceEventRulesAndQueueFact(new FactionSlashResolvedEvent(
            pending.ResolutionId,
            owner.Seat,
            Succeeded: true,
            provider.Seat,
            pair[0].Id,
            CardKind.Slash,
            IsActiveUse: false));
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(owner.Seat, CardKind.Slash);
        FinishZhangbaResponseCards(pair);
        RewardFactionRequestProvider(providerReward, provider.Seat);

        if (pending.Purpose == FactionCardRequestPurpose.DuelResponse)
        {
            AdvanceEventRulesAndQueueFact(new DuelResponseEvent(
                duel!.ResolutionId,
                owner.Seat,
                UsedSlash: true,
                SlashCardId: pair[0].Id,
                ResponseCardKind: CardKind.Slash));
            ContinueDuelAfterSuccessfulSlash(duel, owner.Seat);
            return;
        }

        var group = ActiveGroupCard ??
            throw new InvalidOperationException("A group FactionSlash response has no group continuation.");
        AdvanceEventRulesAndQueueFact(new GroupResponseEvent(
            group.ResolutionId,
            group.Card.Kind,
            CardKind.Slash,
            owner.Seat,
            UsedResponse: true,
            ResponseCardId: pair[0].Id,
            ResponseCardKind: CardKind.Slash));
        CompleteAttack(attack);
    }

    private void ResolveFactionSlashProgramMultiCardCandidateResponse(
        FactionCardRequestHandle pending,
        CharacterState provider,
        ProgramMultiCardViewAsSelection selection)
    {
        var providerReward = CaptureFactionProviderReward(pending);
        if (!SameContinuationOwner(ActiveFactionCardRequest, pending) ||
            !pending.AwaitingProviders ||
            provider.Seat != pending.CurrentCandidateSeat ||
            FindProgramMultiCardViewAsSelection(
                provider,
                selection.Cards.Select(card => card.Id).ToArray(),
                CardKind.Slash,
                forResponse: !pending.IsProgramSkillUse && !pending.IsBorrowedSwordUse &&
                             !pending.IsQinglongCrescentBladeUse,
                selection.Source) is null)
        {
            throw new InvalidOperationException("The configured FactionSlash conversion is not current.");
        }

        var owner = _players[pending.OwnerSeat];
        var conversion = selection.Source;
        if (pending.IsBorrowedSwordUse)
        {
            ClearPendingDecision();
            BeginBorrowedSwordFactionSlashSlash(
                pending,
                provider,
                selection.Cards,
                CardKind.Slash,
                usesZhuqueFan: false,
                conversionSource: conversion);
            return;
        }

        if (pending.IsQinglongCrescentBladeUse)
        {
            ClearPendingDecision();
            BeginQinglongCrescentBladeFactionSlashSlash(
                pending,
                provider,
                selection.Cards,
                CardKind.Slash,
                usesZhuqueFan: false,
                conversionSource: conversion);
            return;
        }

        if (pending.IsProgramSkillUse)
        {
            ClearPendingDecision();
            BeginProvidedFactionSlashSlash(
                pending,
                provider,
                selection.Cards,
                CardKind.Slash,
                usesZhuqueFan: false,
                conversionSource: conversion);
            return;
        }

        var attack = pending.ResponseAttack ??
            throw new InvalidOperationException("A response FactionSlash has no attack continuation.");
        var duel = pending.Purpose == FactionCardRequestPurpose.DuelResponse
            ? ActiveDuel ?? throw new InvalidOperationException("A Duel FactionSlash response has no Duel continuation.")
            : null;
        var responseOpponentSeat = duel?.OpponentSeat ?? attack.SourceSeat;
        PopResponseWindow(attack.ResolutionId);
        SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        ActiveFactionCardRequest = null;
        ClearPendingDecision();
        MoveProgramMultiCardResponse(
            provider,
            selection,
            pending.ResolutionId,
            responseOpponentSeat,
            owner.Seat);
        AdvanceEventRulesAndQueueFact(new FactionSlashResolvedEvent(
            pending.ResolutionId,
            owner.Seat,
            Succeeded: true,
            provider.Seat,
            selection.Cards[0].Id,
            CardKind.Slash,
            IsActiveUse: false));
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(owner.Seat, CardKind.Slash);
        FinishProgramMultiCardResponse(selection);
        RewardFactionRequestProvider(providerReward, provider.Seat);

        if (pending.Purpose == FactionCardRequestPurpose.DuelResponse)
        {
            AdvanceEventRulesAndQueueFact(new DuelResponseEvent(
                duel!.ResolutionId,
                owner.Seat,
                UsedSlash: true,
                SlashCardId: selection.Cards[0].Id,
                ResponseCardKind: CardKind.Slash));
            ContinueDuelAfterSuccessfulSlash(duel, owner.Seat);
            return;
        }

        var group = ActiveGroupCard ??
            throw new InvalidOperationException("A group FactionSlash response has no group continuation.");
        AdvanceEventRulesAndQueueFact(new GroupResponseEvent(
            group.ResolutionId,
            group.Card.Kind,
            CardKind.Slash,
            owner.Seat,
            UsedResponse: true,
            ResponseCardId: selection.Cards[0].Id,
            ResponseCardKind: CardKind.Slash));
        CompleteAttack(attack);
    }

    private void CompleteFactionSlashFailure(FactionCardRequestHandle pending)
    {
        if (!SameContinuationOwner(ActiveFactionCardRequest, pending))
        {
            throw new InvalidOperationException("The failed FactionSlash request is not current.");
        }

        ActiveFactionCardRequest = null;
        if (!pending.IsProgramSkillUse || pending.IsAssistedProgramUse)
            AdvanceEventRulesAndQueueFact(new FactionSlashResolvedEvent(
                pending.ResolutionId,
                pending.OwnerSeat,
                Succeeded: false,
                ProviderSeat: null,
                SlashCardId: null,
                EffectiveSlashKind: null,
                pending.IsBorrowedSwordUse || pending.IsQinglongCrescentBladeUse || pending.IsAssistedProgramUse,
                pending.TargetSeat));
        AddLog(
            "SkillResolved",
            $"没有{GetFactionName(pending.ProviderFactionId)}势力角色响应 {_players[pending.OwnerSeat].Name} 的【{GetFactionRequestDisplayName(pending)}】。",
            pending.OwnerSeat,
            pending.TargetSeat);

        if (pending.IsBorrowedSwordUse)
        {
            var borrowedSword = pending.BorrowedSword ??
                throw new InvalidOperationException("A Borrowed Sword FactionSlash failure has no parent resolution.");
            if (_resolutionStack.LastOrDefault() is ResponseWindowFrame response &&
                response.ParentFrameId == borrowedSword.ResolutionId)
            {
                PopResponseWindow(borrowedSword.ResolutionId);
            }

            BeginBorrowedSwordSlashChoice(borrowedSword, includeFactionSlash: false);
            return;
        }

        if (pending.IsQinglongCrescentBladeUse)
        {
            var qinglong = pending.QinglongCrescentBlade ??
                throw new InvalidOperationException("A Qinglong Crescent Blade FactionSlash failure has no parent resolution.");
            PublishQinglongCrescentBladeChoice(qinglong, includeFactionSlash: false);
            return;
        }

        if (!pending.IsProgramSkillUse)
        {
            PublishOwnerSlashDecision(pending);
            return;
        }

        var frameId = pending.ProgramSkillFrameId ??
            throw new InvalidOperationException("A faction-card request has no program-skill frame.");
        if (pending.IsAssistedProgramUse)
            CommitProgramChoiceResult(frameId, pending.AssistedResultBind!, "declined", pending.OwnerSeat, "没有角色提供【杀】。");
        AdvanceRuntimeProgram(frameId);
        _status = EngineStatus.Running;
    }

    private void PublishOwnerSlashDecision(FactionCardRequestHandle failedRequest)
    {
        var owner = _players[failedRequest.OwnerSeat];
        var slashes = GetResponseCards(owner, CardKind.Slash);
        var incomingCard = failedRequest.Purpose switch
        {
            FactionCardRequestPurpose.DuelResponse => CardKind.Duel,
            FactionCardRequestPurpose.GroupResponse => ActiveGroupCard?.Card.Kind ?? CardKind.BarbarianAssault,
            _ => throw new InvalidOperationException("Only response FactionSlash can restore an owner response prompt.")
        };
        var sourceSeat = failedRequest.Purpose == FactionCardRequestPurpose.DuelResponse
            ? ActiveDuel?.OpponentSeat
            : ActiveGroupCard?.SourceSeat;
        _pendingDecision = new PendingDecision(
            DecisionKind.RespondSlash,
            owner.Seat,
            "【激将】无人响应，是否由你自己打出【杀】？",
            slashes.Select(card => card.Id).ToArray(),
            [],
            sourceSeat,
            incomingCard)
        {
            PromptId = CreatePromptId(),
            RequiredCardKind = CardKind.Slash,
            Choices = CreateResponseChoices(
                slashes,
                CardKind.Slash,
                incomingCard,
                card => GetEffectiveResponseKind(owner, card, CardKind.Slash),
                includeFactionSlash: false,
                responder: owner)
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void CompleteFactionSlashResponse(
        FactionCardRequestHandle pending,
        CharacterState provider,
        Card selectedSlash)
    {
        if (TryBeginCardDeclaration(provider, selectedSlash, CardKind.Slash,
            PeekDeclarationConversion(provider, selectedSlash, CardKind.Slash, false),
            DeclarationReturn(CardDeclarationPurpose.FactionSlash, pending.OwnerSeat), [pending.ResponseAttack!.SourceSeat])) return;
        var providerReward = CaptureFactionProviderReward(pending);
        var attack = pending.ResponseAttack ??
            throw new InvalidOperationException("A response FactionSlash has no attack continuation.");
        if (!SameContinuationOwner(ActiveFactionCardRequest, pending) ||
            !SameAttackOwner(ActiveCardAttack, attack) ||
            provider.Seat != pending.CurrentCandidateSeat)
        {
            throw new InvalidOperationException("The completed FactionSlash response is not current.");
        }

        var owner = _players[pending.OwnerSeat];
        var effectiveKind = GetEffectiveResponseKind(provider, selectedSlash, CardKind.Slash);
        var responseConversion = GetSelectedResponseConversion(provider, selectedSlash, effectiveKind);
        var responseFrom = FindOwnedCardLocation(provider, selectedSlash);
        var responseCostIsRed = CapturePhysicalCardColor(provider.Seat,selectedSlash);
        var duel = pending.Purpose == FactionCardRequestPurpose.DuelResponse
            ? ActiveDuel ?? throw new InvalidOperationException("A Duel FactionSlash response has no Duel continuation.")
            : null;
        var responseOpponentSeat = duel?.OpponentSeat ?? attack.SourceSeat;
        PopResponseWindow(attack.ResolutionId);
        SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        ActiveFactionCardRequest = null;
        ClearPendingDecision();
        MoveCard(
            selectedSlash,
            responseFrom,
            CardLocation.Processing,
            CardMoveReasons.Respond);
        AddLog(
            "CardResponded",
            $"{provider.Name} 响应【激将】，替 {owner.Name} 打出【{CardCatalog.Get(effectiveKind).DisplayName}】。",
            provider.Seat,
            owner.Seat);
        AdvanceEventRulesAndQueueFact(new FactionSlashResolvedEvent(
            pending.ResolutionId,
            owner.Seat,
            Succeeded: true,
            provider.Seat,
            selectedSlash.Id,
            effectiveKind,
            IsActiveUse: false));
        AdvanceEventRulesAndQueueFact(new CardRespondedEvent(
            selectedSlash.Id,
            owner.Seat,
            responseOpponentSeat,
            effectiveKind));
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(owner.Seat, effectiveKind);
        RewardFactionRequestProvider(providerReward, provider.Seat);

        if (pending.Purpose == FactionCardRequestPurpose.DuelResponse)
        {
            AdvanceEventRulesAndQueueFact(new DuelResponseEvent(
                duel!.ResolutionId,
                owner.Seat,
                UsedSlash: true,
                SlashCardId: selectedSlash.Id,
                ResponseCardKind: effectiveKind));
            var responseCost = new CardActionCost(selectedSlash.Id, selectedSlash.Kind, responseFrom,responseCostIsRed);
            if (TryBeginCardResponsePrograms(
                    attack,
                    owner,
                    provider,
                    requesterSeat: owner.Seat,
                    responseOpponentSeat,
                    effectiveKind,
                    [responseCost],
                    ProgramCardContinuation.FactionSlashDuelSlash,
                    responseConversion))
            {
                return;
            }
            MoveCard(
                selectedSlash,
                CardLocation.Processing,
                CardLocation.DiscardPile,
                CardMoveReasons.ResponseFinished);
            ContinueDuelAfterSuccessfulSlash(duel, owner.Seat);
            return;
        }

        var group = ActiveGroupCard ??
            throw new InvalidOperationException("A group FactionSlash response has no group continuation.");
        AdvanceEventRulesAndQueueFact(new GroupResponseEvent(
            group.ResolutionId,
            group.Card.Kind,
            CardKind.Slash,
            owner.Seat,
            UsedResponse: true,
            ResponseCardId: selectedSlash.Id,
            ResponseCardKind: effectiveKind));
        var groupResponseCost = new CardActionCost(selectedSlash.Id, selectedSlash.Kind, responseFrom,responseCostIsRed);
        if (TryBeginCardResponsePrograms(
                attack,
                owner,
                provider,
                requesterSeat: owner.Seat,
                responseOpponentSeat,
                effectiveKind,
                [groupResponseCost],
                ProgramCardContinuation.FactionSlashGroupResponse,
                responseConversion))
        {
            return;
        }
        MoveCard(
            selectedSlash,
            CardLocation.Processing,
            CardLocation.DiscardPile,
            CardMoveReasons.ResponseFinished);
        CompleteAttack(attack);
    }

    private void BeginProvidedFactionSlashSlash(
        FactionCardRequestHandle pending,
        CharacterState provider,
        IReadOnlyList<Card> physicalCards,
        CardKind effectiveKind,
        bool usesZhuqueFan,
        CardConversionSource? conversionSource = null)
    {
        if (physicalCards.Count == 1 && TryBeginCardDeclaration(provider, physicalCards[0], effectiveKind,
            PeekDeclarationConversion(provider, physicalCards[0], effectiveKind, true, conversionSource),
            DeclarationReturn(CardDeclarationPurpose.ProvidedSlash, pending.OwnerSeat, fan: usesZhuqueFan, finalKind: effectiveKind), [pending.TargetSeat!.Value])) return;
        var providerReward = CaptureFactionProviderReward(pending);
        var selectedSlash = physicalCards.FirstOrDefault() ??
            throw new InvalidOperationException("An active FactionSlash Slash must retain a physical card.");
        var owner = _players[pending.OwnerSeat];
        var target = _players[pending.TargetSeat ??
            throw new InvalidOperationException("An active FactionSlash use has no target.")];
        if (!SameContinuationOwner(ActiveFactionCardRequest, pending) ||
            !pending.IsProgramSkillUse ||
            (pending.IsAssistedProgramUse
                ? !IsAssistedProvidedSlashTarget(owner.Seat, target.Seat, effectiveKind,ProvidedSpecificSlashRank(owner,provider,physicalCards,conversionSource is null && physicalCards.Count==2,effectiveKind),allowPotentialRank:false)
                : !CanUseProvidedSlashTarget(owner, target, effectiveKind, physicalSuit:PhysicalGroupSuit(owner,physicalCards),allowAnyPhysicalSuit:false, effectiveColor:PhysicalGroupColor(owner,physicalCards), effectiveRank:ProvidedSpecificSlashRank(owner,provider,physicalCards,conversionSource is null && physicalCards.Count==2,effectiveKind))))
        {
            throw new InvalidOperationException("The active FactionSlash target is no longer legal.");
        }

        if (!CanSpendSlashUse(owner, target, ignoresCount: pending.IsAssistedProgramUse, effectiveKind))
        {
            throw new InvalidOperationException("The FactionSlash Slash limit has already been reached.");
        }

        pending.AwaitingProviders = false;
        var frameId = pending.ProgramSkillFrameId ??
            throw new InvalidOperationException("A faction-card request has no program-skill frame.");
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != frameId)
            throw new InvalidOperationException("A faction-card request lost its program-skill parent.");
        if (pending.IsAssistedProgramUse)
        {
            ValidateAssistedFactionSlashParent(pending, frame);
            CommitProgramChoiceResult(frameId, pending.AssistedResultBind!, "used-slash", owner.Seat, "通过势力响应使用【杀】。");
            AdvanceEventRulesAndQueueFact(new FactionSlashResolvedEvent(pending.ResolutionId, owner.Seat,
                Succeeded: true, provider.Seat, selectedSlash.Id, effectiveKind, IsActiveUse: true, target.Seat));
        }
        AddLog(
            "CardUsed",
            $"{provider.Name} 响应【{GetFactionRequestDisplayName(pending)}】，由 {owner.Name} 对 {target.Name} 使用【{CardCatalog.Get(effectiveKind).DisplayName}】。",
            owner.Seat,
            target.Seat);
        ResolveSlashCore(
            owner,
            target,
            selectedSlash,
            effectiveKind,
            provider.Seat,
            pending,
            physicalCards: physicalCards,
            countsTowardSlashLimit: !pending.IsAssistedProgramUse,
            usesZhuqueFan: usesZhuqueFan,
            conversionSource: conversionSource);
        RewardFactionRequestProvider(providerReward, provider.Seat);
    }

    private void BeginBorrowedSwordFactionSlashSlash(
        FactionCardRequestHandle pending,
        CharacterState provider,
        IReadOnlyList<Card> physicalCards,
        CardKind effectiveKind,
        bool usesZhuqueFan,
        CardConversionSource? conversionSource = null)
    {
        var providerReward = CaptureFactionProviderReward(pending);
        var selectedSlash = physicalCards.FirstOrDefault() ??
            throw new InvalidOperationException("A Borrowed Sword FactionSlash Slash must retain a physical card.");
        var borrowedSword = pending.BorrowedSword ??
            throw new InvalidOperationException("A Borrowed Sword FactionSlash use has no parent resolution.");
        var owner = _players[pending.OwnerSeat];
        var target = _players[pending.TargetSeat ??
            throw new InvalidOperationException("A Borrowed Sword FactionSlash use has no target.")];
        if (!SameContinuationOwner(ActiveFactionCardRequest, pending) ||
            !SameContinuationOwner(ActiveBorrowedSword, borrowedSword) ||
            !pending.IsBorrowedSwordUse ||
            !IsLegalBorrowedSwordSlashTarget(owner, target, effectiveKind,physicalSuit:HasSlashUseDistanceBySuit(owner,effectiveKind,Suit.Diamond) ? PhysicalGroupSuit(owner,physicalCards) : null,allowAnyPhysicalSuit:false,effectiveColor:PhysicalGroupColor(owner,physicalCards),effectiveRank:ProvidedSpecificSlashRank(owner,provider,physicalCards,conversionSource is null && physicalCards.Count==2,effectiveKind)))
        {
            throw new InvalidOperationException("The Borrowed Sword FactionSlash target is no longer legal.");
        }

        if (physicalCards.Count == 1 && TryBeginCardDeclaration(provider, selectedSlash, effectiveKind,
            PeekDeclarationConversion(provider, selectedSlash, effectiveKind, true, conversionSource ?? _selectedResponseConversion),
            DeclarationReturn(CardDeclarationPurpose.BorrowedSwordProvidedSlash, pending.OwnerSeat, fan: usesZhuqueFan, finalKind: effectiveKind), [target.Seat])) return;

        pending.AwaitingProviders = false;
        borrowedSword.AwaitingSlashChoice = false;
        borrowedSword.SlashCardId = selectedSlash.Id;
        borrowedSword.EffectiveSlashKind = effectiveKind;
        PopResponseWindow(borrowedSword.ResolutionId);
        SetCardUseStep(borrowedSword.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        AdvanceEventRulesAndQueueFact(new FactionSlashResolvedEvent(
            borrowedSword.ResolutionId,
            owner.Seat,
            Succeeded: true,
            provider.Seat,
            selectedSlash.Id,
            effectiveKind,
            IsActiveUse: true,
            target.Seat));
        AddLog(
            "CardUsed",
            $"{provider.Name} 响应【激将】，由 {owner.Name} 按【借刀杀人】要求对 {target.Name} 使用【{CardCatalog.Get(effectiveKind).DisplayName}】。",
            owner.Seat,
            target.Seat);
        ResolveSlashCore(
            owner,
            target,
            selectedSlash,
            effectiveKind,
            provider.Seat,
            factionRequest: pending,
            borrowedSword: borrowedSword,
            physicalCards: physicalCards,
            usesZhuqueFan: usesZhuqueFan,
            conversionSource: conversionSource);
        RewardFactionRequestProvider(providerReward, provider.Seat);
    }

    private void BeginQinglongCrescentBladeFactionSlashSlash(
        FactionCardRequestHandle pending,
        CharacterState provider,
        IReadOnlyList<Card> physicalCards,
        CardKind effectiveKind,
        bool usesZhuqueFan,
        CardConversionSource? conversionSource = null)
    {
        var providerReward = CaptureFactionProviderReward(pending);
        var selectedSlash = physicalCards.FirstOrDefault() ??
            throw new InvalidOperationException("A Qinglong Crescent Blade FactionSlash Slash must retain a physical card.");
        var qinglong = pending.QinglongCrescentBlade ??
            throw new InvalidOperationException("A Qinglong Crescent Blade FactionSlash use has no parent resolution.");
        var owner = _players[pending.OwnerSeat];
        var target = _players[pending.TargetSeat ??
            throw new InvalidOperationException("A Qinglong Crescent Blade FactionSlash use has no target.")];
        if (!SameContinuationOwner(ActiveFactionCardRequest, pending) ||
            !SameContinuationOwner(ActiveQinglongCrescentBlade, qinglong) ||
            !pending.IsQinglongCrescentBladeUse ||
            !CanUseQinglongCrescentBladeTarget(owner, target, effectiveKind,PhysicalGroupColor(owner,physicalCards),allowAnyColor:false,physicalSuit:PhysicalGroupSuit(owner,physicalCards),effectiveRank:ProvidedSpecificSlashRank(owner,provider,physicalCards,conversionSource is null && physicalCards.Count==2,effectiveKind)))
        {
            throw new InvalidOperationException("The Qinglong Crescent Blade FactionSlash target is no longer legal.");
        }

        if (usesZhuqueFan)
        {
            throw new InvalidOperationException("Qinglong Crescent Blade cannot simultaneously provide Zhuque Fan conversion.");
        }
        if (physicalCards.Count == 1 && TryBeginCardDeclaration(provider, selectedSlash, effectiveKind,
            PeekDeclarationConversion(provider, selectedSlash, effectiveKind, true, conversionSource ?? _selectedResponseConversion),
            DeclarationReturn(CardDeclarationPurpose.QinglongProvidedSlash, pending.OwnerSeat, fan: usesZhuqueFan, finalKind: effectiveKind), [target.Seat])) return;

        pending.AwaitingProviders = false;
        AdvanceEventRulesAndQueueFact(new FactionSlashResolvedEvent(
            pending.ResolutionId,
            owner.Seat,
            Succeeded: true,
            provider.Seat,
            selectedSlash.Id,
            effectiveKind,
            IsActiveUse: true,
            target.Seat));
        AddLog(
            "CardUsed",
            $"{provider.Name} 响应【激将】，由 {owner.Name} 发动【青龙偃月刀】对 {target.Name} 使用【{CardCatalog.Get(effectiveKind).DisplayName}】。",
            owner.Seat,
            target.Seat);
        BeginQinglongCrescentBladeFollowup(
            qinglong,
            selectedSlash,
            effectiveKind,
            provider.Seat,
            physicalCards,
            pending,
            conversionSource);
        RewardFactionRequestProvider(providerReward, provider.Seat);
    }

    private bool TryBeginFactionSlashZhuqueFanChoice(
        FactionCardRequestHandle pending,
        CharacterState provider,
        IReadOnlyList<Card> physicalCards,
        CardKind baseEffectiveKind)
    {
        var owner = _players[pending.OwnerSeat];
        if ((!pending.IsProgramSkillUse && !pending.IsBorrowedSwordUse) ||
            baseEffectiveKind != CardKind.Slash ||
            (physicalCards.Count == 1 && physicalCards[0].Kind != CardKind.Slash) ||
            !HasZhuqueFan(owner))
        {
            return false;
        }

        if (pending.IsAssistedProgramUse && pending.TargetSeat is { } assistedTarget)
        {
            var normalAllowed = IsAssistedProvidedSlashTarget(owner.Seat, assistedTarget, CardKind.Slash,ProvidedSpecificSlashRank(owner,provider,physicalCards,physicalCards.Count==2),allowPotentialRank:false);
            var fireAllowed = IsAssistedProvidedSlashTarget(owner.Seat, assistedTarget, CardKind.FireSlash,ProvidedSpecificSlashRank(owner,provider,physicalCards,physicalCards.Count==2,CardKind.FireSlash),allowPotentialRank:false);
            if (normalAllowed != fireAllowed)
            {
                ClearPendingDecision();
                BeginProvidedFactionSlashSlash(pending, provider, physicalCards,
                    fireAllowed ? CardKind.FireSlash : CardKind.Slash, usesZhuqueFan: fireAllowed);
                return true;
            }
        }

        if (!pending.AwaitingProviders ||
            pending.AwaitingZhuqueFanChoice ||
            physicalCards.Count == 0 ||
            provider.Seat != pending.CurrentCandidateSeat)
        {
            throw new InvalidOperationException("The FactionSlash Zhuque Fan choice cannot start from this continuation.");
        }

        ClearPendingDecision();
        pending.AwaitingProviders = false;
        pending.AwaitingZhuqueFanChoice = true;
        pending.ZhuqueFanProviderSeat = provider.Seat;
        pending.ZhuqueFanPhysicalCards = Array.AsReadOnly(physicalCards.ToArray());
        _pendingDecision = new PendingDecision(
            DecisionKind.ZhuqueFan,
            owner.Seat,
            $"{provider.Name} 已为【激将】提供普通【杀】，是否发动【朱雀羽扇】改为【火杀】？",
            [],
            [],
            SourceSeat: provider.Seat,
            IncomingCard: CardKind.Slash)
        {
            PromptId = CreatePromptId(),
            TargetSeat = pending.TargetSeat,
            Choices =
            [
                new PromptChoice(
                    new ChoiceId("zhuque-fan.normal"),
                    "保持为普通【杀】。",
                    [],
                    [],
                    new Dictionary<string, string>
                    {
                        ["action"] = "zhuque-fan-normal",
                        ["equipment"] = CardKind.ZhuqueFan.ToString()
                    }),
                new PromptChoice(
                    new ChoiceId("zhuque-fan.fire"),
                    "发动【朱雀羽扇】，将此【杀】改为【火杀】。",
                    [],
                    [],
                    new Dictionary<string, string>
                    {
                        ["action"] = "zhuque-fan-fire",
                        ["equipment"] = CardKind.ZhuqueFan.ToString()
                    })
            ]
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return true;
    }

    private void ResolveFactionSlashZhuqueFanChoice(
        FactionCardRequestHandle pending,
        bool convertToFireSlash)
    {
        if (!SameContinuationOwner(ActiveFactionCardRequest, pending) ||
            !pending.AwaitingZhuqueFanChoice ||
            pending.AwaitingProviders ||
            _pendingDecision is not { Kind: DecisionKind.ZhuqueFan } decision ||
            decision.PlayerSeat != pending.OwnerSeat ||
            pending.ZhuqueFanProviderSeat is not { } providerSeat ||
            pending.ZhuqueFanPhysicalCards.Count == 0)
        {
            throw new InvalidOperationException("The FactionSlash Zhuque Fan choice is not current.");
        }

        var owner = _players[pending.OwnerSeat];
        if (convertToFireSlash && !HasZhuqueFan(owner))
        {
            throw new InvalidOperationException("Zhuque Fan is no longer available for this conversion.");
        }

        var provider = _players[providerSeat];
        var physicalCards = pending.ZhuqueFanPhysicalCards;
        pending.AwaitingZhuqueFanChoice = false;
        pending.ZhuqueFanProviderSeat = null;
        pending.ZhuqueFanPhysicalCards = [];
        ClearPendingDecision();
        _status = EngineStatus.Running;
        var effectiveKind = convertToFireSlash ? CardKind.FireSlash : CardKind.Slash;
        if (pending.IsBorrowedSwordUse)
        {
            BeginBorrowedSwordFactionSlashSlash(
                pending,
                provider,
                physicalCards,
                effectiveKind,
                convertToFireSlash);
            return;
        }

        if (!pending.IsProgramSkillUse)
        {
            throw new InvalidOperationException("Only an active FactionSlash Slash use can resume from Zhuque Fan.");
        }

        BeginProvidedFactionSlashSlash(
            pending,
            provider,
            physicalCards,
            effectiveKind,
            convertToFireSlash);
    }

    private void CompleteQinglongCrescentBladeFactionSlash(FactionCardRequestHandle pending)
    {
        if (!SameContinuationOwner(ActiveFactionCardRequest, pending) ||
            !pending.IsQinglongCrescentBladeUse ||
            pending.AwaitingProviders)
        {
            throw new InvalidOperationException("The completed Qinglong Crescent Blade FactionSlash continuation is invalid.");
        }

        ActiveFactionCardRequest = null;
    }

    private void ResolvePendingAiResponse()
    {
        if (_pendingDecision is { Kind: DecisionKind.RespondDodge, ValidCardIds.Count: 0 } dodgePrompt &&
            dodgePrompt.Choices.FirstOrDefault(item => item.Parameters.GetValueOrDefault("response") == "program-dodge") is { } programDodge)
        {
            BeginProgramDodgeResponse(programDodge, false);
            return;
        }
        if (ActiveQilinBow is not null)
        {
            ResolvePendingAiQilinBow();
            return;
        }

        if (ActiveIceSword is not null)
        {
            ResolvePendingAiIceSword();
            return;
        }

        if (ActiveQinglongCrescentBlade is not null &&
            _pendingDecision?.Kind == DecisionKind.QinglongCrescentBlade)
        {
            ResolvePendingAiQinglongCrescentBlade();
            return;
        }

        if (ActiveCixiongDoubleSwords is not null)
        {
            ResolvePendingAiCixiongDoubleSwords();
            return;
        }

        if (ActiveStoneAxe is not null)
        {
            ResolvePendingAiStoneAxe();
            return;
        }

        if (ActiveFactionCardRequest is { AwaitingZhuqueFanChoice: true })
        {
            ResolvePendingAiZhuqueFan();
            return;
        }

        if (ActiveFactionCardRequest is { AwaitingProviders: true })
        {
            ResolvePendingAiFactionSlash();
            return;
        }

        if (ActiveFactionDefense is not null)
        {
            ResolvePendingAiFactionDefense();
            return;
        }

        if (ActiveBorrowedSword is { AwaitingSlashChoice: true })
        {
            ResolvePendingAiBorrowedSword();
            return;
        }

        if (ActiveDuel is not null)
        {
            ResolvePendingAiDuel();
            return;
        }

        if (ActiveGroupCard is { Effect: GroupCardEffect.ResponseAttack })
        {
            ResolvePendingAiGroupAttack();
            return;
        }

        ResolvePendingAiDodge();
    }

    private void ResolvePendingAiQinglongCrescentBlade()
    {
        var pending = ActiveQinglongCrescentBlade ??
            throw new InvalidOperationException("AI Qinglong Crescent Blade response has no active resolution.");
        var attack = pending.Attack;
        if (!SameAttackOwner(ActiveCardAttack, attack) ||
            _pendingDecision is not { Kind: DecisionKind.QinglongCrescentBlade } decision ||
            decision.PlayerSeat != attack.SourceSeat)
        {
            throw new InvalidOperationException("The pending AI Qinglong Crescent Blade prompt is inconsistent.");
        }

        var (choiceId, thought) = _aiBrains[attack.SourceSeat].ChooseQinglongCrescentBladeChoice(
            CreateSnapshot(attack.SourceSeat),
            attack.TargetSeat,
            ++_thoughtSequence);
        AddThought(thought);
        var selected = decision.Choices.Single(choice => choice.Id == choiceId);
        ResolveQinglongCrescentBladeChoice(selected);
        AdvanceRulesAndPublishState();
    }

    private void ResolvePendingAiIceSword()
    {
        var pending = ActiveIceSword ??
            throw new InvalidOperationException("AI Ice Sword response has no active resolution.");
        var attack = pending.Attack;
        if (!SameAttackOwner(ActiveCardAttack, attack) ||
            _pendingDecision is not { Kind: DecisionKind.IceSword } decision ||
            decision.PlayerSeat != attack.SourceSeat)
        {
            throw new InvalidOperationException("The pending AI Ice Sword prompt is inconsistent.");
        }

        var (choiceId, thought) = _aiBrains[attack.SourceSeat].ChooseIceSwordChoice(
            CreateSnapshot(attack.SourceSeat),
            attack.TargetSeat,
            pending.PreventedDamageAmount,
            ++_thoughtSequence);
        AddThought(thought);
        var selected = decision.Choices.Single(choice => choice.Id == choiceId);
        ResolveIceSwordChoice(selected);
        AdvanceRulesAndPublishState();
    }

    private void ResolvePendingAiQilinBow()
    {
        var pending = ActiveQilinBow ??
            throw new InvalidOperationException("AI Qilin Bow response has no active resolution.");
        var attack = pending.Attack;
        if (!SameAttackOwner(ActiveCardAttack, attack) ||
            _pendingDecision is not { Kind: DecisionKind.QilinBow } decision ||
            decision.PlayerSeat != attack.SourceSeat)
        {
            throw new InvalidOperationException("The pending AI Qilin Bow prompt is inconsistent.");
        }

        var (choiceId, thought) = _aiBrains[attack.SourceSeat].ChooseQilinBowChoice(
            CreateSnapshot(attack.SourceSeat),
            attack.TargetSeat,
            ++_thoughtSequence);
        AddThought(thought);
        var selected = decision.Choices.Single(choice => choice.Id == choiceId);
        ResolveQilinBowChoice(selected);
        AdvanceRulesAndPublishState();
    }


    private void ResolvePendingAiCixiongDoubleSwords()
    {
        var pending = ActiveCixiongDoubleSwords ??
            throw new InvalidOperationException("AI Cixiong Double Swords response has no active resolution.");
        var attack = pending.Attack;
        var responderSeat = pending.Stage == CixiongDoubleSwordsStage.SourceActivation
            ? attack.SourceSeat
            : attack.TargetSeat;
        if (!SameAttackOwner(ActiveCardAttack, attack) ||
            _pendingDecision is not { Kind: DecisionKind.CixiongDoubleSwords } decision ||
            decision.PlayerSeat != responderSeat)
        {
            throw new InvalidOperationException("The pending AI Cixiong Double Swords prompt is inconsistent.");
        }

        var (choiceId, thought) = _aiBrains[responderSeat].ChooseCixiongDoubleSwordsChoice(
            CreateSnapshot(responderSeat),
            attack.SourceSeat,
            attack.TargetSeat,
            ++_thoughtSequence);
        AddThought(thought);
        var selected = decision.Choices.Single(choice => choice.Id == choiceId);
        ResolveCixiongDoubleSwordsChoice(selected);
        AdvanceRulesAndPublishState();
    }

    private void ResolvePendingAiStoneAxe()
    {
        var pending = ActiveStoneAxe ??
            throw new InvalidOperationException("AI Stone Axe response has no active resolution.");
        var attack = pending.Attack;
        if (!SameAttackOwner(ActiveCardAttack, attack) ||
            _pendingDecision is not { Kind: DecisionKind.StoneAxe } decision ||
            decision.PlayerSeat != attack.SourceSeat)
        {
            throw new InvalidOperationException("The pending AI Stone Axe prompt is inconsistent.");
        }

        var source = _players[attack.SourceSeat];
        var (discardedCardIds, thought) = _aiBrains[source.Seat].ChooseStoneAxeResponse(
            CreateSnapshot(source.Seat),
            attack.TargetSeat,
            ++_thoughtSequence);
        AddThought(thought);
        ResolveStoneAxeChoice(discardedCardIds);
        AdvanceRulesAndPublishState();
    }

    private void ResolvePendingAiZhuqueFan()
    {
        var pending = ActiveFactionCardRequest ??
            throw new InvalidOperationException("AI Zhuque Fan response has no FactionSlash continuation.");
        if (!pending.AwaitingZhuqueFanChoice ||
            _pendingDecision is not { Kind: DecisionKind.ZhuqueFan } decision ||
            decision.PlayerSeat != pending.OwnerSeat ||
            pending.TargetSeat is not { } targetSeat)
        {
            throw new InvalidOperationException("The pending AI Zhuque Fan prompt is inconsistent.");
        }

        var (choiceId, thought) = _aiBrains[pending.OwnerSeat].ChooseZhuqueFanChoice(
            CreateSnapshot(pending.OwnerSeat),
            targetSeat,
            ++_thoughtSequence);
        AddThought(thought);
        var selected = decision.Choices.Single(choice => choice.Id == choiceId);
        ResolveFactionSlashZhuqueFanChoice(
            pending,
            selected.Parameters.GetValueOrDefault("action") == "zhuque-fan-fire");
        AdvanceRulesAndPublishState();
    }

    private void ResolvePendingAiBorrowedSword()
    {
        var pending = ActiveBorrowedSword ??
            throw new InvalidOperationException("AI Borrowed Sword response has no active resolution.");
        if (!pending.AwaitingSlashChoice ||
            _pendingDecision is not { Kind: DecisionKind.RespondSlash } decision ||
            decision.PlayerSeat != pending.WeaponOwnerSeat)
        {
            throw new InvalidOperationException("The pending AI Borrowed Sword prompt is inconsistent.");
        }

        var owner = _players[pending.WeaponOwnerSeat];
        var target = _players[pending.SlashTargetSeat];
        var view = CreateSnapshot(owner.Seat);
        var (useSlash, thought) = _aiBrains[owner.Seat].ChooseBorrowedSwordResponse(
            view,
            pending.SourceSeat,
            target.Seat,
            ++_thoughtSequence);
        AddThought(thought);
        var canRequestFactionSlash = decision.Choices.Any(choice =>
            choice.Parameters.GetValueOrDefault("response") == "faction-slash-request");
        if (useSlash && canRequestFactionSlash)
        {
            BeginBorrowedSwordFactionSlashRequest(pending);
            AdvanceRulesAndPublishState();
            return;
        }

        if (useSlash && decision.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("response") == "tiered-round-zero-forced-slash") is { } zeroForced)
        { TryResolveTieredRoundZeroForcedSlash(zeroForced, false); return; }
        var selectedSlash = useSlash
            ? GetBorrowedSwordSlashCards(owner, target).FirstOrDefault()
            : null;
        if (selectedSlash is not null)
        {
            CaptureAiCardResponseChoice(decision, selectedSlash,
                IsSlashCard(selectedSlash.Kind) ? selectedSlash.Kind : CardKind.Slash);
            ResolveBorrowedSwordSlashChoice(pending, selectedSlash);
        }
        else if (useSlash && GetZhangbaSlashPairs(owner).FirstOrDefault() is { } pair)
        {
            ResolveBorrowedSwordZhangbaSlashChoice(pending, pair);
        }
        else if (useSlash && GetProgramMultiCardViewAsSelections(
                     owner, CardKind.Slash, forResponse: false).FirstOrDefault() is { } programSelection)
        {
            ResolveBorrowedSwordProgramMultiCardSlashChoice(pending, programSelection);
        }
        else
        {
            CompleteBorrowedSwordWithoutSlash(pending, transferWeapon: true);
        }

        AdvanceRulesAndPublishState();
    }

    private void ResolvePendingAiFactionSlash()
    {
        var pending = ActiveFactionCardRequest ??
            throw new InvalidOperationException("AI FactionSlash response has no active request.");
        if (_pendingDecision is { Kind: DecisionKind.RespondSlash } cost &&
            cost.PlayerSeat == pending.OwnerSeat &&
            cost.Choices.FirstOrDefault()?.Parameters.GetValueOrDefault("response") == "faction-request-cost")
        {
            ResolveFactionRequestCostChoice(cost.Choices.First(), false);
            return;
        }
        if (!pending.AwaitingProviders ||
            _pendingDecision is not { Kind: DecisionKind.RespondSlash } decision ||
            decision.PlayerSeat != pending.CurrentCandidateSeat)
        {
            throw new InvalidOperationException("The pending AI FactionSlash prompt is inconsistent.");
        }

        var provider = _players[decision.PlayerSeat];
        var slashes = GetFactionSlashSlashCards(pending, provider);
        var zhangbaPair = GetFactionRequestZhangbaPairs(pending, provider).FirstOrDefault();
        var isResponse = !pending.IsProgramSkillUse && !pending.IsBorrowedSwordUse &&
                         !pending.IsQinglongCrescentBladeUse;
        var programSelection = GetFactionRequestMultiCardSelections(pending, provider).FirstOrDefault();
        var view = CreateSnapshot(provider.Seat);
        var (useSlash, thought) = _aiBrains[provider.Seat]
            .ChooseFactionSlashResponse(view, pending.OwnerSeat, ++_thoughtSequence);
        AddThought(thought);
        if (pending.IsAssistedProgramUse)
        {
            var selected = useSlash
                ? decision.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("response") != "faction-slash-decline")
                : null;
            ResolveAssistedFactionSlashChoice(selected ?? decision.Choices.Single(choice => choice.Parameters.GetValueOrDefault("response") == "faction-slash-decline"), false);
            return;
        }
        var selectedSlash = useSlash ? slashes.FirstOrDefault() : null;
        if (selectedSlash is not null)
            CaptureAiCardResponseChoice(decision, selectedSlash,
                GetFactionSlashEffectiveSlashKind(pending, provider, selectedSlash));
        if (useSlash && selectedSlash is null && zhangbaPair is not null)
        {
            ResolveFactionSlashZhangbaCandidateResponse(pending, provider, zhangbaPair);
            AdvanceRulesAndPublishState();
            return;
        }
        if (useSlash && selectedSlash is null && programSelection is not null)
        {
            ResolveFactionSlashProgramMultiCardCandidateResponse(pending, provider, programSelection);
            AdvanceRulesAndPublishState();
            return;
        }
        ResolveFactionSlashCandidateResponse(
            pending,
            useSlash: selectedSlash is not null,
            requestedCardId: selectedSlash?.Id);
        AdvanceRulesAndPublishState();
    }

    private void ResolvePendingAiFactionDefense()
    {
        var pending = ActiveFactionDefense ??
            throw new InvalidOperationException("AI FactionDefense response has no active request.");
        if (_pendingDecision is not { Kind: DecisionKind.RespondDodge } decision ||
            decision.PlayerSeat != pending.CurrentCandidateSeat)
        {
            throw new InvalidOperationException("The pending AI FactionDefense prompt is inconsistent.");
        }

        var provider = _players[decision.PlayerSeat];
        var responseCards = GetResponseCards(provider, CardKind.Dodge);
        var view = CreateSnapshot(provider.Seat);
        var (useDodge, useBagua, thought) = _aiBrains[provider.Seat]
            .ChooseFactionDefenseResponse(view, pending.OwnerSeat, responseCards.Count > 0, ++_thoughtSequence);
        AddThought(thought);
        var selectedDodge = useDodge ? responseCards.FirstOrDefault() : null;
        if (selectedDodge is not null)
            CaptureAiCardResponseChoice(decision, selectedDodge,
                GetEffectiveResponseKind(provider, selectedDodge, CardKind.Dodge));
        ResolveFactionDefenseCandidateResponse(
            pending,
            useDodge: selectedDodge is not null,
            useBagua: useBagua && HasBagua(provider),
            requestedCardId: selectedDodge?.Id);
        AdvanceRulesAndPublishState();
    }

    private void ResolvePendingAiNullification()
    {
        var pending = ActiveNullificationWindow ??
            throw new InvalidOperationException("AI Nullification continuation is missing.");
        var decision = _pendingDecision ??
            throw new InvalidOperationException("AI Nullification continuation has no prompt.");
        if (decision.Kind != DecisionKind.Nullification ||
            pending.CandidateIndex >= pending.CandidateSeats.Count ||
            decision.PlayerSeat != pending.CandidateSeats[pending.CandidateIndex])
        {
            throw new InvalidOperationException("The pending AI Nullification prompt is inconsistent.");
        }

        var responder = _players[decision.PlayerSeat];
        if (responder.IsHuman)
        {
            throw new InvalidOperationException("A human Nullification prompt cannot be resolved as AI.");
        }

        if (TryResolveTieredRoundZeroAiCounterspell()) return;
        var view = CreateSnapshot(responder.Seat);
        var (cardId, thought) = _aiBrains[responder.Seat].ChooseNullification(
            view,
            GetNullificationEffectCard(pending).Kind,
            pending.SourceSeat,
            GetNullificationTargetSeat(pending),
            pending.EffectNullified,
            pending.ChainDepth,
            decision.ValidCardIds,
            ++_thoughtSequence,
            pending.TargetSeats,
            includeEquipment: HasCardPolicy(responder, SkillProgramCardPolicyKind.UnrespondableNullification, CardKind.Nullification) || HasProvenanceCounterspellMaterials(responder, decision.ValidCardIds),
            includeGrain: HasProvenanceGrainCounterspellMaterials(responder, decision.ValidCardIds));
        AddThought(thought);
        var selected = cardId is { } selectedCardId
            ? GetNullificationCards(responder).Single(card => card.Id == selectedCardId)
            : null;
        if (selected is not null && decision.Choices.FirstOrDefault(candidate => candidate.Cards.Contains(selected.Id) &&
                candidate.Parameters.GetValueOrDefault("response") == "extended-view-as") is { } extendedChoice &&
            !decision.Choices.Any(candidate => candidate.Cards is [var singleId] && singleId == selected.Id))
        {
            ResolveExtendedViewAsResponse(extendedChoice);
            return;
        }
        var choice = selected is null ? null : decision.Choices.FirstOrDefault(candidate =>
            candidate.Cards is [var publishedCardId] && publishedCardId == selected.Id &&
            candidate.Parameters.GetValueOrDefault("response") == "nullification") ??
            throw new InvalidOperationException("The AI Nullification response has no published source choice.");
        var selectedConversionSource = choice is null ? null : ReadPublishedNullificationSource(choice);
        if (selected is not null) ValidateNullificationConversion(responder, selected, selectedConversionSource);
        ClearPendingDecision();
        ResolveNullificationChoice(pending, responder, selected, selectedConversionSource);
        AdvanceRulesAndPublishState();
    }

    private void ResolvePendingAiHarvest()
    {
        var group = ActiveGroupCard ??
            throw new InvalidOperationException("AI FiveGrains selection has no pending draft.");
        if (group.Effect != GroupCardEffect.PublicDraft ||
            _pendingDecision?.Kind != DecisionKind.SelectHarvestCard)
        {
            throw new InvalidOperationException("The pending AI decision is not a FiveGrains selection.");
        }

        var picker = _players[group.TargetSeats[group.TargetIndex]];
        if (picker.IsHuman)
        {
            throw new InvalidOperationException("A human FiveGrains picker cannot be resolved as AI.");
        }

        var processing = _cardZones.CardsAt(CardLocation.Processing);
        var options = group.RevealedCardIds
            .Select(cardId => processing.Single(card => card.Id == cardId))
            .Select(ToSnapshot)
            .ToArray();
        var view = CreateSnapshot(picker.Seat);
        var (cardId, thought) = _aiBrains[picker.Seat].ChooseHarvestCard(
            view,
            options,
            ++_thoughtSequence);
        AddThought(thought);
        ClearPendingDecision();
        ResolveHarvestSelection(group, picker, cardId);
        AdvanceRulesAndPublishState();
    }

    private void ResolvePendingAiTargetCardSelection()
    {
        var pending = ActiveTargetCardSelection ??
            throw new InvalidOperationException("AI hidden target-card selection has no pending resolution.");
        if (_pendingDecision is not { Kind: DecisionKind.SelectTargetCard } decision ||
            decision.PlayerSeat != pending.SourceSeat ||
            _players[pending.SourceSeat].IsHuman)
        {
            throw new InvalidOperationException("The pending AI hidden target-card choice is inconsistent.");
        }

        var sourceView = CreateSnapshot(pending.SourceSeat);
        var (choiceId, thought) = _aiBrains[pending.SourceSeat].ChooseTargetCardSlot(
            sourceView,
            pending.TargetSeat,
            pending.ActionKind,
            decision.Choices,
            ++_thoughtSequence);
        AddThought(thought);
        var choice = decision.Choices.SingleOrDefault(candidate => candidate.Id == choiceId) ??
            throw new InvalidOperationException("The AI selected a hidden target-card choice that was not published.");
        if (!choice.Parameters.TryGetValue("slot-index", out var slotText) ||
            !int.TryParse(
                slotText,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var slot))
        {
            throw new InvalidOperationException("The AI selected a malformed hidden target-card slot.");
        }

        ClearPendingDecision();
        ResolveTargetCardSelection(pending, slot);
        AdvanceRulesAndPublishState();
    }

    private void ResolvePendingAiFireAttack()
    {
        var pending = ActiveFireAttack ??
            throw new InvalidOperationException("AI FireAttack selection has no pending resolution.");
        var decision = _pendingDecision ??
            throw new InvalidOperationException("AI FireAttack selection has no pending prompt.");
        if (decision.Kind is not (DecisionKind.FireAttackReveal or DecisionKind.FireAttackDiscard) ||
            decision.PlayerSeat == _options.HumanSeat)
        {
            throw new InvalidOperationException("The pending AI decision is not a FireAttack selection.");
        }

        var actor = _players[decision.PlayerSeat];
        if (actor.IsHuman)
        {
            throw new InvalidOperationException("A human FireAttack selection cannot be resolved as AI.");
        }

        if (TryResolveAiColorFireAttack(pending, decision)) return;
        var view = CreateSnapshot(actor.Seat);
        if (decision.Kind == DecisionKind.FireAttackReveal)
        {
            var (cardId, thought) = _aiBrains[actor.Seat].ChooseFireAttackReveal(
                view,
                decision.ValidCardIds,
                pending.SourceSeat,
                ++_thoughtSequence);
            AddThought(thought);
            ClearPendingDecision();
            ResolveFireAttackReveal(pending, cardId);
        }
        else
        {
            var revealed = GetFireAttackRevealedCard(pending);
            var (cardId, thought) = _aiBrains[actor.Seat].ChooseFireAttackDiscard(
                view,
                GetFireAttackTargetSeat(pending),
                revealed.Suit,
                decision.ValidCardIds,
                ++_thoughtSequence);
            AddThought(thought);
            ClearPendingDecision();
            ResolveFireAttackDiscard(pending, cardId);
        }

        AdvanceRulesAndPublishState();
    }

    private void ResolvePendingAiDuel()
    {
        var duel = ActiveDuel ??
            throw new InvalidOperationException("AI Duel continuation has no pending Duel.");
        var responder = _players[duel.ResponderSeat];
        if (_pendingDecision?.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("response") == "faction-slash-request") == true)
        {
            BeginFactionSlashResponseRequest(duel.Attack);
            AdvanceRulesAndPublishState();
            return;
        }
        var slashes = GetResponseCards(responder, CardKind.Slash);
        var zhangbaPair = GetZhangbaSlashPairs(responder).FirstOrDefault();
        var programSelection = GetProgramMultiCardViewAsSelections(
            responder, CardKind.Slash, forResponse: true).FirstOrDefault();
        var responseDecision = _pendingDecision ??
            throw new InvalidOperationException("The AI Duel response has no published choice.");
        PopResponseWindow(duel.ResolutionId);
        SetResponseParentStep(duel.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        _pendingDecision = null;

        if (slashes.Count == 0 && zhangbaPair is null && programSelection is null)
        {
            ResolveDuelResponse(duel, responder, selectedSlash: null);
        }
        else
        {
            var view = CreateSnapshot(responder.Seat);
            var (useSlash, thought) = _aiBrains[responder.Seat].ChooseDuelResponse(
                view,
                duel.OpponentSeat,
                ++_thoughtSequence);
            AddThought(thought);
            if (useSlash && slashes.Count > 0)
            {
                CaptureAiCardResponseChoice(responseDecision, slashes[0],
                    GetEffectiveResponseKind(responder, slashes[0], CardKind.Slash));
                ResolveDuelResponse(duel, responder, slashes[0]);
            }
            else if (useSlash && zhangbaPair is not null)
            {
                ResolveDuelZhangbaResponse(duel, responder, zhangbaPair);
            }
            else if (useSlash && programSelection is not null)
            {
                ResolveDuelProgramMultiCardResponse(duel, responder, programSelection);
            }
            else
            {
                ResolveDuelResponse(duel, responder, selectedSlash: null);
            }
        }

        AdvanceRulesAndPublishState();
    }

    private void ResolvePendingAiGroupAttack()
    {
        var group = ActiveGroupCard ??
            throw new InvalidOperationException("AI group response has no pending group attack.");
        var attack = group.CurrentAttack ??
            throw new InvalidOperationException("AI group response has no current target.");
        var responder = _players[attack.TargetSeat];
        if (_pendingDecision?.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("response") == "faction-slash-request") == true)
        {
            BeginFactionSlashResponseRequest(attack);
            AdvanceRulesAndPublishState();
            return;
        }
        if (_pendingDecision?.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("response") == "faction-defense-request") == true)
        {
            BeginFactionDefenseRequest(attack);
            AdvanceRulesAndPublishState();
            return;
        }

        var requiredCardKind = group.RequiredCardKind ??
            throw new InvalidOperationException("A group response attack must declare a required card kind.");
        var responseCards = GetResponseCards(responder, requiredCardKind);
        var zhangbaPair = requiredCardKind == CardKind.Slash
            ? GetZhangbaSlashPairs(responder).FirstOrDefault()
            : null;
        var programSelection = requiredCardKind == CardKind.Slash
            ? GetProgramMultiCardViewAsSelections(
                responder, CardKind.Slash, forResponse: true).FirstOrDefault()
            : null;
        var hasBagua = requiredCardKind == CardKind.Dodge &&
                       !attack.IgnoresArmor &&
                       HasBagua(responder);
        var responseDecision = _pendingDecision ??
            throw new InvalidOperationException("The AI group response has no published choice.");
        PopResponseWindow(group.ResolutionId);
        SetCardUseStep(group.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        _pendingDecision = null;

        if (responseCards.Count == 0 && zhangbaPair is null && programSelection is null && !hasBagua)
        {
            ResolveGroupResponse(group, responder, selectedResponse: null);
        }
        else if (requiredCardKind == CardKind.Dodge && hasBagua)
        {
            var view = CreateSnapshot(responder.Seat);
            var (useDodge, useBagua, thought) = _aiBrains[responder.Seat]
                .ChooseDodgeResponse(
                    view,
                    group.SourceSeat,
                    ++_thoughtSequence,
                    attack.IgnoresArmor,
                    legalDodgeAvailable: responseCards.Count > 0);
            AddThought(thought);
            if (useDodge)
            {
                var card = responseCards[0];
                if (TryResolveExtendedAiResponse(responder, card, requiredCardKind)) { AdvanceRulesAndPublishState(); return; }
                CaptureAiCardResponseChoice(responseDecision, card,
                    GetEffectiveResponseKind(responder, card, requiredCardKind));
                ResolveGroupResponse(group, responder, card);
            }
            else if (useBagua)
            {
                var judgmentResult = ResolveBaguaJudgment(attack, responder);
                if (judgmentResult is { } succeeded)
                {
                    CompleteBaguaResponse(attack, succeeded);
                }
            }
            else
            {
                ResolveGroupResponse(group, responder, selectedResponse: null);
            }
        }
        else
        {
            var view = CreateSnapshot(responder.Seat);
            var (useResponse, thought) = _aiBrains[responder.Seat].ChooseGroupResponse(
                view,
                group.SourceSeat,
                group.Card.Kind,
                requiredCardKind,
                ++_thoughtSequence);
            AddThought(thought);
            if (useResponse && responseCards.Count > 0)
            {
                CaptureAiCardResponseChoice(responseDecision, responseCards[0],
                    GetEffectiveResponseKind(responder, responseCards[0], requiredCardKind));
                ResolveGroupResponse(group, responder, responseCards[0]);
            }
            else if (useResponse && zhangbaPair is not null)
            {
                ResolveGroupZhangbaResponse(group, responder, zhangbaPair);
            }
            else if (useResponse && programSelection is not null)
            {
                ResolveGroupProgramMultiCardResponse(group, responder, programSelection);
            }
            else
            {
                ResolveGroupResponse(group, responder, selectedResponse: null);
            }
        }

        AdvanceRulesAndPublishState();
    }

    private void ResolvePendingAiDodge()
    {
        if (TryResolveTieredRoundZeroAiDodge()) return;
        var attack = ActiveCardAttack ??
            throw new InvalidOperationException("AI Dodge continuation has no pending Slash.");
        var source = _players[attack.SourceSeat];
        var target = _players[attack.TargetSeat];
        if (_pendingDecision?.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("response") == "faction-defense-request") == true)
        {
            BeginFactionDefenseRequest(attack);
            AdvanceRulesAndPublishState();
            return;
        }

        var responseDecision = _pendingDecision ??
            throw new InvalidOperationException("The AI Dodge response has no published choice.");
        PopResponseWindow(attack.ResolutionId);
        SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        _pendingDecision = null;
        var dodges = GetResponseCards(target, CardKind.Dodge);
        if (ConvertedSlashSameColorResponseColor(attack) is { } requireRed)
            dodges = dodges.Where(card => IsRedSuit(EffectiveSuit(target, card)) == requireRed).ToList();
        var dodge = dodges.FirstOrDefault();
        var hasBagua = !attack.IgnoresArmor && HasBagua(target);

        if (!target.IsAlive || (dodge is null && !hasBagua))
        {
            if (!ApplyAttackDamage(attack))
            {
                CompleteAttack(attack);
            }
        }
        else
        {
            var view = CreateSnapshot(target.Seat);
            var (useDodge, useBagua, thought) = _aiBrains[target.Seat]
                .ChooseDodgeResponse(
                    view,
                    source.Seat,
                    ++_thoughtSequence,
                    attack.IgnoresArmor,
                    legalDodgeAvailable: dodge is not null);
            AddThought(thought);
            if (useDodge)
            {
                if (TryResolveExtendedAiResponse(target, dodge!, CardKind.Dodge)) { AdvanceRulesAndPublishState(); return; }
                CaptureAiCardResponseChoice(responseDecision, dodge!,
                    GetEffectiveResponseKind(target, dodge!, CardKind.Dodge));
                ResolveDodgeResponse(attack, target, dodge!);
            }
            else if (useBagua)
            {
                var judgmentResult = ResolveBaguaJudgment(attack, target);
                if (judgmentResult is { } succeeded)
                {
                    CompleteBaguaResponse(attack, succeeded);
                }
            }
            else
            {
                if (!ApplyAttackDamage(attack))
                {
                    CompleteAttack(attack);
                }
            }
        }

        AdvanceRulesAndPublishState();
    }

    private bool? ResolveBaguaJudgment(
        CardAttackHandle attack,
        CharacterState defender)
    {
        if (attack.IgnoresArmor)
        {
            throw new InvalidOperationException("The attacking weapon ignores armor, so Bagua cannot respond.");
        }

        if (!HasBagua(defender))
        {
            throw new InvalidOperationException("Bagua judgment requires Bagua equipment.");
        }

        return BeginJudgment(
            attack,
            defender.Seat,
            JudgmentReasons.BaguaDefense,
            attack.ResolutionId,
            attack.EffectiveCardKind,
            JudgmentContinuationKind.Bagua);
    }

    private void CompleteBaguaResponse(CardAttackHandle attack, bool succeeded)
    {
        if (ActiveGroupCard is { Effect: GroupCardEffect.ResponseAttack } group &&
            SameAttackOwner(group.CurrentAttack, attack))
        {
            var requiredCardKind = group.RequiredCardKind ??
                throw new InvalidOperationException(
                    "A group response attack must declare a required card kind.");
            AdvanceEventRulesAndQueueFact(new GroupResponseEvent(
                group.ResolutionId,
                group.Card.Kind,
                requiredCardKind,
                attack.TargetSeat,
                UsedResponse: succeeded,
                ResponseCardId: null,
                ResponseCardKind: succeeded ? CardKind.Dodge : null));
        }

        if (succeeded)
        {
            var continuation = ActiveGroupCard is { Effect: GroupCardEffect.ResponseAttack } responseGroup &&
                               SameAttackOwner(responseGroup.CurrentAttack, attack)
                ? ProgramCardContinuation.GroupResponse
                : ProgramCardContinuation.Dodge;
            if (TryBeginCardResponsePrograms(
                    attack,
                    _players[attack.TargetSeat],
                    _players[attack.TargetSeat],
                    requesterSeat: null,
                    attack.SourceSeat,
                    CardKind.Dodge,
                    [],
                    continuation))
            {
                return;
            }
            CompleteSuccessfulDodgeResponse(attack);
        }
        else if (!ApplyAttackDamage(attack))
        {
            CompleteAttack(attack);
        }
    }

    private bool? BeginJudgment(
        CardAttackHandle? attack,
        int targetSeat,
        string reason,
        long parentFrameId,
        CardKind? sourceCard,
        JudgmentContinuationKind continuation,
        Card? delayedCard = null,
        int? sourceSeat = null,
        string? programResultBind = null,
        SkillProgramCardSetVisibility? programResultVisibility = null)
    {
        if (ActiveJudgment is not null)
        {
            throw new InvalidOperationException("The engine cannot resolve two judgments at once.");
        }

        var frameId = ++_resolutionSequence;
        var frame = new JudgmentFrame(
            frameId,
            parentFrameId,
            targetSeat,
            reason,
            CardId: null,
            CardKind: null,
            Suit: null,
            Succeeded: null)
        {
            SourceSeat = sourceSeat ?? attack?.SourceSeat ?? targetSeat,
            Continuation = continuation,
            ParentAttackId = attack?.ResolutionId,
            DelayedCardId = delayedCard?.Id,
            ProgramResultBind = programResultBind,
            ProgramResultVisibility = programResultVisibility
        };
        PushRuntimeFrame(frame);
        AdvanceEventRulesAndQueueFact(new JudgmentRequestedEvent(
            frameId,
            parentFrameId,
            targetSeat,
            reason,
            sourceCard));

        if (!EnsureDrawPile())
        {
            frame = frame with
            {
                Succeeded = false,
                Step = ResolutionFrameStep.Completed
            };
            ReplaceJudgmentFrame(frame);
            AdvanceEventRulesAndQueueFact(new JudgmentResolvedEvent(
                frameId,
                parentFrameId,
                targetSeat,
                reason,
                CardId: null,
                CardKind: null,
                Suit: null,
                Rank: null,
                Succeeded: false));
            AddLog(
                "Judgment",
                $"{_players[targetSeat].Name} 的判定因牌堆耗尽而失败。",
                targetSeat,
                frame.SourceSeat);
            PopResolutionFrame(frameId, ResolutionFrameKind.Judgment);
            if (IsDelayedJudgmentContinuation(continuation) ||
                continuation == JudgmentContinuationKind.ProgramSkill)
            {
                ResumeCompletedJudgment(new JudgmentCompletionReceipt(frame, Succeeded: false));
            }

            return false;
        }

        var judgmentCard = _cardZones.CardsAt(CardLocation.DrawPile)[^1];
        frame = frame with
        {
            CardId = judgmentCard.Id,
            CardKind = judgmentCard.Kind,
            Suit = judgmentCard.Suit
        };
        ReplaceJudgmentFrame(frame);
        MoveCard(
            judgmentCard,
            CardLocation.DrawPile,
            CardLocation.Judgment(targetSeat),
            CardMoveReasons.JudgmentReveal);

        var candidates = CollectJudgmentReplacementCandidates(
            judgmentCard,
            targetSeat,
            reason);
        var orderedCandidates = candidates.ToArray();
        frame = frame with
        {
            ReplacementCandidates = Array.AsReadOnly(orderedCandidates),
            ReplacementCandidateIndex = 0,
            Step = orderedCandidates.Length == 0
                ? ResolutionFrameStep.ResolvingEffect
                : ResolutionFrameStep.AwaitingResponse
        };
        ReplaceJudgmentFrame(frame);

        var pending = frame;
        if (orderedCandidates.Length == 0)
        {
            var succeeded = FinalizeJudgment(pending);
            if (succeeded is { } completed &&
                (IsDelayedJudgmentContinuation(continuation) ||
                 continuation == JudgmentContinuationKind.ProgramSkill))
            {
                ResumeCompletedJudgment(new JudgmentCompletionReceipt(pending, completed));
            }

            return succeeded;
        }

        BeginJudgmentReplacementChoice(pending);
        return null;
    }

    private IReadOnlyList<JudgmentTriggerCandidate> CollectJudgmentReplacementCandidates(
        Card judgmentCard,
        int targetSeat,
        string reason)
    {
        var candidates = new List<JudgmentTriggerCandidate>();
        foreach (var owner in _players.Where(player => player.IsAlive))
        {
            foreach (var binding in EnabledUniqueProgramTriggers(
                         owner, SkillProgramTriggerWindow.JudgmentReplacing)
                     .OrderBy(item => item.SkillId, StringComparer.Ordinal)
                     .ThenBy(item => item.Trigger.Id, StringComparer.Ordinal))
            {
                var program = binding.Program;
                var trigger = binding.Trigger;
                if (!MatchesProgramJudgmentReplacement(owner, trigger, targetSeat, reason) ||
                    GetProgramJudgmentReplacementCards(owner, trigger, targetSeat, reason).Count == 0)
                {
                    continue;
                }

                candidates.Add(new JudgmentTriggerCandidate(
                    owner.Seat,
                    $"program:{program.Id}:{trigger.Id}",
                    Priority: 0,
                    ProgramId: program.Id,
                    ProgramTriggerId: trigger.Id,
                    SkillInstanceId: binding.SkillInstanceId,
                    GameplayHash: program.GameplayHash));
            }
        }

        var orderingSeat = (_currentSeat);
        return JudgmentTriggerOrdering.Order(candidates, orderingSeat, _playerCount);
    }

    private void BeginJudgmentReplacementChoice(JudgmentFrame pending)
    {
        pending = GetJudgmentFrame(pending.Id);
        if (ActiveJudgment?.Id != pending.Id ||
            GetJudgmentCard(pending) is not { } judgmentCard)
        {
            throw new InvalidOperationException("The replacement judgment is not the current resolution.");
        }

        var frame = GetJudgmentFrame(pending.Id);
        if (frame.ReplacementCandidateIndex >= frame.ReplacementCandidates.Count)
        {
            var succeeded = FinalizeJudgment(pending);
            if (succeeded is { } completed)
            {
                ResumeCompletedJudgment(new JudgmentCompletionReceipt(pending, completed));
            }
            return;
        }

        var candidate = CurrentJudgmentCandidate(pending) ??
            throw new InvalidOperationException("The judgment replacement candidate cursor is invalid.");
        BeginProgramJudgmentReplacementChoice(pending, candidate);
    }

    private bool IsProtectedJudgmentSourceEquipment(
        CharacterState owner,
        Card card,
        int judgmentSubjectSeat,
        string judgmentReason) =>
        owner.Seat == judgmentSubjectSeat &&
        string.Equals(judgmentReason, JudgmentReasons.BaguaDefense, StringComparison.Ordinal) &&
        card.Kind == CardKind.BaguaFormation &&
        _cardZones.GetLocation(card.Id) == CardLocation.Equipment(owner.Seat);

    private void AdvanceJudgmentCandidate(JudgmentFrame pending)
    {
        if (ActiveJudgment?.Id != pending.Id)
        {
            throw new InvalidOperationException("The replacement judgment is not the current resolution.");
        }

        var frame = GetJudgmentFrame(pending.Id);
        var nextIndex = checked(frame.ReplacementCandidateIndex + 1);
        if (nextIndex > frame.ReplacementCandidates.Count)
            throw new InvalidOperationException("The judgment replacement cursor advanced past its candidates.");
        ReplaceJudgmentFrame(frame with
        {
            ReplacementCandidateIndex = nextIndex,
            Step = nextIndex == frame.ReplacementCandidates.Count
                ? ResolutionFrameStep.ResolvingEffect
                : ResolutionFrameStep.AwaitingResponse
        });
        if (nextIndex == frame.ReplacementCandidates.Count)
        {
            pending = GetJudgmentFrame(pending.Id);
            var succeeded = FinalizeJudgment(pending);
            if (succeeded is { } completed)
            {
                ResumeCompletedJudgment(new JudgmentCompletionReceipt(pending, completed));
            }
            return;
        }

        BeginJudgmentReplacementChoice(pending);
    }

    private bool? FinalizeJudgment(JudgmentFrame pending)
    {
        pending = GetJudgmentFrame(pending.Id);
        if (GetJudgmentCard(pending) is not { } judgmentCard)
        {
            throw new InvalidOperationException("A judgment cannot resolve without a card.");
        }

        var frame = GetJudgmentFrame(pending.Id);
        var judgmentSuit = EffectiveSuit(_players[pending.TargetSeat], judgmentCard);
        CaptureProvenanceJudgmentOrigin(pending, judgmentCard, judgmentSuit);
        CaptureProgramSlashSuitJudgment(pending, judgmentSuit);
        CaptureDamageJudgmentSuitResult(pending, judgmentCard, judgmentSuit);
        CaptureProgramRepeatedJudgmentClaimOutcome(pending, judgmentCard, judgmentSuit);
        var succeeded = pending.Continuation switch
        {
            JudgmentContinuationKind.Lightning =>
                judgmentSuit == Suit.Spade && judgmentCard.Rank is >= 2 and <= 9,
            JudgmentContinuationKind.Indulgence => judgmentSuit == Suit.Heart,
            JudgmentContinuationKind.SupplyShortage => judgmentSuit == Suit.Club,
            _ => IsRedSuit(judgmentSuit)
        };
        ReplaceJudgmentFrame(frame with
        {
            CardId = judgmentCard.Id,
            CardKind = judgmentCard.Kind,
            Suit = judgmentSuit,
            Succeeded = succeeded,
            Step = ResolutionFrameStep.Completed
        });
        AdvanceEventRulesAndQueueFact(new JudgmentResolvedEvent(
            pending.Id,
            frame.ParentFrameId,
            pending.TargetSeat,
            pending.Reason,
            judgmentCard.Id,
            judgmentCard.Kind,
            judgmentSuit,
            judgmentCard.Rank,
            succeeded));
        var abilityName = pending.Continuation switch
        {
            JudgmentContinuationKind.Bagua or JudgmentContinuationKind.FactionDefenseBagua => "八卦阵",
            JudgmentContinuationKind.Indulgence => "乐不思蜀",
            JudgmentContinuationKind.SupplyShortage => "兵粮寸断",
            JudgmentContinuationKind.Lightning => "闪电",
            JudgmentContinuationKind.ProgramSkill when
                _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(owner => owner.Id == pending.ParentFrameId) is
                    { SlashSuitDiscard: { Stage: ProgramSlashSuitDiscardStage.Judging } draft } namedOwner &&
                draft.JudgmentFrameId == pending.Id && draft.CardUseFrameId == pending.ParentAttackId &&
                _contentRegistry.Skills.TryGetValue(namedOwner.SkillId, out var definition) => definition.Name,
            _ => pending.Reason
        };
        var judgmentResult = pending.Continuation == JudgmentContinuationKind.Lightning
            ? succeeded ? "命中" : "未命中"
            : succeeded ? "成功" : "失败";
        AddLog(
            "Judgment",
            $"{_players[pending.TargetSeat].Name} 发动【{abilityName}】判定：最终为【{judgmentCard.DisplayName}】（{judgmentCard.Suit} {judgmentCard.RankText}），判定{judgmentResult}。",
            pending.TargetSeat,
            pending.SourceSeat);
        if (TryBeginProgramJudgmentWindow(pending, judgmentCard, judgmentSuit, succeeded))
        {
            return null;
        }

        return CompleteFinalizedJudgment(pending, judgmentCard, succeeded);
    }

    private bool? CompleteFinalizedJudgment(
        JudgmentFrame pending,
        Card judgmentCard,
        bool succeeded)
    {
        if (pending.Continuation == JudgmentContinuationKind.ProgramSkill)
        {
            FinishProgramSkillJudgment(pending, judgmentCard);
            return succeeded;
        }

        FinishResolvedJudgment(pending);
        return succeeded;
    }

    private void FinishResolvedJudgment(JudgmentFrame pending)
    {
        var judgmentCard = GetJudgmentCard(pending) ??
            throw new InvalidOperationException("A resolved judgment must retain its card until disposition.");
        if (_cardZones.GetLocation(judgmentCard.Id) == CardLocation.Judgment(pending.TargetSeat))
        {
            MoveCard(
                judgmentCard,
                CardLocation.Judgment(pending.TargetSeat),
                CardLocation.DiscardPile,
                CardMoveReasons.JudgmentFinish);
        }

        SetJudgmentFrameStep(pending.Id, ResolutionFrameStep.Completed);
        if (pending.Continuation != JudgmentContinuationKind.Lightning || GetJudgmentFrame(pending.Id).Succeeded != true)
            PopResolutionFrame(pending.Id, ResolutionFrameKind.Judgment);
    }

    private void FinishProgramSkillJudgment(JudgmentFrame pending, Card judgmentCard)
    {
        SetJudgmentFrameStep(pending.Id, ResolutionFrameStep.Completed);
        PopResolutionFrame(pending.Id, ResolutionFrameKind.Judgment);
        var location = _cardZones.GetLocation(judgmentCard.Id);
        if (location == CardLocation.Judgment(pending.TargetSeat) &&
            GetActiveProgramFrame(pending.ParentFrameId).RepeatedJudgment is null)
        {
            MoveCard(
                judgmentCard,
                location,
                CardLocation.Processing,
                new CardMoveReason("skill-program.judgment.result"));
        }
    }

    private void CompleteDelayedJudgment(
        JudgmentFrame pending,
        bool succeeded)
    {
        var delayedCard = GetDelayedJudgmentCard(pending) ??
            throw new InvalidOperationException("A delayed judgment has no delayed card.");
        var target = _players[pending.TargetSeat];
        var delayedLocation = _cardZones.GetLocation(delayedCard.Id);
        if (delayedLocation != CardLocation.Judgment(target.Seat))
        {
            throw new InvalidOperationException(
                "The resolved delayed card is not in its target's judgment zone.");
        }

        var effectiveCardKind = GetJudgmentEffectiveCardKind(delayedCard);
        var delayedEffectSucceeded = !succeeded;
        var delayedEffects = delayedEffectSucceeded
            ? GetDelayedTurnEffects(effectiveCardKind)
            : DelayedTurnEffects.None;
        _pendingTurnDelayedEffects |= delayedEffects;
        MoveCard(
            delayedCard,
            CardLocation.Judgment(target.Seat),
            CardLocation.DiscardPile,
            CardMoveReasons.DelayedCardFinish);
        AdvanceEventRulesAndQueueFact(new DelayedCardResolvedEvent(
            pending.Id,
            delayedCard.Id,
            effectiveCardKind,
            target.Seat,
            GetJudgmentCard(pending)?.Id,
            succeeded,
            SkippedPlayPhase: delayedEffects.HasFlag(DelayedTurnEffects.SkipPlayPhase))
        {
            SkippedDrawPhase = delayedEffects.HasFlag(DelayedTurnEffects.SkipDrawPhase)
        });
        var phaseDescription = delayedEffects.HasFlag(DelayedTurnEffects.SkipDrawPhase) &&
                               delayedEffects.HasFlag(DelayedTurnEffects.SkipPlayPhase)
            ? "跳过摸牌和出牌阶段"
            : delayedEffects.HasFlag(DelayedTurnEffects.SkipDrawPhase)
                ? "跳过摸牌阶段"
                : delayedEffects.HasFlag(DelayedTurnEffects.SkipPlayPhase)
                    ? "跳过出牌阶段"
                    : "正常进入后续阶段";
        AddLog(
            "DelayedCardResolved",
            $"{target.Name} 的【{CardCatalog.Get(effectiveCardKind).DisplayName}】判定为{GetSuitDisplayName(GetJudgmentCard(pending)?.Suit)}，{phaseDescription}。",
            target.Seat);
        BeginDelayedJudgmentOrTurnStart(target);
    }

    private static string GetSuitDisplayName(Suit? suit) => suit switch
    {
        Suit.Spade => "黑桃",
        Suit.Heart => "红桃",
        Suit.Club => "梅花",
        Suit.Diamond => "方片",
        _ => "未知花色"
    };

    private void CompleteLightningJudgment(
        JudgmentFrame pending,
        bool hit)
    {
        var lightning = GetDelayedJudgmentCard(pending) ??
            throw new InvalidOperationException("A Lightning judgment has no delayed card.");
        var target = _players[pending.TargetSeat];
        var lightningLocation = _cardZones.GetLocation(lightning.Id);
        if (lightningLocation != CardLocation.Judgment(target.Seat))
        {
            throw new InvalidOperationException(
                "The resolved Lightning is not in its target's judgment zone.");
        }

        if (!hit)
        {
            var nextTargetSeat = Enumerable.Range(1, _playerCount - 1)
                .Select(offset => (target.Seat + offset) % _playerCount)
                .FirstOrDefault(seat => _players[seat].IsAlive &&
                    !_players[seat].JudgmentAreaAbolished &&
                    !HasCardPolicy(_players[seat], SkillProgramCardPolicyKind.ProhibitDelayedTrickTarget) &&
                    // A transfer carries the held delayed card's actual color,
                    // evaluated for its current holder, never the judgment card.
                    !IsBlackTrickTargetProhibited(_players[seat], CardKind.Lightning, SuitColor(EffectiveSuit(target, lightning))) &&
                    !HasJudgmentEffectiveCard(_players[seat], CardKind.Lightning), target.Seat);
            if (nextTargetSeat != target.Seat)
            {
                MoveCard(
                    lightning,
                    CardLocation.Judgment(target.Seat),
                    CardLocation.Judgment(nextTargetSeat),
                    CardMoveReasons.DelayedCardTransfer);
            }

            AdvanceEventRulesAndQueueFact(new LightningResolvedEvent(
                pending.Id,
                lightning.Id,
                target.Seat,
                GetJudgmentCard(pending)?.Id,
                Hit: false,
                NextTargetSeat: nextTargetSeat,
                DamageAmount: 0));
            AddLog(
                "LightningResolved",
                nextTargetSeat == target.Seat
                    ? $"{target.Name} 的【闪电】未命中，因仅剩自己存活而留在其判定区。"
                    : $"{target.Name} 的【闪电】未命中，移至 {_players[nextTargetSeat].Name} 的判定区。",
                target.Seat,
                nextTargetSeat);
            BeginDelayedJudgmentOrTurnStart(
                target,
                excludedCardId: nextTargetSeat == target.Seat ? lightning.Id : null);
            return;
        }

        const int lightningDamage = 3;
        AdvanceEventRulesAndQueueFact(new LightningResolvedEvent(
            pending.Id,
            lightning.Id,
            target.Seat,
            GetJudgmentCard(pending)?.Id,
            Hit: true,
            NextTargetSeat: null,
            DamageAmount: lightningDamage));
        AddLog(
            "LightningResolved",
            $"{target.Name} 的【闪电】命中，受到 {lightningDamage} 点雷电伤害。",
            target.Seat);

        var attack = new CardAttackHandle(this,
            pending.Id,
            target.Seat,
            target.Seat,
            lightning,
            damageAmount: lightningDamage,
            playedCardKind: CardKind.Lightning,
            isDelayedJudgmentDamage: true,
            delayedJudgmentSeat: target.Seat);
        ActiveCardAttack = attack;
        if (!ApplyAttackDamage(attack))
        {
            CompleteAttack(attack);
        }
    }

    private void ResumeAfterLightningDamage(int delayedSeat)
    {
        var current = _players[delayedSeat];
        if (!current.IsAlive)
        {
            EndTurn();
            return;
        }

        BeginDelayedJudgmentOrTurnStart(current);
    }

    private void ResumeCompletedJudgment(JudgmentCompletionReceipt receipt)
    {
        var pending = receipt.Frame;
        var succeeded = receipt.Succeeded;
        if (pending.Continuation == JudgmentContinuationKind.ProgramSkill)
        {
            if (TryReturnDamageJudgmentSuitPayment(pending)) return;
            if (TryReturnProgramSlashSuitJudgment(pending)) return;
            if (GetActiveProgramFrame(pending.ParentFrameId).RepeatedJudgment is not null)
            {
                ResumeProgramRepeatedJudgment(pending);
                return;
            }
            var bind = pending.ProgramResultBind ??
                throw new InvalidOperationException("A program judgment lost its result binding.");
            var visibility = pending.ProgramResultVisibility ??
                throw new InvalidOperationException("A program judgment lost its result visibility.");
            var ownedResultClaimed = IsOwnedDamagePointJudgmentResultClaimed(pending);
            var cardIds = !ownedResultClaimed && GetJudgmentCard(pending) is { } resultCard
                ? new[] { resultCard.Id }
                : Array.Empty<int>();
            var locations = !ownedResultClaimed && GetJudgmentCard(pending) is { } boundCard
                ? new[] { _cardZones.GetLocation(boundCard.Id) }
                : Array.Empty<CardLocation>();
            SetProgramCardSet(pending.ParentFrameId, bind, cardIds, visibility, locations,
                GetJudgmentCard(pending) is { } finalCard
                    ? EffectiveSuit(_players[pending.TargetSeat], finalCard) : null);
            AdvanceRuntimeProgram(pending.ParentFrameId);
            return;
        }


        if (pending.Continuation == JudgmentContinuationKind.Bagua)
        {
            var attack = GetJudgmentAttack(pending) ??
                throw new InvalidOperationException("A Bagua judgment has no attack continuation.");
            CompleteBaguaResponse(attack, succeeded);
            return;
        }

        if (pending.Continuation == JudgmentContinuationKind.FactionDefenseBagua)
        {
            var attack = GetJudgmentAttack(pending) ??
                throw new InvalidOperationException("A FactionDefense Bagua judgment has no attack continuation.");
            CompleteFactionDefenseBaguaResponse(attack, succeeded);
            return;
        }

        if (pending.Continuation == JudgmentContinuationKind.Lightning)
        {
            CompleteLightningJudgment(pending, succeeded);
            return;
        }

        if (IsDelayedJudgmentContinuation(pending.Continuation))
        {
            CompleteDelayedJudgment(pending, succeeded);
            return;
        }

    }

    private JudgmentFrame GetJudgmentFrame(long frameId)
    {
        var frame = _resolutionStack.LastOrDefault(candidate => candidate.Id == frameId) as JudgmentFrame;
        return frame ??
            throw new InvalidOperationException($"Resolution frame {frameId} is not a Judgment frame.");
    }

    private JudgmentTriggerCandidate? CurrentJudgmentCandidate(JudgmentFrame pending)
    {
        var frame = GetJudgmentFrame(pending.Id);
        return frame.ReplacementCandidateIndex < frame.ReplacementCandidates.Count
            ? frame.ReplacementCandidates[frame.ReplacementCandidateIndex]
            : null;
    }

    private void SetJudgmentFrameStep(long frameId, ResolutionFrameStep step)
    {
        var frame = GetJudgmentFrame(frameId);
        ReplaceRuntimeFrame(_resolutionStack[_resolutionStack.FindLastIndex(candidate => candidate.Id == frameId)].Id, frame with { Step = step });
    }

    private void ReplaceJudgmentFrame(JudgmentFrame frame)
    {
        var index = _resolutionStack.FindLastIndex(candidate => candidate.Id == frame.Id);
        if (index < 0 || _resolutionStack[index] is not JudgmentFrame)
        {
            throw new InvalidOperationException($"Resolution frame {frame.Id} is not a Judgment frame.");
        }

        ReplaceRuntimeFrame(_resolutionStack[index].Id, frame);
    }

    private static bool IsRedSuit(Suit suit) =>
        suit is Suit.Heart or Suit.Diamond;

    private static bool IsLightningHit(Card card) =>
        card.Suit == Suit.Spade && card.Rank is >= 2 and <= 9;

    private void ResolveDodgeResponse(
        CardAttackHandle attack,
        CharacterState defender,
        Card responseCard)
    {
        if (TryBeginCardDeclaration(defender, responseCard, CardKind.Dodge,
            PeekDeclarationConversion(defender, responseCard, CardKind.Dodge, false),
            DeclarationReturn(CardDeclarationPurpose.Dodge, defender.Seat), [attack.SourceSeat])) return;
        if (!CanUseCardAsResponse(defender, responseCard, CardKind.Dodge) &&
            GetProgramMultiCardViewAsSelections(defender, CardKind.Dodge, true)
                .FirstOrDefault(selection => selection.Cards[0].Id == responseCard.Id) is { } extended)
        {
            ResolveExtendedViewAsResponse(defender, extended);
            return;
        }
        var responseCardKind = GetEffectiveResponseKind(
            defender,
            responseCard,
            CardKind.Dodge);
        var responseConversion = GetSelectedResponseConversion(defender, responseCard, responseCardKind);
        var responseFrom = FindOwnedCardLocation(defender, responseCard);
        var responseCostIsRed = CapturePhysicalCardColor(defender.Seat,responseCard);
        var completedResponseUseSuit = FreezeCompletedResponseUseSuit(defender, [responseCard], responseCardKind);
        PaySingleCardResponse(responseCard, defender, responseCardKind, responseConversion);
        var incomingName = CardCatalog.Get(RequireAttackCardKind(attack)).DisplayName;
        var responseName = CardCatalog.Get(responseCardKind).DisplayName;
        var responseDescription = PublicDeclarationDescription(responseCard, IsNativeResponseCard(responseCard, CardKind.Dodge)
            ? $"打出【{responseName}】"
            : $"将【{responseCard.DisplayName}】当作【{responseName}】");
        var completesResponse = attack.SuccessfulDodgeResponses + 1 >= attack.RequiredDodgeResponses;
        AddLog(
            "CardResponded",
            completesResponse
                ? IsSlashDodgeCancellationPrevented(attack)
                    ? $"{defender.Name} {responseDescription}，响应了【{incomingName}】，但此杀不能被抵消。"
                    : $"{defender.Name} {responseDescription}，抵消了【{incomingName}】。"
                : $"{defender.Name} {responseDescription}响应【{incomingName}】；【无双】仍要求下一张【闪】。",
            defender.Seat,
            attack.SourceSeat);
        AdvanceEventRulesAndQueueFact(new CardRespondedEvent(
            responseCard.Id,
            defender.Seat,
            attack.SourceSeat,
            responseCardKind));
        var responseCost = new CardActionCost(responseCard.Id, responseCard.Kind, responseFrom,responseCostIsRed);
        if (TryBeginCardResponsePrograms(
                attack,
                defender,
                defender,
                requesterSeat: null,
                attack.SourceSeat,
                responseCardKind,
                [responseCost],
                ProgramCardContinuation.Dodge,
                responseConversion, completedResponseUseSuit))
        {
            return;
        }
        FinishSingleCardResponse(responseCard, responseConversion);
        CompleteSuccessfulDodgeResponse(attack);
    }

    private void ContinueAcceptedCardResponse(
        CardAttackHandle attack,
        CardActionContext action,
        ProgramCardContinuation continuation)
    {
        var processing = _cardZones.CardsAt(CardLocation.Processing);
        foreach (var cost in action.PhysicalCards)
        {
            if (IsProgramAlternativeCost(action, cost.CardId))
            {
                if (processing.Any(card => card.Id == cost.CardId))
                    throw new InvalidOperationException("An alternative response cost must not remain in Processing.");
                continue;
            }
            if (IsExchangedCardClaim(action.ActionId, cost.CardId)) continue;
            var card = processing.Single(item => item.Id == cost.CardId);
            MoveCard(
                card,
                CardLocation.Processing,
                CardLocation.DiscardPile,
                CardMoveReasons.ResponseFinished);
        }

        if (TryBeginCompletedResponseUsePrograms(attack, action, continuation)) return;
        ContinueFinishedCardResponse(attack, action, continuation);
    }

    private void ContinueFinishedCardResponse(CardAttackHandle attack, CardActionContext action,
        ProgramCardContinuation continuation)
    {
        switch (continuation)
        {
            case ProgramCardContinuation.Dodge:
            case ProgramCardContinuation.FactionDefenseDodge:
                CompleteSuccessfulDodgeResponse(attack);
                return;
            case ProgramCardContinuation.DuelSlash:
            case ProgramCardContinuation.FactionSlashDuelSlash:
                {
                    var duel = ActiveDuel ??
                        throw new InvalidOperationException("An accepted Duel response has no Duel continuation.");
                    ContinueDuelAfterSuccessfulSlash(duel, action.ActorSeat);
                    return;
                }
            case ProgramCardContinuation.GroupResponse:
            case ProgramCardContinuation.FactionSlashGroupResponse:
                CompleteAttack(attack);
                return;
            default:
                throw new InvalidOperationException(
                    $"Continuation {continuation} is not a card-response continuation.");
        }
    }

    private int GetIceSwordTargetCardCount(CharacterState target, int actorSeat) =>
        GetHand(target).Count + GetEquipment(target).Count(card =>
            !IsForeignEquipmentDiscardPrevented(actorSeat, card, CardLocation.Equipment(target.Seat), OwnedCardMoveIntent.Discard));

    private bool TryBeginIceSwordChoice(CardAttackHandle attack, int preventedDamageAmount)
    {
        var source = _players[attack.SourceSeat];
        var target = _players[attack.TargetSeat];
        if (!UsesFormalIceSword ||
            attack.IceSwordAttempted ||
            attack.IsChainPropagation ||
            attack.EffectiveCardKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) ||
            !source.IsAlive ||
            !target.IsAlive ||
            !HasWeaponAbility(source, CardKind.IceSword) ||
            GetIceSwordTargetCardCount(target, source.Seat) == 0)
        {
            return false;
        }

        if (ActiveIceSword is not null)
        {
            throw new InvalidOperationException("The engine cannot open two Ice Sword choices at once.");
        }

        attack.MarkIceSwordAttempted();
        ActiveIceSword = new IceSwordHandle(this, attack, preventedDamageAmount);
        SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.AwaitingResponse);
        PublishIceSwordChoice(ActiveIceSword);
        return true;
    }

    private void PublishIceSwordChoice(IceSwordHandle pending)
    {
        if (!SameContinuationOwner(ActiveIceSword, pending) ||
            !SameAttackOwner(ActiveCardAttack, pending.Attack))
        {
            throw new InvalidOperationException("The Ice Sword choice has no current Slash continuation.");
        }

        var attack = pending.Attack;
        var source = _players[attack.SourceSeat];
        var target = _players[attack.TargetSeat];
        var hand = GetHand(target);
        var equipment = GetEquipment(target).Where(card => !IsForeignEquipmentDiscardPrevented(
            source.Seat, card, CardLocation.Equipment(target.Seat), OwnedCardMoveIntent.Discard)).ToArray();
        if (hand.Count + equipment.Length == 0)
        {
            FinishIceSwordPrevention(pending);
            return;
        }

        var choices = new List<PromptChoice>();
        for (var slot = 0; slot < hand.Count; slot++)
        {
            choices.Add(new PromptChoice(
                new ChoiceId($"ice-sword.hand-slot-{slot}.resolution-{attack.ResolutionId}.pick-{pending.DiscardedCardIds.Count + 1}"),
                $"弃置 {target.Name} 的第 {slot + 1} 个暗手牌位。",
                [],
                [target.Seat],
                new Dictionary<string, string>
                {
                    ["action"] = "ice-sword-discard",
                    ["target-zone"] = "hand",
                    ["slot-index"] = slot.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["pick-index"] = (pending.DiscardedCardIds.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                }));
        }
        foreach (var card in equipment)
        {
            choices.Add(new PromptChoice(
                new ChoiceId($"ice-sword.equipment-{card.Id}.resolution-{attack.ResolutionId}.pick-{pending.DiscardedCardIds.Count + 1}"),
                $"弃置 {target.Name} 装备区的【{card.DisplayName}】。",
                [card.Id],
                [target.Seat],
                new Dictionary<string, string>
                {
                    ["action"] = "ice-sword-discard",
                    ["target-zone"] = "equipment",
                    ["pick-index"] = (pending.DiscardedCardIds.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                }));
        }
        if (!pending.Activated)
        {
            choices.Add(new PromptChoice(
                new ChoiceId($"ice-sword.damage.resolution-{attack.ResolutionId}"),
                $"不发动【寒冰剑】，对 {target.Name} 造成 {pending.PreventedDamageAmount} 点伤害。",
                [],
                [],
                new Dictionary<string, string> { ["action"] = "ice-sword-damage" }));
        }

        _pendingDecision = new PendingDecision(
            DecisionKind.IceSword,
            source.Seat,
            pending.Activated
                ? $"【寒冰剑】已防止伤害，请继续弃置 {target.Name} 的第二张牌。"
                : $"你的【{CardCatalog.Get(RequireAttackCardKind(attack)).DisplayName}】将对 {target.Name} 造成 {pending.PreventedDamageAmount} 点伤害，是否发动【寒冰剑】？",
            equipment.Select(card => card.Id).ToArray(),
            [target.Seat],
            SourceSeat: source.Seat,
            IncomingCard: attack.EffectiveCardKind)
        {
            PromptId = source.IsHuman ? CreatePromptId() : default,
            IsPrivate = true,
            TargetSeat = target.Seat,
            Choices = choices
        };
        _status = source.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveIceSwordChoice(PromptChoice selected)
    {
        var pending = ActiveIceSword ??
            throw new InvalidOperationException("There is no Ice Sword choice to resolve.");
        var attack = pending.Attack;
        if (!SameAttackOwner(ActiveCardAttack, attack) ||
            _pendingDecision is not { Kind: DecisionKind.IceSword } decision ||
            decision.PlayerSeat != attack.SourceSeat)
        {
            throw new InvalidOperationException("The Ice Sword choice is not the current Slash continuation.");
        }

        var source = _players[attack.SourceSeat];
        var target = _players[attack.TargetSeat];
        var action = selected.Parameters.GetValueOrDefault("action");
        if (action == "ice-sword-damage")
        {
            if (pending.Activated || selected.Cards.Count != 0 || selected.Targets.Count != 0)
            {
                throw new InvalidOperationException("Ice Sword damage may only be retained before its first discard.");
            }

            ActiveIceSword = null;
            ClearPendingDecision();
            SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect);
            AdvanceEventRulesAndQueueFact(new IceSwordResolvedEvent(
                attack.ResolutionId,
                source.Seat,
                target.Seat,
                Used: false,
                PreventedDamageAmount: 0,
                DiscardedCardIds: []));
            AddLog(
                "EquipmentSkipped",
                $"{source.Name} 未发动【寒冰剑】，{target.Name} 继续受到 {pending.PreventedDamageAmount} 点伤害。",
                source.Seat,
                target.Seat);
            if (!ApplyAttackDamage(attack))
            {
                CompleteAttack(attack);
            }
            return;
        }

        if (action != "ice-sword-discard" ||
            !selected.Targets.SequenceEqual([target.Seat]))
        {
            throw new InvalidOperationException("The Ice Sword discard choice is malformed.");
        }

        Card discarded;
        var targetZone = selected.Parameters.GetValueOrDefault("target-zone");
        if (targetZone == "hand" &&
            selected.Cards.Count == 0 &&
            selected.Parameters.TryGetValue("slot-index", out var slotText) &&
            int.TryParse(
                slotText,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var slot) &&
            slot >= 0 && slot < GetHand(target).Count)
        {
            discarded = GetHand(target)[slot];
        }
        else if (targetZone == "equipment" &&
                 selected.Cards.Count == 1 &&
                 GetEquipment(target).FirstOrDefault(card => card.Id == selected.Cards[0]) is { } equipment)
        {
            discarded = equipment;
        }
        else
        {
            throw new InvalidOperationException("The selected Ice Sword target card is no longer available.");
        }

        if (IsForeignEquipmentDiscardPrevented(source.Seat, discarded,
                FindOwnedCardLocation(target, discarded), OwnedCardMoveIntent.Discard))
        {
            if (GetIceSwordTargetCardCount(target, source.Seat) == 0)
            {
                if (pending.Activated) { ClearPendingDecision(); FinishIceSwordPrevention(pending); }
                else ResolveIceSwordChoice(decision.Choices.Single(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "ice-sword-damage"));
            }
            else
            {
                ClearPendingDecision();
                PublishIceSwordChoice(pending);
            }
            return;
        }

        pending.Activated = true;
        ClearPendingDecision();
        MoveCard(
            discarded,
            FindOwnedCardLocation(target, discarded),
            CardLocation.DiscardPile,
            CardMoveReasons.IceSwordDiscard);
        pending.AddDiscardedCard(discarded.Id);
        AddLog(
            "EquipmentEffect",
            $"{source.Name} 发动【寒冰剑】，弃置 {target.Name} 的【{discarded.DisplayName}】。",
            source.Seat,
            target.Seat);

        if (pending.DiscardedCardIds.Count < 2 && GetIceSwordTargetCardCount(target, source.Seat) > 0)
        {
            PublishIceSwordChoice(pending);
            return;
        }

        FinishIceSwordPrevention(pending);
    }

    private void FinishIceSwordPrevention(IceSwordHandle pending)
    {
        if (!SameContinuationOwner(ActiveIceSword, pending) ||
            !SameAttackOwner(ActiveCardAttack, pending.Attack) ||
            !pending.Activated ||
            pending.DiscardedCardIds.Count is < 1 or > 2)
        {
            throw new InvalidOperationException("The completed Ice Sword prevention is invalid.");
        }

        var attack = pending.Attack;
        var source = _players[attack.SourceSeat];
        var target = _players[attack.TargetSeat];
        ActiveIceSword = null;
        ClearPendingDecision();
        SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        AdvanceEventRulesAndQueueFact(new IceSwordResolvedEvent(
            attack.ResolutionId,
            source.Seat,
            target.Seat,
            Used: true,
            pending.PreventedDamageAmount,
            Array.AsReadOnly(pending.DiscardedCardIds.ToArray())));
        AddLog(
            "DamagePrevented",
            $"{source.Name} 的【寒冰剑】防止了对 {target.Name} 的 {pending.PreventedDamageAmount} 点伤害，并依次弃置其 {pending.DiscardedCardIds.Count} 张牌。",
            source.Seat,
            target.Seat);
        CompleteAttack(attack);
    }

    private IReadOnlyList<Card> GetQilinBowTargetMounts(CharacterState target) =>
        GetEquipment(target)
            .Where(card => EquipmentCatalog.Get(card.Kind).Slot is
                EquipmentSlot.OffensiveHorse or EquipmentSlot.DefensiveHorse)
            .ToArray();

    private bool TryBeginQilinBowChoice(CardAttackHandle attack)
    {
        var source = _players[attack.SourceSeat];
        var target = _players[attack.TargetSeat];
        if (!UsesFormalQilinBow ||
            attack.QilinBowAttempted ||
            attack.IsChainPropagation ||
            attack.EffectiveCardKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) ||
            !source.IsAlive ||
            !target.IsAlive ||
            !HasWeaponAbility(source, CardKind.QilinBow) ||
            GetQilinBowTargetMounts(target).Count == 0)
        {
            return false;
        }

        if (ActiveQilinBow is not null)
        {
            throw new InvalidOperationException("The engine cannot open two Qilin Bow choices at once.");
        }

        attack.MarkQilinBowAttempted();
        ActiveQilinBow = new QilinBowHandle(this, attack);
        SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.AwaitingResponse);
        PublishQilinBowChoice(ActiveQilinBow);
        return true;
    }

    private void PublishQilinBowChoice(QilinBowHandle pending)
    {
        if (!SameContinuationOwner(ActiveQilinBow, pending) ||
            !SameAttackOwner(ActiveCardAttack, pending.Attack))
        {
            throw new InvalidOperationException("The Qilin Bow choice has no current Slash continuation.");
        }

        var attack = pending.Attack;
        var source = _players[attack.SourceSeat];
        var target = _players[attack.TargetSeat];
        var mounts = GetQilinBowTargetMounts(target);
        if (mounts.Count == 0)
        {
            throw new InvalidOperationException("A Qilin Bow prompt requires at least one target mount.");
        }

        var choices = mounts.Select(card => new PromptChoice(
            new ChoiceId($"qilin-bow.mount-{card.Id}.resolution-{attack.ResolutionId}"),
            $"发动【麒麟弓】，弃置 {target.Name} 的【{card.DisplayName}】。",
            [card.Id],
            [target.Seat],
            new Dictionary<string, string>
            {
                ["action"] = "qilin-bow-discard",
                ["target-zone"] = "equipment"
            })).ToList();
        choices.Add(new PromptChoice(
            new ChoiceId($"qilin-bow.skip.resolution-{attack.ResolutionId}"),
            "不发动【麒麟弓】，继续结算此伤害。",
            [],
            [],
            new Dictionary<string, string> { ["action"] = "qilin-bow-skip" }));

        _pendingDecision = new PendingDecision(
            DecisionKind.QilinBow,
            source.Seat,
            $"你的【{CardCatalog.Get(RequireAttackCardKind(attack)).DisplayName}】将对 {target.Name} 造成伤害，是否发动【麒麟弓】弃置其一张坐骑？",
            mounts.Select(card => card.Id).ToArray(),
            [target.Seat],
            SourceSeat: source.Seat,
            IncomingCard: attack.EffectiveCardKind)
        {
            PromptId = source.IsHuman ? CreatePromptId() : default,
            IsPrivate = true,
            TargetSeat = target.Seat,
            Choices = choices
        };
        _status = source.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveQilinBowChoice(PromptChoice selected)
    {
        var pending = ActiveQilinBow ??
            throw new InvalidOperationException("There is no Qilin Bow choice to resolve.");
        var attack = pending.Attack;
        if (!SameAttackOwner(ActiveCardAttack, attack) ||
            _pendingDecision is not { Kind: DecisionKind.QilinBow } decision ||
            decision.PlayerSeat != attack.SourceSeat)
        {
            throw new InvalidOperationException("The Qilin Bow choice is not the current Slash continuation.");
        }

        var source = _players[attack.SourceSeat];
        var target = _players[attack.TargetSeat];
        var action = selected.Parameters.GetValueOrDefault("action");
        Card? discardedMount = null;
        if (action == "qilin-bow-discard" &&
            selected.Targets.SequenceEqual([target.Seat]) &&
            selected.Cards.Count == 1)
        {
            discardedMount = GetQilinBowTargetMounts(target)
                .SingleOrDefault(card => card.Id == selected.Cards[0]) ??
                throw new InvalidOperationException("The selected Qilin Bow mount is no longer available.");
        }
        else if (action != "qilin-bow-skip" ||
                 selected.Cards.Count != 0 ||
                 selected.Targets.Count != 0)
        {
            throw new InvalidOperationException("The Qilin Bow choice is malformed.");
        }

        ActiveQilinBow = null;
        ClearPendingDecision();
        SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        if (discardedMount is not null)
        {
            MoveCard(
                discardedMount,
                FindOwnedCardLocation(target, discardedMount),
                CardLocation.DiscardPile,
                CardMoveReasons.QilinBowDiscard);
        }
        AdvanceEventRulesAndQueueFact(new QilinBowResolvedEvent(
            attack.ResolutionId,
            source.Seat,
            target.Seat,
            Used: discardedMount is not null,
            DiscardedMountCardId: discardedMount?.Id));
        AddLog(
            discardedMount is null ? "EquipmentSkipped" : "EquipmentEffect",
            discardedMount is null
                ? $"{source.Name} 未发动【麒麟弓】，继续对 {target.Name} 结算伤害。"
                : $"{source.Name} 发动【麒麟弓】，弃置 {target.Name} 的【{discardedMount.DisplayName}】。",
            source.Seat,
            target.Seat);
        if (!ApplyAttackDamage(attack))
        {
            CompleteAttack(attack);
        }
    }

    private bool ApplyAttackDamage(IDamageAttempt attack)
    {
        if (TryPauseRecoveryPaidCardUse(attack.ResolutionId, new(RecoveryPaidCardUseKind.AttackDamage, attack.SourceSeat))) return true;
        var source = _players[attack.SourceSeat];
        var target = _players[attack.TargetSeat];
        if (!target.IsAlive)
        {
            return false;
        }

        if (source.IsAlive && HasCardPolicy(source, SkillProgramCardPolicyKind.DamageBecomesHpLoss))
            return ApplyAttackAsHpLoss(attack);

        if (!attack.IsChainPropagation &&
            (HasDirectedCardArmorBypass(attack.ResolutionId, target.Seat) ||
             HasCurrentCardEnhancement(attack.ResolutionId, CurrentCardEnhancement.IgnoreArmor)))
            attack.SetIgnoresArmor(true);
        var nature = GetDamageNature(attack);
        var amount = FinalizeAttackDamageAmount(attack);
        if (nature != DamageNature.Thunder && target.Markers.GetValueOrDefault(PlayerMarkerKind.Mist) > 0)
        {
            AddLog("DamagePrevented", $"{target.Name} 的大雾防止了伤害。", attack.SourceSeat, target.Seat);
            return false;
        }
        if (TryPreventWuyanDamage(attack, amount))
        {
            return false;
        }
        if (TryConsumeOneUseDamageShield(attack, amount))
        {
            return false;
        }
        if (TryBeginBeforeDamageProgramWindowForAttack(attack, amount, nature))
        {
            return true;
        }
        if (attack is CardAttackHandle iceSwordAttack && TryBeginIceSwordChoice(iceSwordAttack, amount))
        {
            return true;
        }
        if (attack is CardAttackHandle qilinBowAttack && TryBeginQilinBowChoice(qilinBowAttack))
        {
            return true;
        }
        if (!attack.IsChainPropagation &&
            (nature is DamageNature.Fire or DamageNature.Thunder) &&
            target.IsChained)
        {
            var propagatedTargets = _players
                .Where(player =>
                    player.IsAlive &&
                    player.Seat != target.Seat &&
                    player.IsChained)
                .Select(player => player.Seat)
                .ToArray();
            var chainMembers = new[] { target.Seat }
                .Concat(propagatedTargets)
                .ToArray();
            CaptureRecipientScopedDamageBase(attack, propagatedTargets);
            attack.SetChainedTargets(propagatedTargets);
            foreach (var chainSeat in chainMembers)
            {
                _players[chainSeat].IsChained = ResolveEnteringChain(_players[chainSeat], HasCardPolicy(_players[chainSeat], SkillProgramCardPolicyKind.ForceChained));
                AdvanceEventRulesAndQueueFact(new IronChainStateChangedEvent(
                    attack.ResolutionId,
                    source.Seat,
                    chainSeat,
                    IsChained: false));
            }

            if (propagatedTargets.Length > 0)
            {
                AddLog(
                    "ChainDamage",
                    $"{target.Name} 受到{GetDamageNatureLabel(nature)}伤害，向 {propagatedTargets.Length} 名连环角色传导同量伤害。",
                    source.Seat,
                    target.Seat);
            }
        }
        var damageFrameId = BeginDamage(
            attack.ResolutionId,
            source.Seat,
            target.Seat,
            amount,
            nature);
        var awaitingDying = false;
        var awaitingDamageTrigger = false;
        try
        {
            AdvanceEventRulesAndQueueFact(new DamageRequestedEvent(
                damageFrameId,
                source.Seat,
                target.Seat,
                amount,
                attack.EffectiveCardKind,
                nature) { SourceLess = attack.IsSourceLess });
            attack.CaptureDamageDistance(source.Seat == target.Seat
                ? 0
                : GetCombatDistance(source.Seat, target.Seat));
            if (amount > 0 && attack.PendingRedBladeDamageBonus)
            {
                _skillRuntimeState.TryConsumeUsage(source.Seat, "equipment:red-blood-blade", "farthest-damage", SkillUsageScope.Turn, 1);
                attack.PendingRedBladeDamageBonus = false;
            }
            target.Hp = Math.Max(0, target.Hp - amount);
            attack.MarkDamageApplied();
            if (amount > 0) RecordCompletedCardDamageParticipant(attack.ResolutionId, target.Seat);
            var useIndex = _resolutionStack.FindLastIndex(frame => frame is CardUseFrame use &&
                use.Id == attack.ResolutionId);
            if (useIndex >= 0 && _resolutionStack[useIndex] is CardUseFrame damageUse)
                ReplaceRuntimeFrame(_resolutionStack[useIndex].Id, damageUse with { CausedDamage = true });
            if (!attack.IsSourceLess && source.Seat == _currentSeat && _phase == TurnPhase.Play)
                _playPhaseDamageDealtByCurrentPlayer += amount;
            if (!attack.IsSourceLess) AccumulateProgramPlayDamageHandLimit(source.Seat, amount);
            var natureLabel = GetDamageNatureLabel(nature);
            AddLog("Damage", attack.IsSourceLess
                ? $"{target.Name} 受到 {amount} 点无来源{natureLabel}伤害，剩余 {Math.Max(0, target.Hp)} 点体力。"
                : $"{target.Name} 受到 {source.Name} 造成的 {amount} 点{natureLabel}伤害，剩余 {Math.Max(0, target.Hp)} 点体力。",
                attack.IsSourceLess ? null : source.Seat, target.Seat);
            AdvanceEventRulesAndQueueFact(new DamageAppliedEvent(
                source.Seat,
                target.Seat,
                amount,
                Math.Max(0, target.Hp),
                nature) { SourceLess = attack.IsSourceLess });
            if (amount > 0 && HasCurrentCardEnhancement(attack.ResolutionId, CurrentCardEnhancement.DrawAfterDamage))
            {
                var enhancementOwnerSeat = _resolutionStack.OfType<CardUseFrame>()
                    .Single(use => use.Id == attack.ResolutionId).EnhancementOwnerSeat ?? source.Seat;
                var enhancementOwner = _players[enhancementOwnerSeat];
                if (enhancementOwner.IsAlive) DrawCards(enhancementOwner, 1, log: true);
            }
            if (!attack.IsSourceLess) ApplyHengyeGrowth(damageFrameId, source, target, amount);
            if (TryBeginDamageTriggerWindow(
                    attack, damageFrameId, SkillProgramTriggerWindow.DamageAppliedBeforeDying))
            {
                awaitingDamageTrigger = true;
            }
            else if (target.Hp <= 0)
            {
                BeginDying(attack, damageFrameId, target, source);
                // Buqu may finish this dying continuation synchronously.
                awaitingDying = true;
            }
            else
            {
                awaitingDamageTrigger = TryBeginDamageTriggerWindow(
                    attack, damageFrameId, SkillProgramTriggerWindow.AfterDamageApplied);
            }

            if (!awaitingDying && !awaitingDamageTrigger)
            {
                AdvanceEventRulesAndQueueFact(new AfterDamageEvent(
                    damageFrameId,
                    source.Seat,
                    target.Seat,
                    amount,
                    Math.Max(0, target.Hp),
                    nature) { SourceLess = attack.IsSourceLess });
            }
        }
        finally
        {
            if (!awaitingDying && !awaitingDamageTrigger)
            {
                PopResolutionFrame(damageFrameId, ResolutionFrameKind.Damage);
            }
        }

        return awaitingDying || awaitingDamageTrigger;
    }

    private Suit EffectiveSuit(CharacterState owner, Card card) =>
        LiveDeclarationPayment(card.Id)?.FrozenSuit ?? GetProgramEffectiveSuit(owner, card);

    private bool TryBeginDamageTriggerWindow(
        IDamageAttempt attack,
        long damageFrameId,
        SkillProgramTriggerWindow window)
    {
        var source = _players[attack.SourceSeat];
        var triggerCandidates = DamageTriggerOrdering.Order(
            CollectDamageTriggerCandidates(attack, window),
            source.Seat,
            _playerCount);
        if (triggerCandidates.Count == 0) return false;

        BeginDamageTriggerWindow(attack, damageFrameId, window, triggerCandidates);
        // Expose an optional trigger in the command that caused the damage.
        // Automatic candidates remain one deterministic engine step each.
        if (triggerCandidates[0].IsOptional)
        {
            RunOneDamageTriggerStep();
        }
        return true;
    }

    private void BeginDamageTriggerWindow(
        IDamageAttempt attack,
        long damageFrameId,
        SkillProgramTriggerWindow window,
        IReadOnlyList<DamageTriggerCandidate> candidates)
    {
        if (ActiveDamageTrigger is not null)
        {
            throw new InvalidOperationException("The engine cannot resolve two damage trigger windows at once.");
        }

        var frozenCandidates = Array.AsReadOnly(candidates.ToArray());
        var frameId = ++_resolutionSequence;
        PushRuntimeFrame(new DamageTriggerWindowFrame(
            frameId,
            damageFrameId,
            attack.SourceSeat,
            attack.TargetSeat,
            attack.Card?.Id,
            attack.EffectiveCardKind,
            frozenCandidates,
            window)
        { EventTargetDamageInstancesTakenThisTurn = window == SkillProgramTriggerWindow.AfterDamageApplied &&
            _contentRegistry.ProgramDependencies.UsesTriggerValue(SkillProgramTriggerValueKind.EventTargetDamageInstancesTakenThisTurn)
                ? DamageInstancesTakenThisTurn(attack.TargetSeat) : null });
        AdvanceEventRulesAndQueueFact(new DamageTriggerWindowOpenedEvent(
            frameId,
            damageFrameId,
            attack.SourceSeat,
            attack.TargetSeat,
            attack.Card?.Id,
            attack.EffectiveCardKind,
            frozenCandidates));
    }

    private void RunOneDamageTriggerStep()
    {
        var window = ActiveDamageTrigger ??
            throw new InvalidOperationException("A damage trigger step has no pending window.");
        if (_pendingDecision is not null)
        {
            return;
        }

        if (window.CandidateIndex >= window.Candidates.Count)
        {
            CompleteDamageTriggerWindow(window);
            return;
        }

        var candidate = window.Candidates[window.CandidateIndex];
        if (candidate.ToProgramCandidate() is { } programCandidate)
        {
            var programContext = CreateAfterDamageProgramContext(window, programCandidate);
            if (!CanRunProgramTrigger(programCandidate, programContext))
            {
                AdvanceDamageTriggerCandidate(window);
                return;
            }
            if (candidate.IsOptional)
            {
                ExposeProgramTriggerDecision(programCandidate, programContext);
                return;
            }
            BeginProgramBinding(programCandidate, programContext);
            return;
        }
        throw new InvalidOperationException("A damage trigger candidate has no configured program.");
    }

    private void AdvanceDamageTriggerCandidate(DamageTriggerWindowFrame window)
    {
        if (ActiveDamageTrigger is not { } current || current.Id != window.Id)
        {
            throw new InvalidOperationException("The completed damage trigger window is not current.");
        }
        window = current;

        var nextIndex = window.CandidateIndex + 1;
        // A nested program damage death can determine the winner before the final
        // trigger candidate. Close this window now so the host never stops at
        // Winner != None with live frames, Processing cards or a new prompt.
        if (_winner != Winner.None) nextIndex = window.Candidates.Count;
        window = SetDamageTriggerWindowCursor(window.Id, nextIndex);
        AdvanceEventRulesAndQueueFact(new DamageTriggerWindowAdvancedEvent(
            window.Id,
            window.CandidateIndex,
            window.CandidateIndex >= window.Candidates.Count));
        if (window.CandidateIndex >= window.Candidates.Count)
        {
            CompleteDamageTriggerWindow(window);
        }
    }

    private void CompleteDamageTriggerWindow(DamageTriggerWindowFrame window)
    {
        if (ActiveDamageTrigger is not { } current || current.Id != window.Id)
        {
            throw new InvalidOperationException("The damage trigger window is not ready to complete.");
        }
        window = current;

        var attack = GetDamageTriggerAttack(window);
        SetDamageTriggerWindowStep(window.Id, ResolutionFrameStep.Completed);
        var target = _players[attack.TargetSeat];
        PopResolutionFrame(window.Id, ResolutionFrameKind.DamageTriggerWindow);
        if (window.TriggerWindow == SkillProgramTriggerWindow.DamageAppliedBeforeDying)
        {
            if (target.Hp <= 0 && target.IsAlive)
            {
                BeginDying(attack, window.ParentFrameId, target,
                    _players[attack.SourceSeat]);
                return;
            }
            if (TryBeginDamageTriggerWindow(attack, window.ParentFrameId,
                    SkillProgramTriggerWindow.AfterDamageApplied))
            {
                return;
            }
        }
        if (target.Hp <= 0 && target.IsAlive &&
            !attack.HasResolvedDyingForDamage(window.ParentFrameId))
        {
            BeginDying(attack, window.ParentFrameId, target, _players[attack.SourceSeat]);
            return;
        }

        AdvanceEventRulesAndQueueFact(new AfterDamageEvent(
            window.ParentFrameId,
            attack.SourceSeat,
            attack.TargetSeat,
            attack.DamageAmount,
            Math.Max(0, target.Hp),
            GetDamageNature(attack)) { SourceLess = attack.IsSourceLess });

        PopResolutionFrame(window.ParentFrameId, ResolutionFrameKind.Damage);
        CompleteDamageAttack(attack);
    }

    private IReadOnlyList<DamageTriggerCandidate> CollectDamageTriggerCandidates(
        IDamageAttempt attack,
        SkillProgramTriggerWindow window)
    {
        var candidates = new List<DamageTriggerCandidate>();

        foreach (var programOwnerSeat in _players.Where(player => player.IsAlive)
                     .Select(player => player.Seat))
            foreach (var candidate in CollectProgramTriggerCandidates(
                         _players[programOwnerSeat], window))
            {
                var trigger = _contentRegistry!.GetSkill(candidate.SkillId).Program!.Triggers
                    .Single(item => item.Id == candidate.BindingId);
                var ownsTiming = trigger.Subject switch
                {
                    SkillProgramTriggerSubject.Owner => programOwnerSeat == attack.TargetSeat,
                    SkillProgramTriggerSubject.DamageTarget => window == SkillProgramTriggerWindow.DamageAppliedBeforeDying &&
                        trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.DrawByDamageCardColor) && programOwnerSeat == attack.TargetSeat,
                    SkillProgramTriggerSubject.Source => !attack.IsSourceLess && programOwnerSeat == attack.SourceSeat &&
                        MatchesAfterDamageProgramSource(attack, trigger),
                    SkillProgramTriggerSubject.DamageSource => !attack.IsSourceLess && programOwnerSeat == attack.SourceSeat,
                    SkillProgramTriggerSubject.Any => true,
                    _ => false
                };
                if (!ownsTiming) continue;
                var occurrenceIndexes = trigger.DamageOccurrence switch
                {
                    null or SkillProgramDamageOccurrence.PerDamage => [0],
                    SkillProgramDamageOccurrence.PerDamagePoint =>
                        Enumerable.Range(0, Math.Max(0, attack.DamageAmount)).ToArray(),
                    _ => throw new InvalidOperationException(
                        "An after-damage trigger lost its occurrence policy.")
                };
                foreach (var occurrenceIndex in occurrenceIndexes)
                {
                    candidates.Add(new DamageTriggerCandidate(
                        candidate.OwnerSeat,
                        $"program:{candidate.SkillId}:{candidate.BindingId}:{candidate.SkillInstanceId}:occurrence-{occurrenceIndex}",
                        candidate.Priority,
                        IsOptional: trigger.Optional,
                        ProgramId: candidate.SkillId,
                        ProgramTriggerId: candidate.BindingId,
                        SkillInstanceId: candidate.SkillInstanceId,
                        GameplayHash: candidate.GameplayHash,
                        OccurrenceIndex: occurrenceIndex));
                }
            }

        return candidates;
    }

    private static bool MatchesAfterDamageProgramSource(
        IDamageAttempt attack,
        SkillProgramTrigger trigger) =>
        attack.ConversionSource is { } source &&
        string.Equals(source.SkillId, trigger.SourceSkillId, StringComparison.Ordinal) &&
        (trigger.SourceViewAsId is null ||
         string.Equals(source.BindingId, trigger.SourceViewAsId, StringComparison.Ordinal));

    private void BeginDying(
        IDamageAttempt attack,
        long damageFrameId,
        CharacterState victim,
        CharacterState killer)
    {
        if (ActiveDying is not null)
        {
            throw new InvalidOperationException("The engine cannot resolve two dying players at once.");
        }

        var responderSeats = Array.AsReadOnly(BuildDyingResponderSeats(victim.Seat).ToArray());
        var frameId = ++_resolutionSequence;
        PushRuntimeFrame(new DyingFrame(
            frameId,
            damageFrameId,
            victim.Seat,
            attack.IsSourceLess ? null : killer.Seat,
            responderSeats,
            ResponderIndex: 0,
            DyingContinuationKind.Damage));
        CaptureDynamicDiscardDamageDying(frameId, damageFrameId, victim.Seat);
        AdvanceEventRulesAndQueueFact(new PlayerDyingEvent(frameId, victim.Seat, attack.IsSourceLess ? null : killer.Seat));
        _status = EngineStatus.Running;
        if (TryBeginMandatorySelfDyingProgram(ActiveDying!) || TryBeginDyingEntryProgramWindow(ActiveDying!))
        {
            return;
        }
        ExposeHumanDyingPrompt();
    }

    private IReadOnlyList<int> BuildDyingResponderSeats(int victimSeat)
    {
        var seats = new List<int>(_playerCount);
        for (var offset = 0; offset < _playerCount; offset++)
        {
            var seat = (victimSeat + offset) % _playerCount;
            if (_players[seat].IsAlive)
            {
                seats.Add(seat);
            }
        }

        return seats;
    }

    private Card[] GetDyingPeaches(CharacterState responder)
    {
        if (ActiveDying is { } dying && !CanUsePeachToRescue(responder.Seat, dying.VictimSeat)) return [];
        var handCandidates = GetPlayableCards(responder)
            .Where(card => !IsTurnHandCardRestricted(responder, card))
            .Where(card => !HasProgramCardIdentity(responder, card))
            .Where(card => card.Kind == CardKind.Peach && (ActiveDying is not { } victim || !HasBeneficiarySuitShield(responder.Seat, victim.VictimSeat, EffectiveSuit(responder, card))) ||
                           GetDyingPeachConversionSources(responder, card).Count != 0)
            .ToArray();
        return handCandidates
            .Concat(GetEquipment(responder).Where(card =>
                GetDyingPeachConversionSources(responder, card).Count != 0))
            .ToArray();
    }

    private IReadOnlyList<CardConversionSource> GetDyingPeachConversionSources(
        CharacterState responder, Card card) =>
        card.Kind == CardKind.Peach
            ? []
            : GetProgramViewAsConversions(responder, card, CardKind.Peach,
                forResponse: true, dyingUse: true).Where(source => ActiveDying is not { } dying || !HasBeneficiarySuitShield(responder.Seat, dying.VictimSeat, EffectiveSuit(responder, ApplyProgramUseAppearance(responder, card, source)))).ToArray();

    private Card[] GetDyingAlcohols(CharacterState responder, int victimSeat) =>
        responder.Seat == victimSeat && !HasSelfCardTargetProhibition(responder.Seat)
            ? GetHand(responder).Concat(GetEquipment(responder)).Concat(SelectedRequestedDeckBasicCards(responder))
                .Where(card => !IsTurnHandCardRestricted(responder, card) &&
                    !IsCardUseForbidden(responder.Seat, CardKind.Alcohol, CardActionType.Use) &&
                    !HasProgramCardIdentity(responder, card) &&
                    (card.Kind == CardKind.Alcohol || GetProgramViewAsConversions(
                        responder, card, CardKind.Alcohol, forResponse: true, dyingUse: true).Count > 0))
                .ToArray()
            : [];

    private void RunOneDyingStep(bool skipRequestedDeckBasic = false)
    {
        var dying = ActiveDying ??
            throw new InvalidOperationException("A dying step requires a pending dying resolution.");
        if (_pendingDecision is not null)
        {
            return;
        }

        if (dying.ResponderIndex >= dying.ResponderSeats.Count)
        {
            CompleteDying(dying, survived: false);
            AdvanceRulesAndPublishState();
            return;
        }

        var responder = _players[dying.ResponderSeat];
        var peaches = GetDyingPeaches(responder);
        var alcohols = GetDyingAlcohols(responder, dying.VictimSeat);
        var programCandidates = GetDyingProgramCandidates(responder, dying);
        if (responder.IsHuman)
        {
            if (peaches.Length > 0 || alcohols.Length > 0 || programCandidates.Count > 0 ||
                ExtendedViewAsResponseChoices(responder, CardKind.Peach).Count > 0 || HasTieredRoundZeroDyingResponse(responder) || HasRequestedDeckBasicSource(responder, CardKind.Peach))
            {
                RequestHumanDyingResponse(responder, peaches, alcohols);
                return;
            }

            ApplyDyingResponse(
                responder,
                usePeach: false,
                peachCardId: null,
                useAlcohol: false,
                alcoholCardId: null);
            AdvanceRulesAndPublishState();
            return;
        }

        if (programCandidates.FirstOrDefault(item =>
                GetProgramTrigger(item).Window == SkillProgramTriggerWindow.SelfDyingResponse) is
            { } programCandidate)
        {
            BeginDyingProgramBinding(programCandidate, dying);
            AdvanceRulesAndPublishState();
            return;
        }

        foreach (var rescueCandidate in programCandidates.Where(item =>
            GetProgramTrigger(item).Window == SkillProgramTriggerWindow.DyingResponse))
        {
            var (useRescue, rescueThought) = _aiBrains[responder.Seat].ChooseProgramDyingRescue(
                CreateSnapshot(responder.Seat), dying.VictimSeat,
                _contentRegistry!.GetSkill(rescueCandidate.SkillId).Name, ++_thoughtSequence);
            AddThought(rescueThought);
            if (!useRescue) continue;
            BeginDyingProgramBinding(rescueCandidate, dying);
            AdvanceRulesAndPublishState();
            return;
        }

        if (!skipRequestedDeckBasic && HasRequestedDeckBasicSource(responder, CardKind.Peach))
        { RequestHumanDyingResponse(responder, peaches, alcohols); AdvanceRulesAndPublishState(); return; }
        if (TryResolveTieredRoundZeroAiDying()) return;
        var view = CreateSnapshot(responder.Seat);
        if (peaches.Length == 0 && CanUsePeachToRescue(responder.Seat, dying.VictimSeat) &&
            GetProgramMultiCardViewAsSelections(responder, CardKind.Peach, true).FirstOrDefault(selection => !IsShieldedRescueSelection(responder, selection)) is { } multiPeach)
        {
            var (useMulti, multiThought) = _aiBrains[responder.Seat].ChooseProgramDyingRescue(view, dying.VictimSeat,
                ProgramConversionName(multiPeach.Source), ++_thoughtSequence);
            AddThought(multiThought);
            if (useMulti) { ResolveExtendedViewAsResponse(responder, multiPeach); AdvanceRulesAndPublishState(); return; }
        }
        var (usePeach, peachCardId, useAlcohol, alcoholCardId, thought) = _aiBrains[responder.Seat].ChooseDyingResponseWithAlcohol(
            view,
            dying.VictimSeat,
            peaches,
            alcohols,
            ++_thoughtSequence,
            false);
        AddThought(thought);
        var aiPeachConversion = usePeach && peachCardId is { } selectedPeachId
            ? peaches.Where(card => card.Id == selectedPeachId && card.Kind != CardKind.Peach)
                .SelectMany(card => GetDyingPeachConversionSources(responder, card))
                .FirstOrDefault()
            : null;
        var aiAlcoholConversion = useAlcohol && alcoholCardId is { } selectedAlcoholId
            ? alcohols.Where(card => card.Id == selectedAlcoholId && card.Kind != CardKind.Alcohol)
                .SelectMany(card => GetProgramViewAsConversions(responder, card, CardKind.Alcohol, forResponse: true, dyingUse: true))
                .FirstOrDefault()
            : null;
        ApplyDyingResponse(
            responder,
            usePeach,
            peachCardId,
            useAlcohol,
            alcoholCardId,
            aiPeachConversion,
            aiAlcoholConversion);
        AdvanceRulesAndPublishState();
    }

    private void ExposeHumanDyingPrompt()
    {
        var dying = ActiveDying;
        if (dying is null || _pendingDecision is not null ||
            dying.ResponderIndex >= dying.ResponderSeats.Count)
        {
            return;
        }

        var responder = _players[dying.ResponderSeat];
        if (!responder.IsHuman)
        {
            return;
        }

        var peaches = GetDyingPeaches(responder);
        var alcohols = GetDyingAlcohols(responder, dying.VictimSeat);
        var programCandidates = GetDyingProgramCandidates(responder, dying);
        if (peaches.Length > 0 || alcohols.Length > 0 || programCandidates.Count > 0 ||
            ExtendedViewAsResponseChoices(responder, CardKind.Peach).Count > 0 || HasTieredRoundZeroDyingResponse(responder) || HasRequestedDeckBasicSource(responder, CardKind.Peach))
        {
            RequestHumanDyingResponse(responder, peaches, alcohols);
        }
    }

    private void RequestHumanDyingResponse(
        CharacterState responder,
        IReadOnlyList<Card> peaches,
        IReadOnlyList<Card> alcohols)
    {
        var dying = ActiveDying ??
            throw new InvalidOperationException("There is no dying resolution for the prompt.");
        var victim = _players[dying.VictimSeat];
        var choices = peaches.SelectMany(peach =>
        {
            var sources = peach.Kind == CardKind.Peach
                ? new CardConversionSource?[] { null }
                : GetDyingPeachConversionSources(responder, peach)
                    .Select(source => (CardConversionSource?)source).ToArray();
            return sources.Select((source, index) =>
            {
                var parameters = new Dictionary<string, string>
                {
                    ["response"] = "peach",
                    ["physical-card-kind"] = peach.Kind.ToString(),
                    ["target-seat"] = victim.Seat.ToString(System.Globalization.CultureInfo.InvariantCulture)
                };
                if (source is not null) AddConversionParameters(parameters, source);
                return new PromptChoice(
                    new ChoiceId($"dying.peach.card-{peach.Id}.source-{index}"),
                    peach.Kind == CardKind.Peach
                        ? $"使用【桃】救援 {victim.Name}。"
                        : $"发动【{_contentRegistry!.GetSkill(source!.SkillId).Name}】，将【{peach.DisplayName}】当作【桃】救援 {victim.Name}。",
                    [peach.Id], [], parameters);
            });
        }).ToList();
        choices.AddRange(alcohols.SelectMany(alcohol => CreateConversionChoiceVariants(
            responder, alcohol, CardKind.Alcohol, forResponse: true,
            $"dying.alcohol.card-{alcohol.Id}", $"将【{alcohol.DisplayName}】作为【酒】自救，回复 1 点体力。",
            [alcohol.Id], [], new Dictionary<string, string>
            {
                ["response"] = "alcohol",
                ["target-seat"] = victim.Seat.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }, dyingUse: true)));
        var programCandidates = GetDyingProgramCandidates(responder, dying);
        choices.AddRange(ExtendedViewAsResponseChoices(responder, CardKind.Peach));
        choices.AddRange(TieredRoundZeroResponseChoices(responder, CardKind.Peach, true));
        choices.AddRange(TieredRoundZeroResponseChoices(responder, CardKind.Alcohol, true));
        foreach (var candidate in programCandidates)
        {
            var skill = _contentRegistry!.GetSkill(candidate.SkillId);
            choices.Add(new PromptChoice(
                new ChoiceId($"dying.program.{candidate.SkillId}.{candidate.BindingId}.{candidate.SkillInstanceId}"),
                GetProgramTrigger(candidate).Window == SkillProgramTriggerWindow.DyingResponse
                    ? $"发动【{skill.Name}】救援 {victim.Name}。"
                    : $"发动【{skill.Name}】：{skill.Description}",
                [], [],
                new Dictionary<string, string>
                {
                    ["response"] = "program-trigger",
                    ["skill-id"] = candidate.SkillId,
                    ["binding-id"] = candidate.BindingId,
                    ["skill-instance-id"] = candidate.SkillInstanceId,
                    ["target-seat"] = victim.Seat.ToString(System.Globalization.CultureInfo.InvariantCulture),
                }));
        }
        var rescueCardIds = peaches
            .Select(card => card.Id)
            .Concat(alcohols.Select(card => card.Id))
            .ToArray();
        var rescueNameParts = new List<string>();
        if (peaches.Count > 0) rescueNameParts.Add("桃");
        if (alcohols.Count > 0) rescueNameParts.Add("酒");
        rescueNameParts.AddRange(programCandidates.Select(candidate =>
            _contentRegistry!.GetSkill(candidate.SkillId).Name));
        var rescueNames = string.Join("】或【", rescueNameParts);
        choices.Add(new PromptChoice(
            new ChoiceId("dying.let-die"),
            $"不使用【{rescueNames}】，让 {victim.Name} 阵亡。",
            [],
            [],
            new Dictionary<string, string>
            {
                ["response"] = "let-die",
                ["target-seat"] = victim.Seat.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }));

        _pendingDecision = new PendingDecision(
            DecisionKind.RescueDying,
            responder.Seat,
            $"{victim.Name} 进入濒死状态，是否使用【{rescueNames}】救援？",
            rescueCardIds,
            [],
            SourceSeat: victim.Seat)
        {
            PromptId = CreatePromptId(),
            Choices = choices,
            TargetSeat = victim.Seat
        };
        _status = EngineStatus.AwaitingHumanDying;
        AdvanceRulesAndPublishState();
    }

    private void ApplyDyingResponse(
        CharacterState responder,
        bool usePeach,
        int? peachCardId,
        bool useAlcohol,
        int? alcoholCardId,
        CardConversionSource? peachConversionSource = null,
        CardConversionSource? alcoholConversionSource = null)
    {
        var dying = ActiveDying ??
            throw new InvalidOperationException("There is no dying response to apply.");
        if (responder.Seat != dying.ResponderSeat)
        {
            throw new InvalidOperationException("The response does not belong to the current dying responder.");
        }

        var victim = _players[dying.VictimSeat];
        SetDyingFrameStep(dying.FrameId, ResolutionFrameStep.ResolvingEffect);
        int? usedPeachCardId = null;
        CardKind? usedPeachPhysicalCardKind = null;
        int? usedAlcoholCardId = null;
        if (usePeach && useAlcohol)
        {
            throw new InvalidOperationException("A dying response can use only one rescue source.");
        }
        if (usePeach)
        {
            var peach = GetDyingPeaches(responder).FirstOrDefault(card =>
                (!peachCardId.HasValue || card.Id == peachCardId.Value));
            if (peach is null)
            {
                throw new InvalidOperationException("The requested Peach is not in the responder's playable zones.");
            }

            var sources = GetDyingPeachConversionSources(responder, peach);
            if (peach.Kind == CardKind.Peach
                ? peachConversionSource is not null
                : peachConversionSource is null || !sources.Contains(peachConversionSource))
                throw new InvalidOperationException("The requested Peach conversion is no longer legal.");

            usedPeachCardId = peach.Id;
            usedPeachPhysicalCardKind = peach.Kind == CardKind.Peach ? null : peach.Kind;
            ResolvePeach(responder, victim, peach, allowDying: true,
                peachConversionSource, new DyingResponseEvent(dying.FrameId, responder.Seat,
                    true, peach.Id, false, null)
                { UsedPeachPhysicalCardKind = usedPeachPhysicalCardKind });
            return;
        }
        if (useAlcohol)
        {
            if (responder.Seat != victim.Seat)
            {
                throw new InvalidOperationException("Alcohol can only rescue its dying holder.");
            }
            var alcohol = GetDyingAlcohols(responder, victim.Seat).FirstOrDefault(card =>
                (!alcoholCardId.HasValue || card.Id == alcoholCardId.Value));
            if (alcohol is null)
            {
                throw new InvalidOperationException("The requested Alcohol is not in the responder's hand.");
            }
            ValidateAlcoholConversion(responder, alcohol, alcoholConversionSource, forResponse: true);
            usedAlcoholCardId = alcohol.Id;
            ResolveDyingAlcohol(responder, victim, alcohol, alcoholConversionSource,
                new DyingResponseEvent(dying.FrameId, responder.Seat, false, null, true, alcohol.Id));
            return;
        }
        CompleteDyingCardResponse(new DyingResponseEvent(
            dying.FrameId,
            responder.Seat,
            usePeach,
            usedPeachCardId,
            useAlcohol,
            usedAlcoholCardId)
        {
            UsedPeachPhysicalCardKind = usedPeachPhysicalCardKind
        });
    }

    private void CompleteDyingCardResponse(DyingResponseEvent response)
    {
        var dying = ActiveDying ?? throw new InvalidOperationException("The rescue use lost its dying parent.");
        if (dying.FrameId != response.ResolutionId)
            throw new InvalidOperationException("The rescue use returned to a different dying parent.");
        var victim = _players[dying.VictimSeat];
        AdvanceEventRulesAndQueueFact(response);
        dying = UpdateDyingFrame(dying.Id, current => current with
        {
            ResponderIndex = current.ResponderIndex + 1
        });

        if (victim.Hp > 0)
        {
            CompleteDying(dying, survived: true);
            return;
        }

        if (dying.ResponderIndex >= dying.ResponderSeats.Count)
        {
            CompleteDying(dying, survived: false);
            return;
        }

        SetDyingFrameStep(dying.FrameId, ResolutionFrameStep.AwaitingResponse);
        _status = EngineStatus.Running;
        ExposeHumanDyingPrompt();
    }

    private void CompleteDying(DyingFrame dying, bool survived)
    {
        if (ActiveDying?.Id != dying.Id)
        {
            throw new InvalidOperationException("The completed dying resolution is not current.");
        }

        SetDyingFrameStep(dying.FrameId, ResolutionFrameStep.ResolvingEffect);
        if (!survived)
        {
            FinalizePlayerDeath(dying);
            return;
        }

        CompleteDyingAfterDeath(dying, survived);
    }

    private void CompleteDyingAfterDeath(DyingFrame dying, bool survived)
    {
        if (ActiveDying?.Id != dying.Id)
        {
            throw new InvalidOperationException("The completed dying continuation is not current.");
        }
        var receipt = new DyingCompletionReceipt(
            dying.Id, dying.ParentFrameId, dying.VictimSeat, dying.Continuation);
        CaptureDynamicDiscardDamageSurvival(receipt, survived);
        AdvanceEventRulesAndQueueFact(new DyingResolvedEvent(dying.FrameId, dying.VictimSeat, survived));
        PopResolutionFrame(dying.FrameId, ResolutionFrameKind.Dying);
        if (TryBeginFactionRecoveryDebts(receipt, survived)) return;
        ContinueDyingAfterFactionRecoveryDebts(receipt, survived);
    }

    private void ContinueDyingAfterFactionRecoveryDebts(DyingCompletionReceipt dying, bool survived)
    {
        if (dying.Continuation == DyingContinuationKind.AttackHpLoss)
        {
            CompleteDamageAttack(GetCompletedDyingAttack(dying)!);
            return;
        }
        if (dying.Continuation == DyingContinuationKind.ProgramSkill)
        {
            CompleteProgramSkillAfterDying(dying);
            return;
        }
        CompleteDamageAfterDying(dying);
    }

    private void CompleteDamageAfterDying(DyingCompletionReceipt dying)
    {
        var victim = _players[dying.VictimSeat];
        var attack = GetCompletedDyingAttack(dying) ??
            throw new InvalidOperationException("A damage dying continuation has no attack.");
        if (dying.Continuation != DyingContinuationKind.Damage)
            throw new InvalidOperationException("A damage dying continuation has no damage frame.");
        var damageFrameId = dying.ParentFrameId;
        attack.MarkDyingResolvedForDamage(damageFrameId);
        if (_winner == Winner.None &&
            TryBeginDamageTriggerWindow(attack, damageFrameId,
                SkillProgramTriggerWindow.AfterDamageApplied))
        {
            return;
        }
        AdvanceEventRulesAndQueueFact(new AfterDamageEvent(
            damageFrameId,
            attack.SourceSeat,
            dying.VictimSeat,
            attack.DamageAmount,
            Math.Max(0, victim.Hp),
            GetDamageNature(attack)) { SourceLess = attack.IsSourceLess });
        PopResolutionFrame(damageFrameId, ResolutionFrameKind.Damage);
        CompleteDamageAttack(attack);
    }

    private void CompleteDamageAttack(IDamageAttempt attack)
    {
        if (attack is CardAttackHandle card) { CompleteAttack(card); return; }
        CompleteProgramAttack(attack);
    }

    private bool FinishAttack(CardAttackHandle attack, bool allowYingboGift = true)
    {
        if (attack.IsProgramSkillDamage)
        {
            if (attack.Card is not null || attack.ProgramSkillFrameId != attack.ResolutionId)
                throw new InvalidOperationException(
                    "Configured active-program damage must retain its program frame and no physical card.");
            return false;
        }

        if (attack.IsProgramJudgmentDamage)
        {
            if (attack.Card is not null || attack.ProgramJudgmentFrameId is null)
            {
                throw new InvalidOperationException(
                    "Configured judgment damage must retain its program frame and no physical card.");
            }
            return false;
        }

        if (FinishTieredRoundZeroAttack(attack, out var tieredZeroPaused)) return tieredZeroPaused;
        if (attack.Card is null && attack.EffectiveCardKind == CardKind.Duel && IsIssuedZeroEntityDuel(attack.ResolutionId))
        {
            var use = LifecycleCardUse(attack.ResolutionId)!;
            SetCardUseStep(use.Id, ResolutionFrameStep.Completed);
            AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(use.Id, 0, CardKind.Duel));
            if (_winner == Winner.None && TryBeginProgramCardWindow(attack, use.Action!,
                    SkillProgramTriggerWindow.CardUseCompleted, use.TargetSeats, ProgramCardContinuation.CompletedSlash,
                    cardUseCausedDamage: attack.CardUseCausedDamage)) return true;
            PopFinishedCardUse(use.Id);
            CompleteFinishedAttackCardUse(attack);
            return false;
        }

        if (attack.Card is null && (attack.EffectiveCardKind == CardKind.Slash ||
            LifecycleCardUse(attack.ResolutionId) is { } fireUse && IsCurrentSlashFireChangedUse(fireUse) ||
            IsSlashCard(attack.EffectiveCardKind ?? CardKind.Slash) && LifecycleCardUse(attack.ResolutionId)?.VirtualBasicReturn is not null) &&
            (attack.ProgramSkillCardUseFrameId is not null || LifecycleCardUse(attack.ResolutionId)?.VirtualBasicReturn is not null || IsForeignPublicPileSlashUse(attack.ResolutionId)))
        {
            var virtualUse = _resolutionStack.OfType<CardUseFrame>()
                .Single(frame => frame.Id == attack.ResolutionId);
            if (virtualUse.Action is { } virtualAction)
            {
                if (attack.ProgramSkillCardUseFrameId is { } programParentId && virtualUse.SharedSlashBenefit is null && virtualUse.ForeignTurnContestSlashReturn is null && virtualUse.PindianWinnerSlashReturn is null)
                {
                    var parentIndex = _resolutionStack.FindIndex(frame => frame.Id == programParentId);
                    if (parentIndex < 0 || _resolutionStack[parentIndex] is not ProgramSkillFrame parent)
                        throw new InvalidOperationException("The virtual Slash lost its selected-target program parent.");
                    IReadOnlyList<int> primaryTarget = virtualUse.TargetSeats;
                    if (virtualUse.TargetSeats.Count != 1 &&
                        !TryGetOriginalTargetVirtualSlashReturn(virtualUse, parent, out primaryTarget))
                        throw new InvalidOperationException("The virtual Slash lost its selected-target program parent.");
                    ReplaceRuntimeFrame(parent.Id, parent with { SelectedTargetSeats = primaryTarget });
                }
                SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.Completed);
                AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(attack.ResolutionId, 0, virtualUse.CardKind));
                if (_winner == Winner.None && TryBeginProgramCardWindow(attack, virtualAction,
                        SkillProgramTriggerWindow.CardUseCompleted, virtualUse.TargetSeats,
                        ProgramCardContinuation.CompletedSlash, cardUseCausedDamage: attack.CardUseCausedDamage))
                    return true;
                PopFinishedCardUse(attack.ResolutionId);
                CompleteFinishedAttackCardUse(attack);
                return false;
            }
            SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.Completed);
            PopResolutionFrame(attack.ResolutionId, ResolutionFrameKind.CardUse);
            if (_winner != Winner.None)
            {
                if (_status != EngineStatus.Completed) CompleteGame();
            }
            return false;
        }

        var attackCard = RequireAttackCard(attack);
        var cardLocation = _cardZones.GetLocation(attackCard.Id);

        if (attack.IsDelayedJudgmentDamage)
        {
            if (cardLocation == CardLocation.Judgment(attack.DelayedJudgmentSeat ?? attack.SourceSeat))
            {
                MoveCard(
                    attackCard,
                    cardLocation,
                    CardLocation.DiscardPile,
                    CardMoveReasons.DelayedCardFinish);
            }
            else if (cardLocation != CardLocation.DiscardPile &&
                     !_cardMovements.Any(movement => movement.CardId == attackCard.Id &&
                         movement.From == CardLocation.Judgment(attack.DelayedJudgmentSeat ?? attack.SourceSeat) &&
                         movement.To == CardLocation.DiscardPile &&
                         movement.Reason == CardMoveReasons.DeathDiscard))
            {
                throw new InvalidOperationException(
                    $"A resolved Lightning left its judgment zone through an unsupported destination: {cardLocation}.");
            }

            if (_winner != Winner.None && _status != EngineStatus.Completed)
            {
                CompleteGame();
            }

            return false;
        }

        if (allowYingboGift &&
            TryBeginYingboGift(
                attack.ResolutionId,
                attack.CardUserSeat,
                attackCard,
                RequireAttackCardKind(attack),
                attack.PhysicalCards,
                YingboGiftContinuation.Attack,
                attack: attack))
        {
            return true;
        }

        foreach (var physicalCard in attack.PhysicalCards)
        {
            if (IsCurrentUsePhysicalCardClaim(attack.ResolutionId, physicalCard.Id)) continue;
            var physicalLocation = _cardZones.GetLocation(physicalCard.Id);
            if (physicalLocation == CardLocation.Processing)
            {
                MoveCard(
                    physicalCard,
                    CardLocation.Processing,
                    CardLocation.DiscardPile,
                    CardMoveReasons.UseFinished);
            }
            else if (physicalLocation.Zone is not (CardZoneKind.Hand or CardZoneKind.DrawPile or CardZoneKind.DiscardPile))
            {
                throw new InvalidOperationException(
                    $"A resolved {attack.EffectiveCardKind} physical card left Processing through an unsupported destination: {physicalLocation}.");
            }
        }

        var completedAction = _resolutionStack.OfType<CardUseFrame>()
            .Single(frame => frame.Id == attack.ResolutionId).Action;
        QueueCardUseFinishedWithoutPop(attack.ResolutionId, attackCard, RequireAttackCardKind(attack));
        if (_winner == Winner.None && completedAction is not null &&
            TryBeginProgramCardWindow(attack, completedAction,
                SkillProgramTriggerWindow.CardUseCompleted, completedAction.TargetSeats,
                ProgramCardContinuation.CompletedSlash,
                cardUseCausedDamage: attack.CardUseCausedDamage))
            return true;
        PopFinishedCardUse(attack.ResolutionId);
        CompleteFinishedAttackCardUse(attack);
        return false;
    }

    private void CompleteProgramFactionCardRequest(FactionCardRequestHandle pending)
    {
        if (!SameContinuationOwner(ActiveFactionCardRequest, pending) ||
            !pending.IsProgramSkillUse || pending.AwaitingProviders ||
            pending.ProgramSkillFrameId is not { } frameId ||
            _resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != frameId)
            throw new InvalidOperationException("The completed faction-card request lost its program parent.");
        ActiveFactionCardRequest = null;
    }

    private void CompleteFinishedAttackCardUse(CardAttackHandle attack)
    {
        if (ActiveFactionCardRequest is { IsProgramSkillUse: true, AwaitingProviders: false } jijiang &&
            SameAttackOwner(jijiang.ActiveAttack, attack))
        {
            CompleteProgramFactionCardRequest(jijiang);
        }
        else if (ActiveFactionCardRequest is { IsQinglongCrescentBladeUse: true, AwaitingProviders: false } qinglongFactionSlash &&
                 SameAttackOwner(qinglongFactionSlash.ActiveAttack, attack))
        {
            CompleteQinglongCrescentBladeFactionSlash(qinglongFactionSlash);
        }

        if (_winner != Winner.None &&
            _status != EngineStatus.Completed &&
            (ActiveBorrowedSword?.ActiveAttack is not { } borrowedAttack ||
             !SameAttackOwner(borrowedAttack, attack)))
        {
            CompleteGame();
        }
    }

    private void CompleteAttack(CardAttackHandle attack)
    {
        if (ActiveDying is not null)
        {
            throw new InvalidOperationException("A card cannot finish while its dying resolution is pending.");
        }


        if (attack.TryConsumeProgramDamageTransferFollowup(out var transferFollowup))
        {
            var target = _players[transferFollowup.TargetSeat];
            var drawCount = transferFollowup.DrawLostHp && attack.DamageWasApplied && target.IsAlive
                ? Math.Max(0, target.MaxHp - target.Hp) : 0;
            if (drawCount > 0)
                DrawCards(target, drawCount, log: true,
                    reason: new CardMoveReason("skill-program.damage-transfer.followup-draw"));
            AdvanceEventRulesAndQueueFact(new ProgramDamageTransferCardsDrawnEvent(
                attack.ResolutionId, transferFollowup.SkillId,
                transferFollowup.OwnerSeat, transferFollowup.TargetSeat, drawCount));
        }

        if (!attack.IsProgramJudgmentDamage &&
            ActiveGroupCard is { Effect: GroupCardEffect.ResponseAttack } group &&
            SameAttackOwner(group.CurrentAttack, attack))
        {
            group.CurrentAttack = null;
            ActiveCardAttack = null;
            ActiveDuel = null;
            _pendingDecision = null;
            group.TargetIndex++;
            SetCardUseTargetIndex(group.ResolutionId, group.TargetIndex);
            BeginGroupAttackResponse(group);
            return;
        }

        if (_winner == Winner.None &&
            attack.TryAdvanceChainedTarget(
                seat => _players[seat].IsAlive,
                UsesFormalTengjia || UsesFormalSilverLion,
                out var fromSeat))
        {
            AdvanceEventRulesAndQueueFact(new ChainedDamagePropagatedEvent(
                attack.ResolutionId,
                attack.SourceSeat,
                fromSeat,
                attack.TargetSeat,
                attack.DamageAmount,
                GetDamageNature(attack)));
            AddLog(
                "ChainDamage",
                $"连环伤害从 {_players[fromSeat].Name} 传导至 {_players[attack.TargetSeat].Name}。",
                attack.SourceSeat,
                attack.TargetSeat);
            if (!ApplyAttackDamage(attack))
            {
                CompleteAttack(attack);
            }

            return;
        }

        if (ActiveFangtianHalberd is { } fangtian &&
            SameAttackOwner(fangtian.CurrentAttack, attack))
        {
            fangtian.DamageWasApplied |= attack.DamageWasApplied;
            fangtian.CurrentAttack = null;
            if (fangtian.TargetIndex < fangtian.TargetSeats.Count) fangtian.TargetIndex++;
            while (fangtian.TargetIndex < fangtian.TargetSeats.Count &&
                   !_players[GetFangtianEffectiveTarget(fangtian, fangtian.TargetIndex)].IsAlive)
            {
                fangtian.TargetIndex++;
            }

            if (_winner == Winner.None && fangtian.TargetIndex < fangtian.TargetSeats.Count)
            {
                ActiveCardAttack = null;
                ActiveDuel = null;
                _pendingDecision = null;
                BeginNextFangtianHalberdTarget(fangtian);
                return;
            }

            SetCardUseTargetIndex(fangtian.ResolutionId, CompletedFangtianOriginalTargetIndex(fangtian));
            attack.SetCardUseCausedDamage(fangtian.DamageWasApplied);
            ActiveFangtianHalberd = null;
        }

        if (!attack.CardUseCausedDamage)
            attack.SetCardUseCausedDamage(attack.DamageWasApplied);

        if (TryContinueSequentialTrick(attack.ResolutionId, afterAttack: true)) return;
        if (TryContinuePaidVirtualSlashTargets(attack)) return;
        if (TryContinueEnhancedSlashTargets(attack)) return;
        if (LifecycleCardUse(attack.ResolutionId) is not null)
            UpdateLifecycleCardUse(attack.ResolutionId, frame => frame with { ProgramAdjustedSlashBaseDamage = null });

        var completion = CaptureAttackCompletion(attack);
        if (FinishAttack(attack))
        {
            return;
        }
        CompleteAttackAfterCardResolution(completion);
    }

    private void CompleteAttackAfterCardResolution(AttackCompletionReceipt completion)
    {
        if (RestoreContinuationAfterQinglongFollowup(completion.ResolutionId))
        {
            return;
        }
        var borrowedSword = ActiveBorrowedSword is { ActiveAttack: { } borrowedAttack } pendingBorrowedSword &&
                            borrowedAttack.ResolutionId == completion.ResolutionId
            ? pendingBorrowedSword
            : null;
        var resumesDelayedTurn = completion.DelayedTurn;
        var resumesProgramSkill = completion.ProgramFrameId is not null;
        if (completion.DelayedTurn) PopResolutionFrame(completion.ResolutionId, ResolutionFrameKind.Judgment);
        ActiveCardAttack = null;
        ActiveDuel = null;
        _pendingDecision = null;
        if (borrowedSword is not null)
        {
            CompleteBorrowedSwordAfterSlash(borrowedSword);
            return;
        }
        if (completion.ConditionalDiscardDuelReturn is not null)
        {
            CompleteConditionalDiscardDuelAttackReturn(completion); return;
        }
        if (completion.PindianWinnerSlashReturn is not null)
        {
            CompletePindianWinnerSlash(completion); return;
        }
        if (completion.ForeignTurnContestSlashReturn is not null)
        {
            CompleteForeignTurnContestSlash(completion); return;
        }
        if (completion.SharedSlashBenefit is not null)
        {
            CompleteSharedSlashBenefit(completion); return;
        }
        if (resumesProgramSkill && completion.ProgramFrameId is { } obtainedParent &&
            _resolutionStack.LastOrDefault() is ProgramSkillFrame obtainedFrame && obtainedFrame.Id == obtainedParent &&
            obtainedFrame.DamageTargetObtain is { Stage: ProgramDamageTargetObtainStage.DuelIssued })
        {
            if (completion.DamageTargetDuelReturn is not {AttackStarted:true} origin || !MatchesDamageTargetDuelParent(obtainedFrame,origin))
                throw new InvalidOperationException("True Duel completion lost its exact typed receipt return.");
            CompleteDamageTargetDuelReturn(obtainedFrame);
            return;
        }
        if (resumesProgramSkill && completion.ProgramFrameId is { } dualColorParent &&
            _resolutionStack.LastOrDefault() is ProgramSkillFrame dualColorFrame && dualColorFrame.Id == dualColorParent &&
            dualColorFrame.DualColorDuel is { Stage: ProgramDualColorDuelStage.DuelIssued })
        {
            CompleteDualColorDuelAttackReturn(dualColorFrame, completion);
            return;
        }
        if (resumesProgramSkill)
        {
            if (completion.ProgramFrameId is not { } frameId ||
                _resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != frameId)
                throw new InvalidOperationException("The program child attack lost its parent frame.");
            if (completion.RequestedSlashChild)
                ReplaceRuntimeFrame(frame.Id, frame with
                {
                    RequestedSlashDamagedOwner =
                        completion.DamageWasApplied && completion.FinalTargetSeat == frame.OwnerSeat
                });
            AdvanceRuntimeProgram(frameId);
        }
        else if (resumesDelayedTurn && _winner == Winner.None && _status != EngineStatus.Completed)
        {
            ResumeAfterLightningDamage(completion.DelayedTurnSeat!.Value);
        }
    }

    private void FinishGroupAttack(GroupCardHandle group, bool allowYingboGift = true)
    {
        if (!SameContinuationOwner(ActiveGroupCard, group) || group.Effect != GroupCardEffect.ResponseAttack)
        {
            throw new InvalidOperationException("The group attack is not the current card resolution.");
        }

        if (ActiveDying is not null || ActiveCardAttack is not null)
        {
            throw new InvalidOperationException(
                "A group card cannot finish while a target continuation is pending.");
        }

        if (allowYingboGift &&
            TryBeginYingboGift(
                group.ResolutionId,
                group.SourceSeat,
                group.Card,
                group.Card.Kind,
                group.PhysicalCards,
                YingboGiftContinuation.GroupAttack,
                group: group))
        {
            return;
        }

        foreach (var physicalCard in group.PhysicalCards)
        {
            var cardLocation = _cardZones.GetLocation(physicalCard.Id);
            if (cardLocation == CardLocation.Processing)
            {
                MoveCard(
                    physicalCard,
                    CardLocation.Processing,
                    CardLocation.DiscardPile,
                    CardMoveReasons.UseFinished);
            }
            else if (!IsResolvedGroupPhysicalCardDestinationAllowed(
                         cardLocation,
                         group.DamageClaimedPhysicalCardIds.Contains(physicalCard.Id),
                         group.TargetSeats))
            {
                throw new InvalidOperationException(
                    $"A resolved {group.Card.Kind} left Processing through an unsupported destination: {cardLocation}.");
            }
        }

        if (_players.FirstOrDefault(player => player.IsAlive &&
                HasCardPolicy(player, SkillProgramCardPolicyKind.ClaimResolvedGlobalCard,
                    group.Card.Kind)) is { } claimant &&
            group.SourceSeat != claimant.Seat)
        {
            var claimable = group.PhysicalCards.Where(card =>
                _cardZones.GetLocation(card.Id) == CardLocation.DiscardPile).ToArray();
            foreach (var card in claimable)
                MoveCard(card, CardLocation.DiscardPile, CardLocation.Hand(claimant.Seat), CardMoveReasons.JuxiangGain);
            if (claimable.Length > 0)
            {
                AdvanceEventRulesAndQueueFact(new JuxiangCardClaimedEvent(group.ResolutionId, claimant.Seat,
                    claimable.Select(card => card.Id).ToArray()));
                AddLog("SkillTriggered", $"{claimant.Name} 获得了结算完毕的【{group.Card.DisplayName}】。", claimant.Seat);
            }
        }

        SetCardUseTargetIndex(group.ResolutionId, group.TargetSeats.Count);
        ActiveGroupCard = null;
        ActiveCardAttack = null;
        ActiveDuel = null;
        _pendingDecision = null;
        FinishCardUse(group.ResolutionId, group.Card);

        if (_winner != Winner.None && _status != EngineStatus.Completed)
        {
            CompleteGame();
        }
    }

    private void FinishGroupRecovery(GroupCardHandle group)
    {
        if (!SameContinuationOwner(ActiveGroupCard, group) || group.Effect != GroupCardEffect.Recovery)
        {
            throw new InvalidOperationException("The group recovery is not the current card resolution.");
        }

        if (ActiveDying is not null || ActiveCardAttack is not null)
        {
            throw new InvalidOperationException(
                "A group recovery cannot finish while another continuation is pending.");
        }

        if (!SkipTieredRoundZeroFinishedMovement(group.ResolutionId, group.Card))
        {
            var cardLocation = _cardZones.GetLocation(group.Card.Id);
            if (cardLocation == CardLocation.Processing)
                MoveCard(group.Card, CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.UseFinished);
            else if (cardLocation != CardLocation.DiscardPile)
                throw new InvalidOperationException($"A resolved {group.Card.Kind} left Processing through an unsupported destination: {cardLocation}.");
        }

        SetCardUseTargetIndex(group.ResolutionId, group.TargetSeats.Count);
        ActiveGroupCard = null;
        ActiveCardAttack = null;
        ActiveDuel = null;
        _pendingDecision = null;
        FinishCardUse(group.ResolutionId, group.Card);

        if (_winner != Winner.None && _status != EngineStatus.Completed)
        {
            CompleteGame();
        }
    }

    private void ResolvePeach(CharacterState player, Card peach)
    {
        ResolvePeach(player, player, peach, allowDying: false);
    }

    private void ResolvePeach(
        CharacterState source,
        CharacterState target,
        Card peach,
        bool allowDying,
        CardConversionSource? conversionSource = null,
        DyingResponseEvent? dyingResponse = null)
    {
        if ((!allowDying && source.Hp >= source.MaxHp) ||
            (allowDying && target.Hp > 0) ||
            !target.IsAlive)
        {
            throw new InvalidOperationException("This Peach target cannot currently be recovered.");
        }


        var recoveryPolicies = allowDying && source.Seat != target.Seat
            ? CardPolicies(target, SkillProgramCardPolicyKind.RescueRecoveryBonus, CardKind.Peach)
                .Where(item => string.Equals(GetEffectiveFactionId(source), item.Policy.FactionId,
                    StringComparison.Ordinal)).ToArray()
            : [];
        ResolveRecoveryCard(
            source,
            target,
            peach,
            "桃",
            playedCardKind: CardKind.Peach,
            recoveryAmount: 1 + recoveryPolicies.Sum(item => item.Policy.Value),
            recoveryPolicySources: recoveryPolicies.Select(item => (item.Source.SkillId, item.Policy.Id)).ToArray(),
            conversionSource: conversionSource, dyingResponse: dyingResponse);
    }

    private void ResolveDyingAlcohol(
        CharacterState source,
        CharacterState target,
        Card alcohol,
        CardConversionSource? conversionSource = null,
        DyingResponseEvent? dyingResponse = null)
    {
        if ((source.Seat != target.Seat) ||
            target.Hp > 0 ||
            !target.IsAlive)
        {
            throw new InvalidOperationException("Alcohol can only rescue a living dying player.");
        }

        ResolveRecoveryCard(source, target, alcohol, "酒", playedCardKind: CardKind.Alcohol,
            conversionSource: conversionSource, dyingResponse: dyingResponse);
    }

    private void ResolveRecoveryCard(
        CharacterState source,
        CharacterState target,
        Card card,
        string cardName,
        CardKind? playedCardKind = null,
        int recoveryAmount = 1,
        IReadOnlyList<(string SkillId, string PolicyId)>? recoveryPolicySources = null,
        CardConversionSource? conversionSource = null,
        DyingResponseEvent? dyingResponse = null,
        IReadOnlyList<Card>? physicalCards = null)
    {
        if (TryBeginCardDeclaration(source, card, playedCardKind ?? card.Kind,
            PeekDeclarationConversion(source, card, playedCardKind ?? card.Kind, true, conversionSource),
            DeclarationReturn(CardDeclarationPurpose.Recovery, source.Seat, target: target.Seat, recovery: recoveryAmount,
                policies: recoveryPolicySources?.Select(policy => new ProgramRecoveryPolicySource(policy.SkillId, policy.PolicyId)).ToArray(), dying: dyingResponse), [target.Seat])) return;
        var resolutionId = BeginCardUse(
            card,
            source.Seat,
            [target.Seat],
            playedCardKind: playedCardKind,
            conversionSource: conversionSource, physicalCardIds: physicalCards?.Select(item => item.Id).ToArray());
        var useIndex = _resolutionStack.FindLastIndex(frame => frame.Id == resolutionId);
        ReplaceRuntimeFrame(_resolutionStack[useIndex].Id, ((CardUseFrame)_resolutionStack[useIndex]) with { DyingResponse = dyingResponse });
        foreach (var physical in physicalCards ?? [card])
            MoveCard(physical, FindOwnedCardLocation(source, physical), CardLocation.Processing, CardMoveReasons.Use);
        BeginSimpleCardUse(resolutionId, new(card.Id, SimpleCardUseEffect.Recovery, recoveryAmount,
            recoveryPolicySources?.Select(policy => new ProgramRecoveryPolicySource(policy.SkillId, policy.PolicyId)).ToArray()));
    }

    private void CompleteRecoveryCardUse(CharacterState source, CharacterState target, Card card,
        long resolutionId, CardKind playedCardKind, int recoveryAmount,
        IReadOnlyList<ProgramRecoveryPolicySource> recoveryPolicySources)
    {
        var cardName = CardCatalog.Get(playedCardKind).DisplayName;
        if (!target.IsAlive || IsCardEffectIneffective(resolutionId, target.Seat))
        {
            FinishCardUse(resolutionId, card, playedCardKind);
            return;
        }
        if (TryQueueRecoveryReplacement(resolutionId, source.Seat, target.Seat, recoveryAmount,
            new(RecoveryAttemptProducer.CardUse, CardId: card.Id, CardKind: playedCardKind, Policies: recoveryPolicySources)))
        { FinishCardUse(resolutionId, card, playedCardKind); return; }
        var recoveryFrameId = BeginRecovery(resolutionId, source.Seat, target.Seat, recoveryAmount);
        try
        {
            target.Hp = Math.Min(target.MaxHp, target.Hp + recoveryAmount);
            foreach (var policy in recoveryPolicySources ?? [])
            {
                AdvanceEventRulesAndQueueFact(new ProgramRecoveryPolicyAppliedEvent(
                    resolutionId,
                    target.Seat,
                    source.Seat,
                    card.Id,
                    recoveryAmount, policy.SkillId, policy.PolicyId));
            }
            AddLog(
                "Recovered",
                $"{source.Name} 使用【{cardName}】使 {target.Name} 回复 {recoveryAmount} 点体力，至 {target.Hp}/{target.MaxHp}。",
                source.Seat,
                target.Seat);
            AdvanceEventRulesAndQueueFact(new RecoveryAppliedEvent(
                source.Seat,
                target.Seat,
                recoveryAmount,
                target.Hp));
        }
        finally
        {
            PopResolutionFrame(recoveryFrameId, ResolutionFrameKind.Recovery);
        }

        var physicalIds = _resolutionStack.OfType<CardUseFrame>().Single(item => item.Id == resolutionId).PhysicalCardIds ?? [card.Id];
        foreach (var id in HasRemainingAdjustedSimpleTargets(resolutionId) ? [] : physicalIds)
            if (_cardZones.CardsAt(CardLocation.Processing).SingleOrDefault(item => item.Id == id) is { } physical)
                MoveCard(physical, CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.UseFinished);
        FinishCardUse(resolutionId, card, playedCardKind);
    }
    private long BeginCardUse(
        Card card,
        int sourceSeat,
        IReadOnlyList<int> targetSeats,
        CardKind? playedCardKind = null,
        bool ignoresArmor = false,
        IReadOnlyList<int>? physicalCardIds = null,
        CardConversionSource? conversionSource = null,
        IReadOnlyList<CardConversionSource>? additionalConversionSources = null,
        IReadOnlyList<int>? designatedTargetSeats = null, bool isTrueZhangbaSlash = false, ProgramSelectedActorDuelOrigin? selectedActorDuelOrigin = null, ProgramDamageTargetDuelOrigin? damageTargetDuelOrigin = null, ProgramDualColorDuelOrigin? dualColorDuelOrigin = null, ConditionalDiscardDuelOrigin? conditionalDiscardDuelOrigin = null)
    {
        var resolutionId = ++_resolutionSequence;
        var adjustedTargets = _selectedNextCardTargetSeats;
        _selectedNextCardTargetSeats = null;
        targetSeats = GetImplicitSelfCardUseTargets(playedCardKind ?? card.Kind, sourceSeat, targetSeats);
        var targets = Array.AsReadOnly((adjustedTargets ?? targetSeats).ToArray());
        if (adjustedTargets is not null)
        {
            if (_selectedRedAdditionalTargetSource is { } redSource)
            {
                _redAdditionalTargetGrants.Remove(sourceSeat);
                AdvanceEventRulesAndQueueFact(new RedAdditionalTargetsConsumedEvent(sourceSeat, redSource, adjustedTargets));
                _selectedRedAdditionalTargetSource = null;
            }
        }
        var designated = (Array.AsReadOnly((designatedTargetSeats ?? targets)
                .Where(seat => IsValidPlayerSeat(seat) && _players[seat].IsAlive)
                .Distinct().ToArray())
);
        var effectiveCardKind = playedCardKind ?? card.Kind;
        var physicalIds = Array.AsReadOnly((physicalCardIds ?? [card.Id]).ToArray());
        var actionContext = CaptureCardUseAction(
            card,
            sourceSeat,
            targets,
            effectiveCardKind,
            physicalIds,
            conversionSource,
            additionalConversionSources,
            designated, isTrueZhangbaSlash);
        if (selectedActorDuelOrigin is not null || damageTargetDuelOrigin is not null || dualColorDuelOrigin is not null || conditionalDiscardDuelOrigin is not null)
            actionContext = CaptureFactionAction(new CardActionContext(actionContext!.ActionId, actionContext.ParentActionId,
                CardActionType.Use, sourceSeat, sourceSeat, null, null, null, CardKind.Duel, targets, [], [], designated,
                effectiveSuit: Suit.None, effectiveRank: 0, effectiveIsRed: false));
        var colorFireAttack = FreezeColorFireAttackPolicy(sourceSeat, actionContext);
        var firstUseDistance = actionContext is not null && HasFirstActualPlayUseDistance(_players[sourceSeat]);
        if (actionContext is not null) RecordActualPlayPhaseUse(actionContext);
        RecordProgramUsedBasicCard(sourceSeat, effectiveCardKind);
        var declarationPayment = TransferDeclarationToCardUse(resolutionId);
        PushRuntimeFrame(new CardUseFrame(
            resolutionId,
            sourceSeat,
            card.Id,
            effectiveCardKind,
            targets,
            IgnoresArmor: ignoresArmor,
            PhysicalCardIds: physicalIds)
        { SelectedActorDuelOrigin = selectedActorDuelOrigin, DamageTargetDuelOrigin = damageTargetDuelOrigin, DualColorDuelOrigin = dualColorDuelOrigin, ConditionalDiscardDuelOrigin = conditionalDiscardDuelOrigin, Enhancements = actionContext is { Type: CardActionType.Use, EffectiveIsRed: true } && HasTurnRedSlashPolicyForColor(sourceSeat, effectiveCardKind, actionContext.EffectiveIsRed) ? CurrentCardEnhancement.Uncancelable : CurrentCardEnhancement.None, AcceptedDeclarationPayment = declarationPayment, Action = actionContext, TargetsAdjusted = adjustedTargets is not null, FirstOwnPlayUseDistanceUnlimited = firstUseDistance, ColorFireAttack = colorFireAttack });
        if (colorFireAttack is not null) AdvanceEventRulesAndQueueFact(new ColorFireAttackPolicyIssuedEvent(resolutionId, colorFireAttack.ActionId, colorFireAttack.Source));
        IssueTieredRoundConversionUse(LifecycleCardUse(resolutionId)!);
        IssueProvenanceUsePolicy(resolutionId, actionContext);
        IssueShownEntityUseBenefits(resolutionId, actionContext);
        IssueGrantedPhaseEntityUseDistance(resolutionId, actionContext);
        AdvanceEventRulesAndQueueFact(new CardUseDeclaredEvent(
            resolutionId,
            card.Id,
            effectiveCardKind,
            sourceSeat,
            IgnoresArmor: ignoresArmor));
        if (TracksPlayCardHistory && actionContext is not null)
            AdvanceEventRulesAndQueueFact(new CardUseAppearanceCapturedEvent(actionContext));
        AdvanceEventRulesAndQueueFact(new TargetsConfirmedEvent(resolutionId, targets));
        RecordYingboCardUse(resolutionId, sourceSeat, effectiveCardKind);
        return resolutionId;
    }

    private void PushResponseWindow(
        long parentFrameId,
        int sourceSeat,
        int responderSeat,
        CardKind incomingCard,
        CardKind requiredCardKind)
    {
        SetResponseParentStep(parentFrameId, ResolutionFrameStep.AwaitingResponse);
        PushRuntimeFrame(new ResponseWindowFrame(
            ++_resolutionSequence,
            parentFrameId,
            sourceSeat,
            responderSeat,
            incomingCard,
            RequiredCardKind: requiredCardKind));
    }

    private void SetResponseParentStep(long frameId, ResolutionFrameStep step)
    {
        var index = _resolutionStack.FindLastIndex(frame => frame.Id == frameId);
        if (index < 0 || _resolutionStack[index] is not
            (CardUseFrame or ProgramSkillFrame or JudgmentFrame or ProgramJudgmentTriggerWindowFrame))
        {
            throw new InvalidOperationException(
                $"Resolution frame {frameId} cannot own a response window.");
        }

        ReplaceRuntimeFrame(_resolutionStack[index].Id, _resolutionStack[index] with { Step = step });
    }

    private long BeginDamage(
        long parentFrameId,
        int sourceSeat,
        int targetSeat,
        int amount,
        DamageNature nature = DamageNature.Normal)
    {
        var frameId = ++_resolutionSequence;
        PushRuntimeFrame(new DamageFrame(
            frameId,
            parentFrameId,
            sourceSeat,
            targetSeat,
            amount,
            Nature: nature));
        CaptureDynamicDiscardDamageFrame(frameId, parentFrameId, targetSeat);
        return frameId;
    }

    private long BeginRecovery(
        long parentFrameId,
        int sourceSeat,
        int targetSeat,
        int amount)
    {
        var frameId = ++_resolutionSequence;
        PushRuntimeFrame(new RecoveryFrame(
            frameId,
            parentFrameId,
            sourceSeat,
            targetSeat,
            amount, HpBefore: _players[targetSeat].Hp));
        return frameId;
    }

    private long BeginDeath(long parentFrameId, int victimSeat, int? killerSeat, DeathReturnKind returnKind)
    {
        var frameId = ++_resolutionSequence;
        PushRuntimeFrame(new DeathFrame(frameId, parentFrameId, victimSeat, killerSeat)
        {
            ReturnKind = returnKind
        });
        return frameId;
    }

    private void SetDyingFrameStep(long frameId, ResolutionFrameStep step)
    {
        var index = _resolutionStack.FindLastIndex(frame => frame.Id == frameId);
        if (index < 0 || _resolutionStack[index] is not DyingFrame dying)
        {
            throw new InvalidOperationException($"Resolution frame {frameId} is not a Dying frame.");
        }

        ReplaceRuntimeFrame(_resolutionStack[index].Id, dying with
        {
            Step = step
        });
    }

    private DyingFrame UpdateDyingFrame(long frameId, Func<DyingFrame, DyingFrame> update)
    {
        var index = _resolutionStack.FindLastIndex(frame => frame.Id == frameId);
        if (index < 0 || _resolutionStack[index] is not DyingFrame dying)
            throw new InvalidOperationException($"Resolution frame {frameId} is not a Dying frame.");
        var next = update(dying);
        ReplaceRuntimeFrame(_resolutionStack[index].Id, next);
        return next;
    }

    private void SetDamageTriggerWindowStep(long frameId, ResolutionFrameStep step)
    {
        var index = _resolutionStack.FindLastIndex(frame => frame.Id == frameId);
        if (index < 0 || _resolutionStack[index] is not DamageTriggerWindowFrame triggerWindow)
        {
            throw new InvalidOperationException($"Resolution frame {frameId} is not a damage trigger window.");
        }

        ReplaceRuntimeFrame(_resolutionStack[index].Id, triggerWindow with { Step = step });
    }

    private DamageTriggerWindowFrame SetDamageTriggerWindowCursor(long frameId, int candidateIndex)
    {
        var index = _resolutionStack.FindLastIndex(frame => frame.Id == frameId);
        if (index < 0 || _resolutionStack[index] is not DamageTriggerWindowFrame triggerWindow)
        {
            throw new InvalidOperationException($"Resolution frame {frameId} is not a damage trigger window.");
        }

        if (candidateIndex < 0 || candidateIndex > triggerWindow.Candidates.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(candidateIndex));
        }

        var next = triggerWindow with { CandidateIndex = candidateIndex };
        ReplaceRuntimeFrame(_resolutionStack[index].Id, next);
        return next;
    }

    private void PopResponseWindow(long parentFrameId)
    {
        if (_resolutionStack.Count == 0 ||
            _resolutionStack[^1] is not ResponseWindowFrame response ||
            response.ParentFrameId != parentFrameId)
        {
            throw new InvalidOperationException(
                $"Resolution stack does not have a response window for frame {parentFrameId}.");
        }

        PopResolutionFrame(response.Id, ResolutionFrameKind.ResponseWindow);
    }

    private void SetCardUseStep(long frameId, ResolutionFrameStep step)
    {
        var index = _resolutionStack.FindLastIndex(frame => frame.Id == frameId);
        if (index < 0 || _resolutionStack[index] is not CardUseFrame cardUse)
        {
            throw new InvalidOperationException($"Resolution frame {frameId} is not a CardUse frame.");
        }

        ReplaceRuntimeFrame(_resolutionStack[index].Id, cardUse with { Step = step });
    }

    private void RedirectCardUseTarget(long frameId, int originalTargetSeat, int redirectedTargetSeat)
    {
        var index = _resolutionStack.FindLastIndex(frame => frame.Id == frameId);
        if (index < 0 || _resolutionStack[index] is not CardUseFrame cardUse)
        {
            throw new InvalidOperationException($"Resolution frame {frameId} is not a CardUse frame.");
        }

        var targetSeats = cardUse.TargetSeats.ToArray();
        var targetIndex = Array.IndexOf(targetSeats, originalTargetSeat);
        if (targetIndex < 0)
        {
            throw new InvalidOperationException("The redirected Slash target is absent from its CardUse frame.");
        }

        targetSeats[targetIndex] = redirectedTargetSeat;
        ReplaceRuntimeFrame(_resolutionStack[index].Id, cardUse with { TargetSeats = targetSeats });
    }

    private NullificationWindowFrame ReplaceNullificationWindowFrame(NullificationWindowFrame next)
    {
        var index = _resolutionStack.FindLastIndex(frame => frame.Id == next.Id);
        if (index < 0 || _resolutionStack[index] is not NullificationWindowFrame)
        {
            throw new InvalidOperationException(
                $"Resolution frame {next.Id} is not a Nullification window.");
        }

        ReplaceRuntimeFrame(_resolutionStack[index].Id, next);
        return next;
    }

    private static int? GetNullificationTargetSeat(NullificationWindowFrame frame) =>
        frame.TargetSeats.Count == 1 ? frame.TargetSeats[0] : null;

    private Card GetNullificationEffectCard(NullificationWindowFrame frame) =>
        GetTrickRepresentation(frame.ParentFrameId, frame.EffectCardId);

    private void SetCardUseTargetIndex(long frameId, int targetIndex)
    {
        var index = _resolutionStack.FindLastIndex(frame => frame.Id == frameId);
        if (index < 0 || _resolutionStack[index] is not CardUseFrame cardUse)
        {
            throw new InvalidOperationException($"Resolution frame {frameId} is not a CardUse frame.");
        }

        if (targetIndex < 0 || targetIndex > cardUse.TargetSeats.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(targetIndex));
        }

        ReplaceRuntimeFrame(_resolutionStack[index].Id, cardUse with { TargetIndex = targetIndex });
    }

    private void FinishCardUse(
        long frameId,
        Card card,
        CardKind? playedCardKind = null)
    {
        if (TryBeginCharacterStateProgramWindow(frameId, CharacterStateContinuation.CardUse, card.Id, playedCardKind)) return;
        if (TryBeginHpChangedProgramWindow(frameId, PostEventContinuation.CardUse, card.Id, playedCardKind)) return;
        if (TryContinueSequentialTrick(frameId)) return;
        if (TryContinueAdjustedSimpleCardUse(frameId)) return;
        if (TryContinueAdjustedBorrowedSwordUse(frameId)) return;
        if (_resolutionStack.OfType<CardUseFrame>().SingleOrDefault(frame => frame.Id == frameId) is { } pendingUse)
        {
            foreach (var physicalCardId in pendingUse.PhysicalCardIds ?? [pendingUse.CardId])
            {
                if (IsCurrentUsePhysicalCardClaim(frameId, physicalCardId)) continue;
                if (_cardZones.GetLocation(physicalCardId) == CardLocation.Processing)
                {
                    var physicalCard = _cardZones.CardsAt(CardLocation.Processing)
                        .Single(candidate => candidate.Id == physicalCardId);
                    MoveCard(
                        physicalCard,
                        CardLocation.Processing,
                        CardLocation.DiscardPile,
                        CardMoveReasons.UseFinished);
                }
            }
        }
        QueueCardUseFinishedWithoutPop(frameId, card, playedCardKind);
        var completedUse = _resolutionStack.OfType<CardUseFrame>().Single(frame => frame.Id == frameId);
        if (_winner == Winner.None && completedUse.Action is { } action &&
            TryBeginProgramCardWindow(null, action, SkillProgramTriggerWindow.CardUseCompleted,
                action.TargetSeats, ProgramCardContinuation.CompletedCard,
                cardUseCausedDamage: completedUse.CausedDamage)) return;
        PopFinishedCardUse(frameId);
    }

    private void QueueCardUseFinishedWithoutPop(long frameId, Card card, CardKind? playedCardKind)
    {
        SetCardUseStep(frameId, ResolutionFrameStep.Completed);
        if (_resolutionStack.Count == 0 ||
            _resolutionStack[^1] is not CardUseFrame cardUse ||
            cardUse.Id != frameId)
        {
            throw new InvalidOperationException(
                $"Resolution frame {frameId} has unfinished child frames.");
        }

        AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(
            frameId,
            card.Id,
            playedCardKind ?? card.Kind));
    }

    private void PopFinishedCardUse(long frameId)
    {
        var completedUse = _resolutionStack.OfType<CardUseFrame>().Single(frame => frame.Id == frameId);
        var hpLossMaterialReturn = HasExactHpLossMaterialReturn(completedUse);
        var basicReturn = completedUse.VirtualBasicReturn;
        var dyingResponse = completedUse.DyingResponse;
        PopResolutionFrame(frameId, ResolutionFrameKind.CardUse);
        ContinueProgramAfterDiamondDelayedUse(frameId);
        ContinueProgramAfterPileEquipmentUse(frameId);
        ContinueProgramAfterRandomEquipmentUse();
        if (!HasExactNextActualUseAdjustedProgramReturn(completedUse) && !hpLossMaterialReturn)
            ContinueProgramAfterSelectedCardUse();
        if (dyingResponse is not null) CompleteDyingCardResponse(dyingResponse);
        ReturnVirtualBasicUse(basicReturn, frameId);
        ReturnSelectedActorDuel(completedUse);
        ReturnDamageTargetDuel(completedUse);
        ReturnDualColorDuel(completedUse);
        ReturnConditionalDiscardDuel(completedUse);
        ReturnRoundPileAlcoholUse(completedUse);
    }

    private bool CanUseSlashTarget(
        CharacterState source,
        CharacterState target,
        Card slashCard,
        CardConversionSource? conversionSource = null,
        CardKind effectiveKind = CardKind.Slash,
        bool ignoreDistance = false, long? existingUseFrameId=null, bool noEffectiveRank = false, int? specificEffectiveRank = null, IReadOnlyList<int>? physicalCardIds = null) =>
        !IsCardUseForbidden(source.Seat, effectiveKind, CardActionType.Use, ignoreIssuedPlayBan: existingUseFrameId is {} id && _resolutionStack.OfType<CardUseFrame>().Any(f=>f.Id==id&&f.Action?.ActorSeat==source.Seat)) &&
        target.IsAlive &&
        target.Seat != source.Seat &&
        (ignoreDistance || (HasIssuedProvenanceUseDistance(existingUseFrameId, source.Seat) || HasIssuedGrantedPhaseEntityDistance(existingUseFrameId, source.Seat)) || (HasProvenanceUseDistance(source, physicalCardIds ?? (slashCard.Id > 0 ? new[] { slashCard.Id } : [])) || HasGrantedPhaseEntityDistance(source, physicalCardIds ?? (slashCard.Id > 0 ? new[] { slashCard.Id } : []))) || HasSlashUseDistanceBySuit(source,effectiveKind,EffectiveSuit(source,slashCard)) || HasTurnRedSlashPolicy(source.Seat, effectiveKind, EffectiveSuit(source,slashCard)) || HasPhaseSuitAllowance(source,slashCard) || HasCardDistanceExemption(source, target, effectiveKind) ||
         IgnoresProgramSlashDistance(source, conversionSource) ||
         IgnoresSpGuanYuWushengDistance(source, slashCard) ||
         HasUnlimitedTurnRuleModifier(source.Seat, SkillRuleQuery.SlashDistanceLimit) ||
         IsWithinSpecificSlashRange(source, target, effectiveKind, specificEffectiveRank ?? (noEffectiveRank ? null : SpecificSlashRank(source, slashCard, effectiveKind, existingUseFrameId)))) &&
        !IsDirectedCardTargetProhibited(source.Seat, target.Seat, effectiveKind) &&
        !IsSlashProhibited(source, target, slashCard);

    private bool CanUseVirtualSlashTarget(
        CharacterState source,
        CharacterState target,
        CardKind effectiveKind = CardKind.Slash, Suit? physicalSuit = null, bool? effectiveColor = null, int? effectiveRank = null,
        IReadOnlyList<int>? physicalCardIds = null, bool ignoreDistance = false) =>        !IsCardUseForbidden(source.Seat, effectiveKind, CardActionType.Use) &&
        target.IsAlive &&
        target.Seat != source.Seat &&
        CanSpendSlashUse(source, target, ignoresCount: HasPhaseSuitAllowance(source.Seat,physicalSuit), effectiveKind) &&
        (ignoreDistance || (HasProvenanceUseDistance(source, physicalCardIds) || HasGrantedPhaseEntityDistance(source, physicalCardIds)) || HasSlashUseDistanceBySuit(source,effectiveKind,physicalSuit) || HasTurnRedSlashPolicyForColor(source.Seat, effectiveKind, effectiveColor ?? SuitColor(physicalSuit)) || HasPhaseSuitAllowance(source.Seat,physicalSuit) || HasCardDistanceExemption(source, target, effectiveKind) ||
         HasUnlimitedTurnRuleModifier(source.Seat, SkillRuleQuery.SlashDistanceLimit) ||
         IsWithinSpecificSlashRange(source, target, effectiveKind, effectiveRank)) &&
        !IsSlashProhibited(target);

    private bool CanUseZhangbaSerpentSpear(CharacterState actor) =>
        SlashKinds.Any(kind => !IsCardUseForbidden(actor.Seat, kind, CardActionType.Use)) &&
        UsesFormalZhangbaSerpentSpear &&
        HasWeaponAbility(actor, CardKind.ZhangbaSerpentSpear) &&
        GetPlayableCards(actor).Count(card => !IsTurnHandCardRestricted(actor, card)) >= 2;

    private bool HasZhuqueFan(CharacterState actor) =>
        UsesFormalZhuqueFan &&
        HasWeaponAbility(actor, CardKind.ZhuqueFan);

    private IReadOnlyList<CardKind> GetSlashUseKinds(
        CharacterState actor,
        CardKind baseEffectiveKind) =>
        GetSlashUseVariants(actor, baseEffectiveKind)
            .Select(variant => variant.EffectiveKind)
            .Distinct()
            .ToArray();

    private bool IsZhuqueFanConversion(
        CharacterState actor,
        CardKind baseEffectiveKind,
        CardKind requestedEffectiveKind) =>
        baseEffectiveKind == CardKind.Slash &&
        requestedEffectiveKind == CardKind.FireSlash &&
        HasZhuqueFan(actor);

    private IReadOnlyList<LegalAction> BuildNationalRevealActions(CharacterState actor)
    {
        var actions = new List<LegalAction>();
        if (!IsNationalWarMode || !actor.IsAlive)
        {
            return actions;
        }

        if (actor.GeneralSelected && !actor.GeneralRevealed)
        {
            actions.Add(new LegalAction(
                LegalActionKind.RevealGeneral,
                null,
                null,
                $"明置【{actor.General.Name}】")
            {
                GeneralSlot = GeneralSelectionSlot.Primary
            });
        }

        if (actor.SecondaryGeneralSelected &&
            !actor.SecondaryGeneralRevealed &&
            actor.SecondaryGeneral is not null)
        {
            actions.Add(new LegalAction(
                LegalActionKind.RevealGeneral,
                null,
                null,
                $"明置【{actor.SecondaryGeneral.Name}】")
            {
                GeneralSlot = GeneralSelectionSlot.Secondary
            });
        }

        return actions;
    }

    private IReadOnlyList<LegalAction> BuildLegalActions(CharacterState actor, bool includeProgramActions = true)
    {
        var actions = new List<LegalAction>();
        if (!actor.IsAlive || _phase != TurnPhase.Play || actor.Seat != _currentSeat)
        {
            return actions;
        }

        actions.AddRange(TieredRoundZeroPlayActions(actor));
        if (IsNationalWarMode)
        {
            actions.AddRange(BuildNationalRevealActions(actor));
        }

        var allPhysicalPlayableCards = GetPlayableCards(actor);
        var physicalPlayableCards = allPhysicalPlayableCards
            .Where(card => !IsTurnHandCardRestricted(actor, card))
            .ToArray();
        var playableCards = physicalPlayableCards
            .Where(card => !HasProgramCardIdentity(actor, card))
            .ToArray();
        var slashLimit = GetSlashUseLimit(actor);
        if (SlashKinds.Any(kind => !IsCardUseForbidden(actor.Seat, kind, CardActionType.Use)) &&
            (_slashCountThisTurn < slashLimit || HasSlashAllowanceForAnyTarget(actor) || physicalPlayableCards.Any(card =>
                GetProgramCardIdentityMatches(actor, card).Any(match => IgnoresProgramSlashLimit(actor, match.Source)))))
        {
            foreach (var transformed in physicalPlayableCards
                         .SelectMany(card => GetProgramCardIdentityMatches(actor, card)
                             .Where(match => IsSlashCard(match.Identity.OutputKind))
                             .Select(match => (Card: card, Match: match))))
            {
                var card = transformed.Card;
                var conversionSource = transformed.Match.Source;
                foreach (var variant in GetSlashUseVariants(
                             actor, card, transformed.Match.Identity.OutputKind, conversionSource))
                {
                    var effectiveKind = variant.EffectiveKind;
                    IReadOnlyList<CardConversionSource>? additionalConversions =
                        variant.AdditionalConversionSource is { } additional ? [additional] : null;
                    var targets = GetFangtianOrderedSlashTargets(
                        actor, card, conversionSource, effectiveKind: effectiveKind);
                    var physicalName = CardCatalog.Get(card.Kind).DisplayName;
                    var effectiveName = CardCatalog.Get(effectiveKind).DisplayName;
                    foreach (var target in targets)
                    {
                        actions.Add(new LegalAction(
                            LegalActionKind.Slash,
                            card.Id,
                            target.Seat,
                            DescribeConversion(conversionSource,
                                variant.AdditionalConversionSource is { } chained
                                    ? $"将【{physicalName}】视为【杀】，再发动【{DescribeAdditionalConversion(chained)}】改为【火杀】对 {target.Name} 使用"
                                    : variant.UsesZhuqueFan
                                    ? $"将【{physicalName}】视为【杀】，再以【朱雀羽扇】改为【火杀】对 {target.Name} 使用"
                                    : $"将【{physicalName}】视为【{effectiveName}】对 {target.Name} 使用"),
                            PlayedCardKind: effectiveKind)
                        {
                            ConversionSource = conversionSource,
                            AdditionalConversionSources = additionalConversions
                        });
                    }

                    AddFangtianHalberdSlashActions(
                        actions, actor, card, targets, effectiveName, effectiveKind, conversionSource,
                        additionalConversions);
                    AddProgramTargetCountSlashActions(
                        actions, actor, card, targets, effectiveName, effectiveKind, conversionSource,
                        additionalConversions);
                }
            }

            foreach (var slash in playableCards.Where(card => IsSlashCard(card.Kind)))
            {
                foreach (var variant in GetSlashUseVariants(actor, slash.Kind))
                {
                    var effectiveKind = variant.EffectiveKind;
                    var targets = GetFangtianOrderedSlashTargets(actor, slash, effectiveKind: effectiveKind);
                    var slashName = CardCatalog.Get(effectiveKind).DisplayName;
                    CardKind? playedCardKind = effectiveKind == slash.Kind
                        ? null
                        : effectiveKind;
                    foreach (var target in targets)
                    {
                        actions.Add(new LegalAction(
                            LegalActionKind.Slash,
                            slash.Id,
                            target.Seat,
                            variant.UsesZhuqueFan
                                ? $"发动【朱雀羽扇】，将【杀】改为【火杀】对 {target.Name} 使用"
                                : $"对 {target.Name} 使用【{slashName}】",
                            PlayedCardKind: playedCardKind));
                    }

                    AddFangtianHalberdSlashActions(
                        actions,
                        actor,
                        slash,
                        targets,
                        slashName,
                        playedCardKind);
                    AddProgramTargetCountSlashActions(
                        actions, actor, slash, targets, slashName, playedCardKind);
                }
            }

            foreach (var slash in playableCards.Where(card => card.Kind == CardKind.Slash))
                foreach (var conversionSource in GetProgramViewAsConversions(
                             actor, slash, CardKind.FireSlash, forResponse: false))
                {
                    var targets = GetFangtianOrderedSlashTargets(
                        actor, slash, conversionSource, effectiveKind: CardKind.FireSlash);
                    foreach (var target in targets)
                    {
                        actions.Add(new LegalAction(
                            LegalActionKind.Slash,
                            slash.Id,
                            target.Seat,
                            DescribeConversion(conversionSource,
                                $"将【杀】改为【火杀】对 {target.Name} 使用"),
                            PlayedCardKind: CardKind.FireSlash)
                        {
                            ConversionSource = conversionSource
                        });
                    }
                    AddFangtianHalberdSlashActions(actions, actor, slash, targets,
                        "火杀", CardKind.FireSlash, conversionSource);
                    AddProgramTargetCountSlashActions(actions, actor, slash, targets,
                        "火杀", CardKind.FireSlash, conversionSource);
                }

            foreach (var converted in GetHand(actor).Concat(GetEquipment(actor)).Where(card => card.Kind != CardKind.Slash))
                foreach (var conversion in GetProgramViewAsConversions(actor, converted, CardKind.FireSlash, false))
                    foreach (var target in GetFangtianOrderedSlashTargets(actor, converted, conversion, effectiveKind: CardKind.FireSlash))
                        actions.Add(new LegalAction(LegalActionKind.Slash, converted.Id, target.Seat,
                            DescribeConversion(conversion, $"当作【火杀】对 {target.Name} 使用"), PlayedCardKind: CardKind.FireSlash) { ConversionSource = conversion });
            AddCurrentNonFireConvertedTargetActions(actions, actor);
            foreach (var converted in playableCards)
            {
                var programSources = GetProgramViewAsConversions(
                    actor, converted, CardKind.Slash, forResponse: false);
                if (programSources.Count == 0) continue;
                var physicalName = CardCatalog.Get(converted.Kind).DisplayName;
                var sources = new List<CardConversionSource?>();
                sources.AddRange(programSources);
                foreach (var conversionSource in sources)
                {
                    foreach (var variant in GetSlashUseVariants(
                                 actor, converted, CardKind.Slash, conversionSource))
                    {
                        IReadOnlyList<CardConversionSource>? additionalConversions =
                            variant.AdditionalConversionSource is { } additional ? [additional] : null;
                        var targets = GetFangtianOrderedSlashTargets(
                            actor, converted, conversionSource, effectiveKind: variant.EffectiveKind);
                        var effectiveName = CardCatalog.Get(variant.EffectiveKind).DisplayName;
                        foreach (var target in targets)
                        {
                            var description = variant.AdditionalConversionSource is { } chained
                                ? $"将【{physicalName}】当作【杀】，再发动【{DescribeAdditionalConversion(chained)}】改为【火杀】对 {target.Name} 使用"
                                : variant.UsesZhuqueFan
                                    ? $"将【{physicalName}】当作【杀】，再以【朱雀羽扇】改为【火杀】对 {target.Name} 使用"
                                    : $"将【{physicalName}】当作【杀】对 {target.Name} 使用";
                            actions.Add(new LegalAction(
                                LegalActionKind.Slash,
                                converted.Id,
                                target.Seat,
                                DescribeConversion(conversionSource, description),
                                PlayedCardKind: variant.EffectiveKind)
                            {
                                ConversionSource = conversionSource,
                                AdditionalConversionSources = additionalConversions
                            });
                        }

                        AddFangtianHalberdSlashActions(
                            actions, actor, converted, targets, effectiveName,
                            variant.EffectiveKind, conversionSource, additionalConversions);
                        AddProgramTargetCountSlashActions(
                            actions, actor, converted, targets, effectiveName,
                            variant.EffectiveKind, conversionSource, additionalConversions);
                    }
                }
            }

            {
                foreach (var converted in GetEquipment(actor).Where(card =>
                             GetProgramViewAsConversions(actor, card, CardKind.Slash,
                                 forResponse: false).Count != 0))
                {
                    var physicalName = CardCatalog.Get(converted.Kind).DisplayName;
                    var sources = GetProgramViewAsConversions(
                        actor, converted, CardKind.Slash, forResponse: false);
                    foreach (var conversionSource in sources)
                    {
                        foreach (var variant in GetSlashUseVariants(
                                     actor, converted, CardKind.Slash, conversionSource))
                        {
                            IReadOnlyList<CardConversionSource>? additionalConversions =
                                variant.AdditionalConversionSource is { } additional ? [additional] : null;
                            var targets = GetFangtianOrderedSlashTargets(
                                actor, converted, conversionSource, effectiveKind: variant.EffectiveKind);
                            var effectiveName = CardCatalog.Get(variant.EffectiveKind).DisplayName;
                            foreach (var target in targets)
                            {
                                var description = variant.AdditionalConversionSource is { } chained
                                    ? $"将装备区【{physicalName}】当作【杀】，再发动【{DescribeAdditionalConversion(chained)}】改为【火杀】对 {target.Name} 使用"
                                    : variant.UsesZhuqueFan
                                        ? $"将装备区【{physicalName}】当作【杀】，再以【朱雀羽扇】改为【火杀】对 {target.Name} 使用"
                                        : $"将装备区【{physicalName}】当作【杀】对 {target.Name} 使用";
                                actions.Add(new LegalAction(
                                    LegalActionKind.Slash,
                                    converted.Id,
                                    target.Seat,
                                    DescribeConversion(conversionSource, description),
                                    PlayedCardKind: variant.EffectiveKind)
                                {
                                    ConversionSource = conversionSource,
                                    AdditionalConversionSources = additionalConversions
                                });
                            }
                            AddProgramTargetCountSlashActions(
                                actions, actor, converted, targets, effectiveName,
                                variant.EffectiveKind, conversionSource, additionalConversions);
                        }
                    }
                }
            }

            if (CanUseZhangbaSerpentSpear(actor))
            {
                var targets = SlashKinds
                    .SelectMany(kind => _players.Where(target => GetZhangbaSlashPairs(actor).Any(pair => CanUseVirtualSlashTarget(actor, target, kind, PhysicalGroupSuit(actor,pair), PhysicalGroupColor(actor,pair), ZhangbaSpecificSlashRank(actor,pair), pair.Select(c=>c.Id).ToArray()))))
                    .Select(target => target.Seat)
                    .Distinct()
                    .Order()
                    .ToArray();
                if (targets.Length > 0)
                {
                    actions.Add(new LegalAction(
                        LegalActionKind.UseEquipmentEffect,
                        null,
                        null,
                        "发动【丈八蛇矛】，将两张手牌当【杀】使用",
                        MinCardCount: 2,
                        MaxCardCount: 2,
                        MinTargetCount: 1,
                        MaxTargetCount: 1,
                        EquipmentKind: CardKind.ZhangbaSerpentSpear)
                    {
                        SelectableCardIds = physicalPlayableCards.Select(card => card.Id).Order().ToArray(),
                        SelectableTargetSeats = targets
                    });
                }
            }
        }
        else if (SlashKinds.Any(kind => !IsCardUseForbidden(actor.Seat, kind, CardActionType.Use)))
        {
            AddNuzhanUnlimitedTrickSlashActions(actions, actor, playableCards);
        }

        if (actor.Hp < actor.MaxHp)
        {
            foreach (var converted in GetHand(actor).Concat(GetEquipment(actor)))
                foreach (var conversion in GetProgramViewAsConversions(actor, converted, CardKind.Peach, false))
                    actions.Add(new LegalAction(LegalActionKind.Peach, converted.Id, actor.Seat,
                        DescribeConversion(conversion, "当作【桃】对自己使用"), PlayedCardKind: CardKind.Peach) { ConversionSource = conversion });
            foreach (var peach in playableCards.Where(card => card.Kind == CardKind.Peach))
            {
                actions.Add(new LegalAction(
                    LegalActionKind.Peach,
                    peach.Id,
                    actor.Seat,
                    "对自己使用【桃】"));
            }
        }

        foreach (var duel in playableCards.Where(card => card.Kind == CardKind.Duel))
        {
            foreach (var target in _players.Where(player =>
                         player.IsAlive &&
                         player.Seat != actor.Seat &&
                         !IsCardTargetProhibited(player, CardKind.Duel, duel.Suit, SuitColor(EffectiveSuit(actor, duel)))))
            {
                actions.Add(new LegalAction(
                    LegalActionKind.Duel,
                    duel.Id,
                    target.Seat,
                    $"对 {target.Name} 使用【决斗】"));
            }
        }

        foreach (var converted in playableCards.Where(card => card.Kind != CardKind.Duel))
        {
            foreach (var conversionSource in GetProgramViewAsConversions(
                         actor, converted, CardKind.Duel, forResponse: false))
            {
                foreach (var target in _players.Where(player =>
                             player.IsAlive &&
                             player.Seat != actor.Seat &&
                             !IsCardTargetProhibited(player, CardKind.Duel, converted.Suit, SuitColor(EffectiveSuit(actor, ApplyProgramUseAppearance(actor, converted, conversionSource))))))
                {
                    actions.Add(new LegalAction(
                        LegalActionKind.Duel,
                        converted.Id,
                        target.Seat,
                        DescribeConversion(conversionSource,
                            $"将【{converted.DisplayName}】当作【决斗】对 {target.Name} 使用"),
                        PlayedCardKind: CardKind.Duel)
                    {
                        ConversionSource = conversionSource
                    });
                }
            }
        }

        foreach (var drawTwo in playableCards.Where(card => card.Kind == CardKind.DrawTwo))
        {
            actions.Add(new LegalAction(
                LegalActionKind.DrawTwo,
                drawTwo.Id,
                null,
                "使用【无中生有】摸两张牌"));
        }

        foreach (var assault in playableCards.Where(card => card.Kind == CardKind.BarbarianAssault))
        {
            if (CanUseGlobalCard(actor, CardKind.BarbarianAssault))
                actions.Add(new LegalAction(
                    LegalActionKind.BarbarianAssault,
                    assault.Id,
                    null,
                    "使用【南蛮入侵】"));
        }

        foreach (var arrowBarrage in playableCards.Where(card => card.Kind == CardKind.ArrowBarrage))
        {
            if (CanUseGlobalCard(actor, CardKind.ArrowBarrage))
                actions.Add(new LegalAction(
                    LegalActionKind.ArrowBarrage,
                    arrowBarrage.Id,
                    null,
                    "使用【万箭齐发】"));
        }

        foreach (var peachGarden in playableCards.Where(card => card.Kind == CardKind.PeachGarden))
        {
            if (CanUseGlobalCard(actor, CardKind.PeachGarden))
                actions.Add(new LegalAction(
                    LegalActionKind.PeachGarden,
                    peachGarden.Id,
                    null,
                    "使用【桃园结义】"));
        }

        foreach (var fiveGrains in playableCards.Where(card => card.Kind == CardKind.FiveGrains))
        {
            if (CanUseGlobalCard(actor, CardKind.FiveGrains))
                actions.Add(new LegalAction(
                    LegalActionKind.FiveGrains,
                    fiveGrains.Id,
                    null,
                    "使用【五谷丰登】"));
        }

        foreach (var indulgence in playableCards.Where(card => card.Kind == CardKind.Indulgence))
        {
            foreach (var target in _players.Where(player =>
                         player.IsAlive &&
                         player.Seat != actor.Seat &&
                         !player.JudgmentAreaAbolished &&
                         !HasJudgmentEffectiveCard(player, CardKind.Indulgence)))
            {
                actions.Add(new LegalAction(
                    LegalActionKind.Indulgence,
                    indulgence.Id,
                    target.Seat,
                    $"对 {target.Name} 使用【乐不思蜀】"));
            }
        }

        {
            foreach (var converted in GetHand(actor)
                         .Concat(GetEquipment(actor))
                         .Where(card => !IsTurnHandCardRestricted(actor, card))
                         .Where(card => !HasProgramCardIdentity(actor, card))
                         .Where(card => GetProgramViewAsConversions(actor, card,
                             CardKind.Indulgence, forResponse: false).Count != 0))
            {
                var physicalName = CardCatalog.Get(converted.Kind).DisplayName;
                foreach (var conversionSource in GetProgramViewAsConversions(
                             actor, converted, CardKind.Indulgence, forResponse: false))
                    foreach (var target in _players.Where(player =>
                                 player.IsAlive &&
                                 player.Seat != actor.Seat &&
                                 !player.JudgmentAreaAbolished &&
                                 !HasJudgmentEffectiveCard(player, CardKind.Indulgence)))
                    {
                        actions.Add(new LegalAction(
                            LegalActionKind.Indulgence,
                            converted.Id,
                            target.Seat,
                            DescribeConversion(conversionSource,
                                $"将【{physicalName}】当作【乐不思蜀】对 {target.Name} 使用"),
                            PlayedCardKind: CardKind.Indulgence)
                        { ConversionSource = conversionSource });
                    }
            }
        }

        foreach (var supplyShortage in playableCards.Where(card => card.Kind == CardKind.SupplyShortage))
        {
            var ignoresDistance = HasCardPolicy(actor, SkillProgramCardPolicyKind.IgnoreUseDistance,
                CardKind.SupplyShortage) || HasProvenanceUseDistance(actor, supplyShortage);
            var distanceLimit = GetCardUseDistanceLimit(actor, CardKind.SupplyShortage);
            foreach (var target in _players.Where(player =>
                         player.IsAlive &&
                         player.Seat != actor.Seat &&
                         !player.JudgmentAreaAbolished &&
                         ((HasPhaseSuitAllowance(actor,supplyShortage) || HasCardDistanceExemption(actor, player, CardKind.SupplyShortage) ||
                               ignoresDistance ||
                               GetCombatDistance(actor.Seat, player.Seat) <= distanceLimit
)) &&
                         !IsDirectedCardTargetProhibited(actor.Seat, player.Seat, CardKind.SupplyShortage) &&
                         !HasJudgmentEffectiveCard(player, CardKind.SupplyShortage)))
            {
                actions.Add(new LegalAction(
                    LegalActionKind.SupplyShortage,
                    supplyShortage.Id,
                    target.Seat,
                    $"对 {target.Name} 使用【兵粮寸断】"));
            }
        }

        {
            var distanceLimit = GetCardUseDistanceLimit(actor, CardKind.SupplyShortage);
            var convertedCards = GetHand(actor)
                .Concat(GetEquipment(actor))
                .Where(card => !IsTurnHandCardRestricted(actor, card))
                .Where(card => !HasProgramCardIdentity(actor, card))
                .Where(card => GetProgramViewAsConversions(actor, card,
                    CardKind.SupplyShortage, forResponse: false).Count != 0);
            foreach (var converted in convertedCards)
            {
                var physicalName = CardCatalog.Get(converted.Kind).DisplayName;
                foreach (var conversionSource in GetProgramViewAsConversions(
                             actor, converted, CardKind.SupplyShortage, forResponse: false))
                    foreach (var target in _players.Where(player =>
                                 player.IsAlive &&
                                 player.Seat != actor.Seat &&
                                 !player.JudgmentAreaAbolished &&
                                 (HasPhaseSuitAllowance(actor,converted) || HasCardDistanceExemption(actor, player, CardKind.SupplyShortage) ||
                                  HasCardPolicy(actor, SkillProgramCardPolicyKind.IgnoreUseDistance,
                                      CardKind.SupplyShortage) || HasProvenanceUseDistance(actor, converted) ||
                                  GetCombatDistance(actor.Seat, player.Seat) <= distanceLimit) &&
                                 !IsDirectedCardTargetProhibited(actor.Seat, player.Seat, CardKind.SupplyShortage) &&
                                 !HasJudgmentEffectiveCard(player, CardKind.SupplyShortage)))
                    {
                        actions.Add(new LegalAction(
                            LegalActionKind.SupplyShortage,
                            converted.Id,
                            target.Seat,
                            DescribeConversion(conversionSource,
                                $"将【{physicalName}】当作【兵粮寸断】对 {target.Name} 使用"),
                            PlayedCardKind: CardKind.SupplyShortage)
                        { ConversionSource = conversionSource });
                    }
            }
        }

        foreach (var lightning in playableCards.Where(card => card.Kind == CardKind.Lightning))
        {
            if (!actor.JudgmentAreaAbolished && !HasJudgmentEffectiveCard(actor, CardKind.Lightning))
            {
                actions.Add(new LegalAction(
                    LegalActionKind.Lightning,
                    lightning.Id,
                    actor.Seat,
                    "对自己使用【闪电】"));
            }
        }

        var ironChainTargets = _players
            .Where(player => player.IsAlive)
            .OrderBy(player => player.Seat)
            .ToArray();
        {
            foreach (var ironChain in allPhysicalPlayableCards.Where(card =>
                         !HasProgramCardIdentity(actor, card) &&
                         card.Kind == CardKind.IronChain))
            {
                actions.Add(new LegalAction(
                    LegalActionKind.Recast,
                    ironChain.Id,
                    null,
                    "重铸【铁索连环】，摸一张牌"));
            }
        }

        foreach (var ironChain in playableCards.Where(card => card.Kind == CardKind.IronChain))
            AddProgramIronChainUseActions(actions, actor, ironChain, ironChainTargets);

        foreach (var converted in allPhysicalPlayableCards.Concat(GetEquipment(actor)).DistinctBy(card => card.Id).Where(card =>
                     !HasProgramCardIdentity(actor, card) && card.Kind != CardKind.IronChain))
        {
            foreach (var source in GetProgramViewAsConversions(actor, converted,
                         CardKind.IronChain, forResponse: false))
            {
                actions.Add(new LegalAction(LegalActionKind.Recast, converted.Id, null,
                    $"将【{converted.DisplayName}】当【铁索连环】重铸并摸一张牌",
                    PlayedCardKind: CardKind.IronChain)
                { ConversionSource = source });
                if (IsTurnHandCardRestricted(actor, converted)) continue;
                AddProgramIronChainUseActions(actions, actor, converted, ironChainTargets,
                    CardKind.IronChain, source);
            }
        }

        if (!actor.HasAlcoholEffect &&
            (!actor.UsedPlayPhaseAlcoholThisTurn || HasTargetCardQuotaAllowance(actor.Seat, actor.Seat) || HasNextUnlimitedCard(actor) || HasCardPolicy(actor, SkillProgramCardPolicyKind.UnlimitedAlcoholUse, CardKind.Alcohol)))
        {
            foreach (var alcohol in playableCards.Where(card => card.Kind == CardKind.Alcohol))
            {
                actions.Add(new LegalAction(
                    LegalActionKind.Alcohol,
                    alcohol.Id,
                    null,
                    "使用【酒】，本回合下一张杀伤害+1"));
            }
            foreach (var card in playableCards.Concat(GetEquipment(actor)).DistinctBy(card => card.Id))
                foreach (var conversion in GetProgramViewAsConversions(actor, card, CardKind.Alcohol, forResponse: false))
                    actions.Add(new LegalAction(LegalActionKind.Alcohol, card.Id, null,
                        DescribeConversion(conversion, $"将【{card.DisplayName}】当作【酒】使用"),
                        PlayedCardKind: CardKind.Alcohol)
                    { ConversionSource = conversion });
        }

        foreach (var equipment in playableCards.Where(card =>
                     EquipmentCatalog.IsEquipment(card.Kind) && actor.EquipmentSlotCapacity(EquipmentCatalog.Get(card.Kind).Slot) > 0))
        {
            var definition = EquipmentCatalog.Get(equipment.Kind);
            actions.Add(new LegalAction(
                LegalActionKind.Equip,
                equipment.Id,
                null,
                $"装备【{definition.DisplayName}】至{EquipmentCatalog.GetSlotName(definition.Slot)}槽"));
        }

        foreach (var dismantlement in playableCards.Where(card => card.Kind == CardKind.Dismantlement))
        {
            foreach (var target in _players.Where(player =>
                         player.IsAlive &&
                         player.Seat != actor.Seat &&
                         HasTargetCard(player)))
            {
                AddTargetCardActions(
                    actions,
                    actor.Seat,
                    LegalActionKind.Dismantlement,
                    dismantlement,
                    target,
                    "过河拆桥");
            }
        }

        {
            var qixiCards = GetHand(actor)
                .Concat(GetEquipment(actor))
                .Where(card => !IsTurnHandCardRestricted(actor, card))
                .Where(card => !HasProgramCardIdentity(actor, card))
                .Where(card => GetProgramViewAsConversions(actor, card,
                    CardKind.Dismantlement, forResponse: false).Count != 0);
            foreach (var converted in qixiCards)
            {
                foreach (var conversionSource in GetProgramViewAsConversions(
                             actor, converted, CardKind.Dismantlement, forResponse: false))
                    foreach (var target in _players.Where(player =>
                                 player.IsAlive &&
                                 player.Seat != actor.Seat &&
                                 HasTargetCard(player)))
                    {
                        AddTargetCardActions(
                            actions,
                            actor.Seat,
                            LegalActionKind.Dismantlement,
                            converted,
                            target,
                            "过河拆桥",
                            CardKind.Dismantlement,
                            conversionSource);
                    }
            }
        }

        foreach (var snatch in playableCards.Where(card => card.Kind == CardKind.Snatch))
        {
            var ignoresDistance = HasCardPolicy(actor, SkillProgramCardPolicyKind.IgnoreUseDistance,
                CardKind.Snatch) || HasProvenanceUseDistance(actor, snatch);
            foreach (var target in _players.Where(player =>
                         player.IsAlive &&
                         player.Seat != actor.Seat &&
                         (HasPhaseSuitAllowance(actor,snatch) || HasCardDistanceExemption(actor, player, CardKind.Snatch) ||
                          ignoresDistance ||
                          GetCombatDistance(actor.Seat, player.Seat) == 1) &&
                         !IsDirectedCardTargetProhibited(actor.Seat, player.Seat, CardKind.Snatch) &&
                         HasTargetCard(player)))
            {
                AddTargetCardActions(
                    actions,
                    actor.Seat,
                    LegalActionKind.Snatch,
                    snatch,
                    target,
                    "顺手牵羊");
            }
        }

        {
            var jixiCandidates = GetHand(actor)
                .Concat(GetEquipment(actor))
                .Concat(GetAuthority(actor))
                .Where(card => !IsTurnHandCardRestricted(actor, card) && !HasProgramCardIdentity(actor, card))
                .Where(card => GetProgramViewAsConversions(actor, card,
                    CardKind.Snatch, forResponse: false).Count != 0);
            foreach (var converted in jixiCandidates)
                foreach (var conversionSource in GetProgramViewAsConversions(
                             actor, converted, CardKind.Snatch, forResponse: false))
                {
                    var ignoresDistance = HasCardPolicy(actor, SkillProgramCardPolicyKind.IgnoreUseDistance,
                        CardKind.Snatch) || HasProvenanceUseDistance(actor, converted);
                    foreach (var target in _players.Where(player =>
                                 player.IsAlive &&
                                 player.Seat != actor.Seat &&
                                 (HasPhaseSuitAllowance(actor,converted) || HasCardDistanceExemption(actor, player, CardKind.Snatch) ||
                                  ignoresDistance ||
                                  GetCombatDistance(actor.Seat, player.Seat) == 1) &&
                                 !IsDirectedCardTargetProhibited(actor.Seat, player.Seat, CardKind.Snatch) &&
                                 HasTargetCard(player)))
                    {
                        AddTargetCardActions(
                            actions,
                            actor.Seat,
                            LegalActionKind.Snatch,
                            converted,
                            target,
                            "顺手牵羊",
                            CardKind.Snatch,
                            conversionSource);
                    }
                }
        }

        if (UsesFormalBorrowedSword)
        {
            foreach (var borrowedSword in playableCards.Where(card => card.Kind == CardKind.BorrowedSword))
            {
                foreach (var weaponOwner in _players.Where(player =>
                             player.IsAlive &&
                             player.Seat != actor.Seat &&
                             !IsDirectedCardTargetProhibited(actor.Seat, player.Seat, CardKind.BorrowedSword) &&
                             GetWeapon(player) is not null))
                {
                    foreach (var slashTarget in _players.Where(player =>
                                 IsLegalBorrowedSwordSlashTarget(weaponOwner, player)))
                    {
                        actions.Add(new LegalAction(
                            LegalActionKind.BorrowedSword,
                            borrowedSword.Id,
                            null,
                            $"令 {weaponOwner.Name} 对 {slashTarget.Name} 使用【杀】，否则获得其武器",
                            TargetSeats: [weaponOwner.Seat, slashTarget.Seat]));
                    }
                }
            }
        }

        foreach (var fireAttack in playableCards.Where(card => card.Kind == CardKind.FireAttack))
        {
            foreach (var target in _players.Where(player =>
                         player.IsAlive &&
                         GetHand(player).Any(card => player.Seat != actor.Seat || card.Id != fireAttack.Id)))
            {
                actions.Add(new LegalAction(
                    LegalActionKind.FireAttack,
                    fireAttack.Id,
                    target.Seat,
                    $"对 {target.Name} 使用【火攻】"));
            }
        }

        foreach (var converted in playableCards.Where(card => card.Kind != CardKind.FireAttack))
        {
            foreach (var conversionSource in GetProgramViewAsConversions(
                         actor, converted, CardKind.FireAttack, forResponse: false))
            {
                foreach (var target in _players.Where(player => player.IsAlive &&
                             GetHand(player).Any(card => player.Seat != actor.Seat || card.Id != converted.Id)))
                {
                    actions.Add(new LegalAction(LegalActionKind.FireAttack, converted.Id, target.Seat,
                        DescribeConversion(conversionSource,
                            $"将【{converted.DisplayName}】当作【火攻】对 {target.Name} 使用"),
                        PlayedCardKind: CardKind.FireAttack)
                    {
                        ConversionSource = conversionSource
                    });
                }
            }
        }

        if (UsesFormalWoodenOx &&
            !_woodenOxUsedThisTurn &&
            GetHand(actor).Count > 0 &&
            GetEquipment(actor).Any(card => card.Kind == CardKind.WoodenOx))
        {
            actions.Add(new LegalAction(
                LegalActionKind.UseEquipmentEffect,
                null,
                null,
                "发动【木牛流马】，将一张手牌扣置为“粮”，并可移动此装备",
                MinCardCount: 1,
                MaxCardCount: 1,
                MinTargetCount: 0,
                MaxTargetCount: 1,
                EquipmentKind: CardKind.WoodenOx)
            {
                SelectableCardIds = GetHand(actor).Select(card => card.Id).Order().ToArray(),
                SelectableTargetSeats = _players
                    .Where(player => player.IsAlive &&
                                     player.Seat != actor.Seat &&
                                     !player.EquipmentAreaAbolished &&
                                     GetEquipment(player).All(card =>
                                         EquipmentCatalog.Get(card.Kind).Slot != EquipmentSlot.Treasure))
                    .Select(player => player.Seat)
                    .Order()
                    .ToArray()
            });
        }

        AddPhaseLimitedBasicCardActions(actions, actor, allPhysicalPlayableCards);
        AddSingleCardTrickConversionActions(actions, actor, allPhysicalPlayableCards);
        AddDeclarationSlashActions(actions, actor, allPhysicalPlayableCards);
        if (includeProgramActions) actions.AddRange(BuildProgramActions(actor));
        actions.AddRange(BuildForeignPublicPileSlashActions(actor));
        AddNextCardTargetAdjustmentActions(actions, actor);
        AddNextActualUseAdjustmentActions(actions, actor);
        AddHpLossSlashEquipmentActions(actions, actor);
        AddPaidColorEquipmentDuelActions(actions, actor);
        AddRedAdditionalTargetActions(actions, actor);
        actions.Add(new LegalAction(LegalActionKind.EndPlay, null, null, "结束出牌"));
        var physicalKinds = allPhysicalPlayableCards.ToDictionary(card => card.Id, card => card.Kind);
        var physicalSuits = allPhysicalPlayableCards.ToDictionary(card => card.Id, card => EffectiveSuit(actor, card));
        var permittedActions = actions.Where(action =>
        {
            var effectiveKind = action.PlayedCardKind ??
                (action.CardId is { } cardId && physicalKinds.TryGetValue(cardId, out var physicalKind)
                    ? physicalKind
                    : (CardKind?)null);
            if (effectiveKind is null) return true;
            var targets = action.TargetSeats.Count > 0
                ? action.TargetSeats
                : action.TargetSeat is { } targetSeat ? [targetSeat]
                : HasBlackTrickTargetPolicy && action.Kind == LegalActionKind.DrawTwo ? [actor.Seat] : [];
            var declaredSuit = action.CardId is { } actionCardId &&
                physicalSuits.TryGetValue(actionCardId, out var physicalSuit)
                    ? physicalSuit
                    : (action.Kind == LegalActionKind.IronChain ||
                       FindOwnedPlayableCard(actor, action.CardId) is { } paidColorCard && IsPaidColorEquipmentDuelAction(actor, action, paidColorCard)) &&
                      FindOwnedPlayableCard(actor, action.CardId) is { } equipmentConversion &&
                      _cardZones.GetLocation(equipmentConversion.Id) == CardLocation.Equipment(actor.Seat)
                        ? EffectiveSuit(actor, equipmentConversion)
                        : (Suit?)null;
            var declaredColor = action.CardId is { } colorCardId && FindOwnedPlayableCard(actor, colorCardId) is { } colorCard
                ? SuitColor(EffectiveSuit(actor, ApplyProgramUseAppearance(actor, colorCard, action.ConversionSource))) : null;
            if (action.Kind != LegalActionKind.Recast && action.MaxCardCount <= 1 && declaredSuit is { } useSuit && IsTurnSuitUseForbidden(actor.Seat, useSuit)) return false;
            // Global tricks drop individually shielded targets instead of the whole
            // use: a black Nanman still resolves against everyone a Curtain bearer
            // does not protect, and only loses its action when no target remains.
            if (action.Kind is LegalActionKind.BarbarianAssault or LegalActionKind.ArrowBarrage or
                LegalActionKind.PeachGarden or LegalActionKind.FiveGrains)
                return GetDeclaredCardTargets(actor, action.Kind, targets)
                    .Any(target => !IsDirectedCardTargetProhibited(actor.Seat, target, effectiveKind.Value) &&
                                   !IsCardTargetProhibited(_players[target], effectiveKind.Value, declaredSuit, declaredColor));
            return GetDeclaredCardTargets(actor, action.Kind, targets).All(target =>
                !IsDirectedCardTargetProhibited(actor.Seat, target, effectiveKind.Value) &&
                !IsCardTargetProhibited(_players[target], effectiveKind.Value, declaredSuit, declaredColor));
        });
        return FilterBeneficiarySuitShieldActions(actor, FilterTurnCardUseRestrictions(actor, permittedActions));
    }

    private IReadOnlyList<CharacterState> GetFangtianOrderedSlashTargets(
        CharacterState actor,
        Card slash,
        CardConversionSource? conversionSource = null,
        bool ignoresSlashLimit = false,
        CardKind effectiveKind = CardKind.Slash)
    {
        if (IsCardUseForbidden(actor.Seat, effectiveKind, CardActionType.Use))
        {
            return [];
        }

        return _players
            .Where(player =>
                CanSpendSlashUse(actor, player, ignoresSlashLimit || IgnoresProgramSlashLimit(actor, conversionSource), effectiveKind, slash) &&
                CanUseSlashTarget(actor, player, slash, conversionSource, effectiveKind))
            .ToArray();
    }

    private void AddFangtianHalberdSlashActions(
        ICollection<LegalAction> actions,
        CharacterState actor,
        Card physicalCard,
        IReadOnlyList<CharacterState> legalTargets,
        string slashName,
        CardKind? playedCardKind,
        CardConversionSource? conversionSource = null,
        IReadOnlyList<CardConversionSource>? additionalConversionSources = null)
    {
        if (!UsesFormalFangtianHalberd ||
            GetHand(actor).Count != 1 ||
            GetHand(actor)[0].Id != physicalCard.Id ||
            !HasWeaponAbility(actor, CardKind.FangtianHalberd) ||
            legalTargets.Count < 2)
        {
            return;
        }

        var orderedTargets = legalTargets
            .OrderBy(player => (player.Seat - actor.Seat + _playerCount) % _playerCount)
            .ToArray();
        var maximumTargets = Math.Min(3, orderedTargets.Length);
        for (var targetCount = 2; targetCount <= maximumTargets; targetCount++)
        {
            AddCombinations(0, []);

            void AddCombinations(int startIndex, IReadOnlyList<CharacterState> selected)
            {
                if (selected.Count == targetCount)
                {
                    AddFangtianHalberdSlashAction(
                        actions,
                        physicalCard,
                        slashName,
                        playedCardKind,
                        selected,
                        conversionSource,
                        additionalConversionSources);
                    return;
                }

                for (var index = startIndex;
                     index <= orderedTargets.Length - (targetCount - selected.Count);
                     index++)
                {
                    AddCombinations(index + 1, [.. selected, orderedTargets[index]]);
                }
            }
        }
    }

    private void AddFangtianHalberdSlashAction(
        ICollection<LegalAction> actions,
        Card physicalCard,
        string slashName,
        CardKind? playedCardKind,
        IReadOnlyList<CharacterState> targets,
        CardConversionSource? conversionSource,
        IReadOnlyList<CardConversionSource>? additionalConversionSources)
    {
        var targetSeats = Array.AsReadOnly(targets.Select(target => target.Seat).ToArray());
        actions.Add(new LegalAction(
            LegalActionKind.Slash,
            physicalCard.Id,
            targetSeats[0],
            DescribeConversion(conversionSource, $"发动【方天画戟】，对 {string.Join("、", targets.Select(target => target.Name))} 使用【{slashName}】"),
            PlayedCardKind: playedCardKind,
            TargetSeats: targetSeats)
        {
            ConversionSource = conversionSource,
            AdditionalConversionSources = additionalConversionSources,
        });
    }

    private void AddTargetCardActions(
        ICollection<LegalAction> actions,
        int actorSeat,
        LegalActionKind actionKind,
        Card effectCard,
        CharacterState target,
        string effectName,
        CardKind? playedCardKind = null,
        CardConversionSource? conversionSource = null)
    {
        var useDescription = playedCardKind is null
            ? $"对 {target.Name} 使用【{effectName}】"
            : $"将【{effectCard.DisplayName}】当作【{effectName}】对 {target.Name} 使用";
        if (GetHand(target).Count > 0)
        {
            actions.Add(new LegalAction(
                actionKind,
                effectCard.Id,
                target.Seat,
                useDescription,
                PlayedCardKind: playedCardKind)
            { ConversionSource = conversionSource });
        }

        foreach (var equipment in GetEquipment(target))
        {
            if (actionKind == LegalActionKind.Dismantlement && IsForeignEquipmentDiscardPrevented(
                    actorSeat, equipment, CardLocation.Equipment(target.Seat), OwnedCardMoveIntent.Discard))
                continue;
            actions.Add(new LegalAction(
                actionKind,
                effectCard.Id,
                target.Seat,
                $"{useDescription}，选择其装备【{equipment.DisplayName}】",
                PlayedCardKind: playedCardKind,
                TargetCardId: equipment.Id)
            { ConversionSource = conversionSource });
        }

        foreach (var judgment in GetJudgment(target))
        {
            actions.Add(new LegalAction(
                actionKind,
                effectCard.Id,
                target.Seat,
                $"{useDescription}，选择其判定区【{judgment.DisplayName}】",
                PlayedCardKind: playedCardKind,
                TargetCardId: judgment.Id)
            { ConversionSource = conversionSource });
        }
    }

    private Card? FindOwnedPlayableCard(CharacterState actor, int? cardId)
    {
        if (cardId is not { } requestedCardId)
        {
            return null;
        }

        return AvailableDeclarationCard(actor) is { } declared && declared.Id == requestedCardId ? declared :
               GetHand(actor).SingleOrDefault(card => card.Id == requestedCardId) ??
               GetWoodenOxGrain(actor).SingleOrDefault(card => card.Id == requestedCardId) ??
               GetEquipment(actor).SingleOrDefault(card => card.Id == requestedCardId) ??
               GetAuthority(actor).SingleOrDefault(card => card.Id == requestedCardId);
    }

    private static bool IsOwnedPlayableLocation(CharacterState actor, CardLocation location) =>
        location.OwnerSeat == actor.Seat &&
        location.Zone is CardZoneKind.Hand or CardZoneKind.WoodenOxGrain;

    private CardLocation FindOwnedCardLocation(CharacterState actor, Card card)
    {
        if (IsSelectedRequestedDeckBasicMaterial(actor.Seat, card.Id)) return CardLocation.DrawPile;
        if (UnclaimedDeclarationPayment(actor.Seat, card.Id) is { } paid) return paid.Cost.From;
        if (GetHand(actor).Any(candidate => candidate.Id == card.Id))
        {
            return CardLocation.Hand(actor.Seat);
        }

        if (GetEquipment(actor).Any(candidate => candidate.Id == card.Id))
        {
            return CardLocation.Equipment(actor.Seat);
        }

        if (GetWoodenOxGrain(actor).Any(candidate => candidate.Id == card.Id))
        {
            return CardLocation.WoodenOxGrain(actor.Seat);
        }

        if (GetAuthority(actor).Any(candidate => candidate.Id == card.Id))
        {
            return CardLocation.Authority(actor.Seat);
        }

        throw new InvalidOperationException("The chosen card is no longer in the actor's playable zones.");
    }

    private static bool IsPublicTargetZone(CardZoneKind zone) =>
        zone is CardZoneKind.Equipment or CardZoneKind.Judgment;

    private static CardMoveReason GetTargetCardMoveReason(
        LegalActionKind actionKind,
        CardZoneKind sourceZone) =>
        (actionKind, sourceZone) switch
        {
            (LegalActionKind.Dismantlement, CardZoneKind.Hand) => CardMoveReasons.Dismantlement,
            (LegalActionKind.Dismantlement, CardZoneKind.Equipment) => CardMoveReasons.Dismantlement,
            (LegalActionKind.Dismantlement, CardZoneKind.Judgment) => CardMoveReasons.DismantlementJudgment,
            (LegalActionKind.Snatch, CardZoneKind.Hand) => CardMoveReasons.Snatch,
            (LegalActionKind.Snatch, CardZoneKind.Equipment) => CardMoveReasons.Snatch,
            (LegalActionKind.Snatch, CardZoneKind.Judgment) => CardMoveReasons.SnatchJudgment,
            _ => throw new InvalidOperationException(
                $"{actionKind} cannot move a target card from {sourceZone}.")
        };

    private static CardMoveReason GetTargetCardFinishedMoveReason(
        LegalActionKind actionKind,
        CardZoneKind sourceZone) =>
        (actionKind, sourceZone) switch
        {
            (LegalActionKind.Dismantlement, CardZoneKind.Hand) => CardMoveReasons.DismantlementFinished,
            (LegalActionKind.Dismantlement, CardZoneKind.Equipment) => CardMoveReasons.DismantlementFinished,
            (LegalActionKind.Dismantlement, CardZoneKind.Judgment) => CardMoveReasons.DismantlementJudgmentFinished,
            (LegalActionKind.Snatch, CardZoneKind.Hand) => CardMoveReasons.SnatchFinished,
            (LegalActionKind.Snatch, CardZoneKind.Equipment) => CardMoveReasons.SnatchFinished,
            (LegalActionKind.Snatch, CardZoneKind.Judgment) => CardMoveReasons.SnatchJudgmentFinished,
            _ => throw new InvalidOperationException(
                $"{actionKind} cannot finish a target card from {sourceZone}.")
        };

    private static LegalAction? SelectPlayAction(
        IReadOnlyList<LegalAction> legalActions,
        Card card,
        IReadOnlyList<int> targets,
        CardKind? playedCardKind,
        int? targetCardId,
        CardConversionSource? conversionSource,
        IReadOnlyList<CardConversionSource>? additionalConversionSources)
    {
        var matching = legalActions
            .Where(candidate => candidate.Kind != LegalActionKind.Recast)
            .Where(candidate => candidate.TargetSeats.SequenceEqual(targets))
            .Where(candidate => candidate.TargetCardId == targetCardId)
            .Where(candidate => (candidate.PlayedCardKind ?? card.Kind) ==
                (playedCardKind ?? card.Kind))
            .Where(candidate => candidate.ConversionSource == conversionSource)
            .Where(candidate => ConversionSourcesEqual(candidate.AdditionalConversionSources,
                additionalConversionSources))
            .ToArray();
        return matching.Length == 1 ? matching[0] : null;
    }

    private static bool ConversionSourcesEqual(
        IReadOnlyList<CardConversionSource>? left,
        IReadOnlyList<CardConversionSource>? right) =>
        (left is null || left.Count == 0) && (right is null || right.Count == 0) ||
        left is not null && right is not null && left.SequenceEqual(right);

    private static bool IsSlashCard(CardKind kind) =>
        kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash;

    private void MarkSlashUsedOrPlayedDuringCurrentPlayPhase(int actorSeat, CardKind effectiveCardKind)
    {
        if (_phase == TurnPhase.Play &&
            actorSeat == _currentSeat &&
            IsSlashCard(effectiveCardKind))
        {
            _usedOrPlayedSlashDuringPlayPhase = true;
        }
    }

    private IReadOnlyList<Card> GetResponseCards(
        CharacterState responder,
        CardKind requiredCardKind)
    {
        if (HasIssuedPlayPhaseUseBan(responder.Seat) && IsProgramResponseCardUse(responder, requiredCardKind) || requiredCardKind == CardKind.Slash &&
            IsCardUseForbidden(responder.Seat, CardKind.Slash, CardActionType.Response))
        {
            return [];
        }

        var cards = GetPlayableCards(responder)
            .Where(card => !IsTurnHandCardRestricted(responder, card))
            .Where(card => CanUseCardAsResponse(responder, card, requiredCardKind));
        cards = cards.Concat(GetEquipment(responder).Where(card =>
            GetProgramViewAsConversions(responder, card, requiredCardKind,
                forResponse: true).Count != 0));

        if (requiredCardKind == CardKind.Dodge)
            cards = cards.Concat(GetProgramMultiCardViewAsSelections(responder, requiredCardKind, true).Select(selection => selection.Cards[0]));
        return cards.DistinctBy(card => card.Id).ToArray();
    }

    private IReadOnlyList<IReadOnlyList<Card>> GetZhangbaSlashPairs(CharacterState responder)
    {
        if (!CanUseZhangbaSerpentSpear(responder))
        {
            return [];
        }

        var hand = GetPlayableCards(responder)
            .Where(card => !IsTurnHandCardRestricted(responder, card))
            .OrderBy(card => card.Id)
            .ToArray();
        var pairs = new List<IReadOnlyList<Card>>();
        for (var first = 0; first < hand.Length - 1; first++)
        {
            for (var second = first + 1; second < hand.Length; second++)
            {
                pairs.Add(Array.AsReadOnly(new[] { hand[first], hand[second] }));
            }
        }

        return pairs;
    }

    private IReadOnlyList<Card>? FindZhangbaSlashPair(
        CharacterState responder,
        IReadOnlyList<int> requestedCardIds)
    {
        if (requestedCardIds.Count != 2 || requestedCardIds.Distinct().Count() != 2)
        {
            return null;
        }

        var requested = requestedCardIds.Order().ToArray();
        return GetZhangbaSlashPairs(responder).FirstOrDefault(pair =>
            pair.Select(card => card.Id).Order().SequenceEqual(requested));
    }

    private bool CanConvertResponse(CharacterState responder, Card card, CardKind requiredCardKind)
    {
        var identities = GetProgramCardIdentityMatches(responder, card);
        if (identities.Count != 0)
            return identities.Any(match =>
                MatchesRequiredCard(match.Identity.OutputKind, requiredCardKind));
        return GetProgramViewAsConversions(responder, card, requiredCardKind,
            forResponse: true).Count != 0;
    }

    private CardKind GetEffectiveResponseKind(
        CharacterState responder,
        Card responseCard,
        CardKind requiredCardKind)
    {
        var identities = GetProgramCardIdentityMatches(responder, responseCard);
        if (identities.Count != 0)
        {
            return identities.Any(match =>
                    MatchesRequiredCard(match.Identity.OutputKind, requiredCardKind))
                ? requiredCardKind
                : throw new InvalidOperationException(
                    $"The mandatory identity of response card {responseCard.Id} cannot satisfy {requiredCardKind}.");
        }

        if (MatchesRequiredCard(responseCard.Kind, requiredCardKind))
        {
            return requiredCardKind;
        }

        if ((HasResponseEntityExchangeObservers() || TracksActionDiscardColor) && GetProgramMultiCardViewAsSelections(responder,requiredCardKind,true).Any(selection=>selection.Cards[0].Id==responseCard.Id))
            return requiredCardKind;
        return CanConvertResponse(responder, responseCard, requiredCardKind)
            ? requiredCardKind
            : throw new InvalidOperationException(
                $"The response card {responseCard.Id} cannot be used as {requiredCardKind}.");
    }

    private static bool IsNativeResponseCard(Card responseCard, CardKind requiredCardKind) =>
        MatchesRequiredCard(responseCard.Kind, requiredCardKind);

    private bool CanUseCardAsResponse(
        CharacterState responder,
        Card card,
        CardKind requiredCardKind)
    {
        var identities = GetProgramCardIdentityMatches(responder, card);
        return identities.Count != 0
            ? identities.Any(match => MatchesRequiredCard(match.Identity.OutputKind, requiredCardKind))
            : MatchesRequiredCard(card.Kind, requiredCardKind) ||
              CanConvertResponse(responder, card, requiredCardKind);
    }

    private static CardKind? ReadResponseCardKind(PromptChoice choice) =>
        choice.Parameters.TryGetValue("response-card-kind", out var value) &&
        Enum.TryParse<CardKind>(value, ignoreCase: false, out var kind)
            ? kind
            : null;

    private static bool MatchesRequiredCard(CardKind actual, CardKind required) =>
        required == CardKind.Slash ? IsSlashCard(actual) : actual == required;

    private static DamageNature GetDamageNature(CardKind kind) => kind switch
    {
        CardKind.FireSlash or CardKind.FireAttack => DamageNature.Fire,
        CardKind.ThunderSlash or CardKind.Lightning => DamageNature.Thunder,
        _ => DamageNature.Normal
    };

    private DamageNature GetDamageNature(IDamageAttempt attack) =>
        IsYingboRepeated(attack.ResolutionId)
            ? DamageNature.Fire
            : attack.DamageNatureOverride ?? GetDamageNature(RequireAttackCardKind(attack));

    private static Card RequireAttackCard(CardAttackHandle attack) =>
        attack.Card ?? throw new InvalidOperationException(
            "This card-resolution path requires a physical source card.");

    private static CardKind RequireAttackCardKind(IDamageAttempt attack) =>
        attack.EffectiveCardKind ?? throw new InvalidOperationException(
            "This card-resolution path requires an effective source card kind.");

    private int FinalizeAttackDamageAmount(IDamageAttempt attack)
    {
        if (attack.DamageAmountFinalized)
        {
            return attack.DamageAmount;
        }

        var conversionBonus = attack is CardAttackHandle converted ? ProgramConversionDamageBonus(converted) : 0;
        var baseAmount = attack.DamageAmount + conversionBonus +
            (GetDamageNature(attack) == DamageNature.Fire ? _players[attack.TargetSeat].Markers.GetValueOrDefault(PlayerMarkerKind.Gale) : 0);
        var turnDamageModifiers = attack.EffectiveCardKind is { } effectiveKind
            ? _turnCardUseEffects.GetDamageModifiers(
                _turnNumber,
                _currentSeat,
                attack.SourceSeat,
                attack.CardUserSeat,
                effectiveKind,
                attack.IsChainPropagation)
            : [];
        var programDamageModifiers = turnDamageModifiers
            .Select(modifier => (modifier.Source, modifier.Amount))
            .Concat(GetPassiveProgramDamageModifiers(attack))
            .Concat(GetCurrentTurnHeartSlashBonuses(attack))
            .Concat(GetShownEntityUseDamageBenefits(attack))
            .ToArray();
        var programDamageBonus = programDamageModifiers.Sum(modifier => modifier.Amount) + FinalTargetSlashDamage(attack) + FrozenNextSlashDamage(attack) +
            (attack is CardAttackHandle bladed ? RedBladeDamageBonus(bladed) : 0);
        var receivesGudingBladeBonus = UsesFormalGudingBlade &&
            !attack.IsChainPropagation &&
            attack.CardUserSeat == attack.SourceSeat &&
            attack.EffectiveCardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash &&
            GetHand(_players[attack.TargetSeat]).Count == 0 &&
            HasWeaponAbility(_players[attack.SourceSeat], CardKind.GudingBlade);
        var receivesYingboBonus = IsYingboRepeated(attack.ResolutionId);
        var receivesTengjiaBonus = UsesFormalTengjia &&
            GetDamageNature(attack) == DamageNature.Fire &&
            HasTengjia(_players[attack.TargetSeat]) &&
            (attack.IsChainPropagation || !attack.IgnoresArmor);
        var silverLionCapsDamage = UsesFormalSilverLion &&
            baseAmount + programDamageBonus +
            (receivesGudingBladeBonus ? 1 : 0) +
            (receivesYingboBonus ? 1 : 0) +
            (receivesTengjiaBonus ? 1 : 0) > 1 &&
            HasSilverLion(_players[attack.TargetSeat]) &&
            (attack.IsChainPropagation || !attack.IgnoresArmor);
        var damageBonus = programDamageBonus + conversionBonus +
                          (GetDamageNature(attack) == DamageNature.Fire ? _players[attack.TargetSeat].Markers.GetValueOrDefault(PlayerMarkerKind.Gale) : 0) +
                          (receivesGudingBladeBonus ? 1 : 0) +
                          (receivesYingboBonus ? 1 : 0) +
                          (receivesTengjiaBonus ? 1 : 0);
        attack.FinalizeDamageAmount(damageBonus, silverLionCapsDamage ? 1 : null);
        var runningAmount = baseAmount;
        foreach (var modifier in programDamageModifiers)
        {
            var modifiedAmount = checked(runningAmount + modifier.Amount);
            AdvanceEventRulesAndQueueFact(new ProgramCardDamageModifiedEvent(
                attack.ResolutionId,
                modifier.Source,
                attack.TargetSeat,
                attack.EffectiveCardKind,
                runningAmount,
                modifiedAmount));
            AddLog(
                "SkillTriggered",
                $"{_players[modifier.Source.OwnerSeat].Name} 的【{_contentRegistry!.GetSkill(modifier.Source.SkillId).Name}】令本次伤害 +{modifier.Amount}。",
                modifier.Source.OwnerSeat,
                attack.TargetSeat);
            runningAmount = modifiedAmount;
        }
        if (receivesGudingBladeBonus)
        {
            var modifiedAmount = checked(runningAmount + 1);
            AdvanceEventRulesAndQueueFact(new GudingBladeDamageIncreasedEvent(
                attack.ResolutionId,
                attack.SourceSeat,
                attack.TargetSeat,
                RequireAttackCardKind(attack),
                runningAmount,
                modifiedAmount));
            AddLog(
                "EquipmentEffect",
                $"{_players[attack.SourceSeat].Name} 的【古锭刀】对空手的 {_players[attack.TargetSeat].Name} 生效，本次伤害 +1。",
                attack.SourceSeat,
                attack.TargetSeat);
            runningAmount = modifiedAmount;
        }
        if (receivesYingboBonus)
        {
            var modifiedAmount = checked(runningAmount + 1);
            AdvanceEventRulesAndQueueFact(new YingboDamageIncreasedEvent(
                attack.ResolutionId,
                attack.SourceSeat,
                attack.TargetSeat,
                RequireAttackCardKind(attack),
                runningAmount,
                modifiedAmount));
            AddLog(
                "SkillTriggered",
                $"{_players[attack.SourceSeat].Name} 的【英博】令本次伤害 +1。",
                attack.SourceSeat,
                attack.TargetSeat);
            runningAmount = modifiedAmount;
        }
        if (receivesTengjiaBonus)
        {
            var modifiedAmount = checked(runningAmount + 1);
            AdvanceEventRulesAndQueueFact(new TengjiaFireDamageIncreasedEvent(
                attack.ResolutionId,
                attack.SourceSeat,
                attack.TargetSeat,
                attack.EffectiveCardKind,
                runningAmount,
                modifiedAmount));
            AddLog("EquipmentEffect", $"{_players[attack.TargetSeat].Name} 的【藤甲】令本次火焰伤害 +1。", attack.TargetSeat, attack.SourceSeat);
            runningAmount = modifiedAmount;
        }
        if (silverLionCapsDamage)
        {
            AdvanceEventRulesAndQueueFact(new SilverLionDamageCappedEvent(
                attack.ResolutionId,
                attack.SourceSeat,
                attack.TargetSeat,
                attack.EffectiveCardKind,
                runningAmount,
                1));
            AddLog("EquipmentEffect", $"{_players[attack.TargetSeat].Name} 的【白银狮子】将本次伤害改为 1 点。", attack.TargetSeat, attack.SourceSeat);
        }

        return attack.DamageAmount;
    }

    private IReadOnlyList<(CardUseEffectSource Source, int Amount)> GetPassiveProgramDamageModifiers(
        IDamageAttempt attack)
    {
        if (!IsValidPlayerSeat(attack.SourceSeat) || !IsValidPlayerSeat(attack.TargetSeat) ||
            !_players[attack.SourceSeat].IsAlive || !_players[attack.TargetSeat].IsAlive)
            return [];

        var sourceAndTarget = new[] { attack.SourceSeat, attack.TargetSeat }.Distinct();
        return sourceAndTarget
            .SelectMany(seat => GetSkillBindingShard(_players[seat]).ProgramInstances
                .SelectMany(instance => instance.Program.DamageModifiers.Select(modifier =>
                    (seat, instance, modifier))))
            .Where(item => item.modifier.SourceScope == SkillProgramDamageModifierSourceScope.DamageParticipant
                ? item.modifier.Condition switch
                  {
                      SkillProgramDamageModifierCondition.OwnerUniqueMaximumHand =>
                          _players.Where(player => player.IsAlive && player.Seat != item.seat)
                              .All(player => GetHand(player).Count < GetHand(_players[item.seat]).Count),
                      SkillProgramDamageModifierCondition.ChainedFirePropagationOrigin =>
                          item.seat == attack.TargetSeat && !attack.IsChainPropagation &&
                          GetDamageNature(attack) == DamageNature.Fire && _players[item.seat].IsChained &&
                          _players.Any(player => player.IsAlive && player.Seat != item.seat && player.IsChained),
                      _ => false
                  }
                : item.seat == attack.SourceSeat &&
                  !attack.IsChainPropagation && !attack.IsDelayedJudgmentDamage &&
                  !attack.IsProgramJudgmentDamage && attack.CardUserSeat == attack.SourceSeat &&
                  attack.EffectiveCardKind is { } kind && item.modifier.CardKinds.Contains(kind) &&
                  (item.modifier.Condition switch
                  {
                      SkillProgramDamageModifierCondition.Always => true,
                      SkillProgramDamageModifierCondition.SourceOutsideTargetAttackRange =>
                          !IsWithinAttackRange(attack.TargetSeat, attack.SourceSeat),
                      SkillProgramDamageModifierCondition.SourceNotFewerHandAndEquipmentThanTarget =>
                          GetHand(_players[attack.TargetSeat]).Count <= GetHand(_players[attack.SourceSeat]).Count &&
                          GetEquipment(_players[attack.TargetSeat]).Count <=
                          GetEquipment(_players[attack.SourceSeat]).Count,
                      _ => false
                  }))
            .Select(item => (new CardUseEffectSource(item.instance.SkillId, item.modifier.Id,
                item.seat, item.instance.SkillInstanceId), item.modifier.Amount))
            .ToArray();
    }

    private static string GetSuitName(Suit suit) => suit switch
    {
        Suit.Spade => "黑桃",
        Suit.Heart => "红桃",
        Suit.Club => "梅花",
        Suit.Diamond => "方块",
        _ => suit.ToString()
    };

    private static string GetDamageNatureLabel(DamageNature nature) => nature switch
    {
        DamageNature.Fire => "火焰",
        DamageNature.Thunder => "雷电",
        _ => string.Empty
    };

    private bool IsSlashProhibited(CharacterState target)
    {
        return IsCardTargetProhibited(target, CardKind.Slash);
    }

    private bool IsCardTargetProhibited(CharacterState target, CardKind cardKind, Suit? suit = null, bool? effectiveIsRed = null) =>
        IsDelayedCard(cardKind) && HasCardPolicy(target, SkillProgramCardPolicyKind.ProhibitDelayedTrickTarget) ||
        HasCardPolicy(target, SkillProgramCardPolicyKind.ProhibitTarget, cardKind) ||
        suit is { } declaredSuit &&
        CardPolicies(target, SkillProgramCardPolicyKind.ProhibitTargetBySuit, cardKind)
            .Any(item => item.Policy.InputSuit == declaredSuit) || IsBlackTrickTargetProhibited(target, cardKind, effectiveIsRed);

    private bool IsSlashProhibited(CharacterState source, CharacterState target, Card slashCard)
    {
        if (IsSlashProhibited(target))
        {
            return true;
        }

        return false;
    }

    private void RequestHumanPlay()
    {
        var legal = BuildLegalActions(_players[_currentSeat]);
        _pendingDecision = new PendingDecision(
            DecisionKind.PlayCard,
            _currentSeat,
            "请选择一张牌和目标，或结束出牌阶段。",
            legal.Where(action => action.CardId.HasValue).Select(action => action.CardId!.Value).Distinct().ToArray(),
            legal.SelectMany(action => action.TargetSeats).Distinct().ToArray())
        {
            PromptId = CreatePromptId(),
            Choices = CreatePlayChoices(legal)
        };
        _status = EngineStatus.AwaitingHumanPlay;
        AdvanceRulesAndPublishState();
    }

    private PromptId CreatePromptId() => new(++_nextPromptId);

    private static IReadOnlyList<PromptChoice> CreatePlayChoices(
        IReadOnlyList<LegalAction> legalActions)
    {
        var choices = new List<PromptChoice>(legalActions.Count);
        foreach (var action in legalActions)
        {
            if (action.Kind == LegalActionKind.UseProgramSkill)
            {
                choices.Add(CreateProgramPlayChoice(action));
                continue;
            }
            if (action.Kind == LegalActionKind.RevealGeneral)
            {
                var slot = action.GeneralSlot ?? throw new InvalidOperationException("A reveal action must identify a general slot.");
                choices.Add(new PromptChoice(new ChoiceId($"play.reveal.{slot}"), action.Description, [], [],
                    new Dictionary<string, string> { ["action"] = "reveal-general", ["general-slot"] = slot.ToString() }));
                continue;
            }
            if (action.Kind == LegalActionKind.UseEquipmentEffect)
            {
                var equipment = action.EquipmentKind ??
                    throw new InvalidOperationException("An equipment-effect prompt choice must identify its equipment.");
                choices.Add(new PromptChoice(
                    new ChoiceId($"play.equipment-effect.{equipment}"),
                    action.Description,
                    [],
                    [],
                    new Dictionary<string, string>
                    {
                        ["action"] = "use-equipment-effect",
                        ["equipment"] = equipment.ToString(),
                        ["min-card-count"] = action.MinCardCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["max-card-count"] = action.MaxCardCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["min-target-count"] = action.MinTargetCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["max-target-count"] = action.MaxTargetCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    }));
                continue;
            }

            if (action.Kind == LegalActionKind.EndPlay)
            {
                choices.Add(new PromptChoice(
                    new ChoiceId("play.end"),
                    action.Description,
                    [],
                    [],
                    new Dictionary<string, string>
                    {
                        ["action"] = "end-play"
                    }));
                continue;
            }

            var cardId = action.CardId ??
                throw new InvalidOperationException("A playable prompt choice must reference a card.");
            var targets = action.TargetSeats;
            var actionName = action.Kind switch
            {
                LegalActionKind.Slash => "slash",
                LegalActionKind.Peach => "peach",
                LegalActionKind.Duel => "duel",
                LegalActionKind.DrawTwo => "draw-two",
                LegalActionKind.BarbarianAssault => "barbarian-assault",
                LegalActionKind.ArrowBarrage => "arrow-barrage",
                LegalActionKind.PeachGarden => "peach-garden",
                LegalActionKind.FiveGrains => "five-grains",
                LegalActionKind.Dismantlement => "dismantlement",
                LegalActionKind.Snatch => "snatch",
                LegalActionKind.FireAttack => "fire-attack",
                LegalActionKind.Alcohol => "alcohol",
                LegalActionKind.Equip => "equip",
                LegalActionKind.IronChain => "iron-chain",
                LegalActionKind.Recast => "recast",
                LegalActionKind.Indulgence => "indulgence",
                LegalActionKind.SupplyShortage => "supply-shortage",
                LegalActionKind.Lightning => "lightning",
                LegalActionKind.BorrowedSword => "borrowed-sword",
                _ => throw new InvalidOperationException($"Unsupported prompt action {action.Kind}.")
            };
            var choiceId = targets.Count switch
            {
                0 => $"play.{actionName}.card-{cardId}",
                1 => $"play.{actionName}.card-{cardId}.target-{targets[0]}",
                _ => $"play.{actionName}.card-{cardId}.targets-{string.Join('-', targets)}"
            };
            if (action.TargetCardId is { } targetCardId)
            {
                choiceId += $".target-card-{targetCardId}";
            }
            if (action.PlayedCardKind == CardKind.FireSlash)
            {
                choiceId += ".as-FireSlash";
            }
            if (action.PlayedCardKind == CardKind.ThunderSlash)
                choiceId += ".as-ThunderSlash";
            if (action.ConversionSource is { } conversionChoiceSource)
            {
                choiceId += $".conversion-{conversionChoiceSource.SkillId}-{conversionChoiceSource.BindingId}";
            }
            if (action.AdditionalConversionSources is { Count: > 0 } additionalChoiceSources)
            {
                choiceId += $".additional-conversions-{string.Join('-', additionalChoiceSources.Select(source => $"{source.SkillId}-{source.BindingId}"))}";
            }

            var parameters = new Dictionary<string, string>
            {
                ["action"] = actionName
            };
            if (action.PlayedCardKind is { } playedCardKind)
            {
                parameters["played-card-kind"] = playedCardKind.ToString();
            }
            if (action.ConversionSource is { } conversionSource)
            {
                AddConversionParameters(parameters, conversionSource);
            }
            if (action.AdditionalConversionSources is { Count: > 0 } additionalConversionSources)
            {
                parameters["additional-conversion-count"] = additionalConversionSources.Count.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                for (var index = 0; index < additionalConversionSources.Count; index++)
                {
                    var source = additionalConversionSources[index];
                    parameters[$"additional-conversion-{index}-skill-id"] = source.SkillId;
                    parameters[$"additional-conversion-{index}-binding-id"] = source.BindingId;
                    parameters[$"additional-conversion-{index}-owner-seat"] = source.OwnerSeat.ToString(
                        System.Globalization.CultureInfo.InvariantCulture);
                    parameters[$"additional-conversion-{index}-instance-id"] = source.SkillInstanceId;
                }
            }
            if (action.TargetCardId is { } targetCardIdParameter)
            {
                parameters["target-card-id"] = targetCardIdParameter.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            }

            choices.Add(new PromptChoice(
                new ChoiceId(choiceId),
                action.Description,
                [cardId],
                targets,
                parameters));
        }

        return choices;
    }

    private IReadOnlyList<PromptChoice> CreateResponseChoices(
        IReadOnlyList<Card> responseCards,
        CardKind requiredCardKind,
        CardKind? incomingCard = null,
        Func<Card, CardKind>? effectiveCardKindSelector = null,
        bool includeBagua = false,
        bool includeFactionDefense = false,
        bool includeFactionSlash = false,
        CharacterState? responder = null)
    {
        var responseName = CardCatalog.Get(requiredCardKind).DisplayName;
        var isDodge = requiredCardKind == CardKind.Dodge;
        var responseAction = isDodge ? "dodge" : "slash";
        var incomingName = incomingCard is { } incoming
            ? CardCatalog.Get(incoming).DisplayName
            : "这张牌";
        var isGroupAttack = incomingCard is CardKind.BarbarianAssault or CardKind.ArrowBarrage;
        var isSlashAttack = incomingCard is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash;
        var incomingAttackName = isGroupAttack || isSlashAttack
            ? $"【{incomingName}】"
            : "【杀】";
        var damageDescription = isDodge
            ? isGroupAttack
                ? $"不响应，受到【{incomingName}】造成的伤害。"
                : $"不响应，受到{incomingAttackName}造成的伤害。"
            : isGroupAttack
                ? $"不打出【杀】，受到【{incomingName}】造成的伤害。"
                : "不打出【杀】，受到【决斗】造成的伤害。";
        var choices = responseCards
            .SelectMany(card =>
            {
                var effectiveCardKind = effectiveCardKindSelector?.Invoke(card) ?? requiredCardKind;
                var cardDescription = IsNativeResponseCard(card, requiredCardKind)
                    ? $"打出【{responseName}】"
                    : $"将【{card.DisplayName}】当作【{responseName}】";
                var description = isDodge
                    ? isGroupAttack
                        ? $"{cardDescription}，响应【{incomingName}】。"
                        : ActiveCardAttack is {} uncancelable && IsSlashDodgeCancellationPrevented(uncancelable)
                            ? $"{cardDescription}，响应这次{incomingAttackName}；此杀不能被抵消。"
                            : $"{cardDescription}，抵消这次{incomingAttackName}。"
                    : isGroupAttack
                        ? $"{cardDescription}，响应【{incomingName}】。"
                        : $"{cardDescription}，继续应战【决斗】。";
                var programSources = responder is null
                    ? []
                    : GetProgramViewAsConversions(responder, card, effectiveCardKind, forResponse: true);
                var hasIdentity = responder is not null && HasProgramCardIdentity(responder, card);
                var identitySources = responder is null
                    ? []
                    : GetProgramCardIdentitySources(
                        responder, card, effectiveCardKind, forResponse: true);
                var includeUnspecified = !hasIdentity &&
                    IsNativeResponseCard(card, requiredCardKind);
                var sources = new List<CardConversionSource?>();
                if (includeUnspecified) sources.Add(null);
                if (hasIdentity) sources.AddRange(identitySources);
                else
                {
                    sources.AddRange(programSources);
                }
                return sources.Select((source, index) =>
                {
                    var parameters = new Dictionary<string, string>
                    {
                        ["response"] = responseAction,
                        ["required-card"] = responseName,
                        ["response-card-kind"] = effectiveCardKind.ToString()
                    };
                    if (source is not null) AddConversionParameters(parameters, source);
                    var suffix = source is null ? string.Empty : $".conversion-{index}";
                    return new PromptChoice(
                        new ChoiceId($"respond.{responseAction}.card-{card.Id}{suffix}"),
                        DescribeConversion(source, description),
                        [card.Id],
                        [],
                        parameters);
                });
            })
            .ToList();
        if (isDodge && responder is not null)
            choices.AddRange(ExtendedViewAsResponseChoices(responder, CardKind.Dodge));
        if (!isDodge && requiredCardKind == CardKind.Slash && responder is not null)
        {
            foreach (var pair in GetZhangbaSlashPairs(responder))
            {
                choices.Add(new PromptChoice(
                    new ChoiceId($"respond.zhangba-slash.cards-{pair[0].Id}-{pair[1].Id}"),
                    isGroupAttack
                        ? $"发动【丈八蛇矛】，将两张手牌当【杀】响应【{incomingName}】。"
                        : "发动【丈八蛇矛】，将两张手牌当【杀】继续应战【决斗】。",
                    [pair[0].Id, pair[1].Id],
                    [],
                    new Dictionary<string, string>
                    {
                        ["response"] = "zhangba-slash",
                        ["required-card"] = responseName,
                        ["response-card-kind"] = CardKind.Slash.ToString(),
                        ["equipment"] = CardKind.ZhangbaSerpentSpear.ToString()
                    }));
            }
            foreach (var selection in GetProgramMultiCardViewAsSelections(
                         responder, CardKind.Slash, forResponse: true))
            {
                var cardIds = selection.Cards.Select(card => card.Id).ToArray();
                var parameters = new Dictionary<string, string>
                {
                    ["response"] = "program-view-as-slash",
                    ["required-card"] = responseName,
                    ["response-card-kind"] = CardKind.Slash.ToString()
                };
                AddConversionParameters(parameters, selection.Source);
                choices.Add(new PromptChoice(
                    new ChoiceId($"respond.program-view-as-slash.{selection.Source.SkillId.Length}:" +
                                 $"{selection.Source.SkillId}.{selection.Source.BindingId}.cards-{string.Join('-', cardIds)}"),
                    isGroupAttack
                        ? $"发动【{ProgramConversionName(selection.Source)}】，将 {cardIds.Length} 张手牌当【杀】响应【{incomingName}】。"
                        : $"发动【{ProgramConversionName(selection.Source)}】，将 {cardIds.Length} 张手牌当【杀】继续应战【决斗】。",
                    cardIds,
                    [],
                    parameters));
            }
        }
        if (isDodge && responder is not null) choices.AddRange(ProgramDodgeResponseChoices(responder));
        if (isDodge && includeBagua)
        {
            choices.Add(new PromptChoice(
                new ChoiceId("respond.bagua"),
                isSlashAttack
                    ? ActiveCardAttack is {} uncancelableBagua && IsSlashDodgeCancellationPrevented(uncancelableBagua)
                        ? $"发动【八卦阵】判定，红色牌视为打出【闪】响应{incomingAttackName}；此杀不能被抵消。"
                        : $"发动【八卦阵】判定，红色牌视为打出【闪】抵消{incomingAttackName}。"
                    : "发动【八卦阵】判定，红色牌视为打出【闪】。",
                [],
                [],
                new Dictionary<string, string>
                {
                    ["response"] = "bagua",
                    ["required-card"] = responseName,
                    ["equipment"] = CardKind.BaguaFormation.ToString()
                }));
        }

        if (isDodge && includeFactionDefense)
        {
            var policySource = GetFactionResponsePolicy(responder!, CardKind.Dodge)!;
            choices.Add(new PromptChoice(
                new ChoiceId("respond.hujia"),
                $"发动【{FactionPolicyName(policySource)}】，依次询问其他{GetFactionName(policySource.FactionId)}势力角色是否替你打出【闪】。",
                [],
                [],
                new Dictionary<string, string>
                {
                    ["response"] = "faction-defense-request",
                    ["required-card"] = responseName,
                    ["skill"] = policySource.SkillId
                }));
        }

        if (!isDodge && requiredCardKind == CardKind.Slash && includeFactionSlash)
        {
            var policySource = GetFactionResponsePolicy(responder!, CardKind.Slash)!;
            choices.Add(new PromptChoice(
                new ChoiceId("respond.jijiang"),
                $"发动【{FactionPolicyName(policySource)}】，依次询问其他{GetFactionName(policySource.FactionId)}势力角色是否替你打出【杀】。",
                [],
                [],
                new Dictionary<string, string>
                {
                    ["response"] = "faction-slash-request",
                    ["required-card"] = responseName,
                    ["skill"] = policySource.SkillId
                }));
        }

        choices.Add(new PromptChoice(
            new ChoiceId("respond.take-damage"),
            damageDescription,
            [],
            [],
            new Dictionary<string, string>
            {
                ["response"] = "take-damage"
            }));
        return choices;
    }

    private void RefreshNationalResponseDecision()
    {
        if (!IsNationalWarMode ||
            _pendingDecision is not { Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash } pending ||
            pending.RequiredCardKind is not { } requiredCardKind)
        {
            return;
        }

        var responder = _players[pending.PlayerSeat];
        var responseCards = GetResponseCards(responder, requiredCardKind);
        var includeBagua = pending.Choices.Any(choice =>
            choice.Parameters.TryGetValue("response", out var response) && response == "bagua");
        _pendingDecision = pending with
        {
            PromptId = CreatePromptId(),
            ValidCardIds = responseCards.Select(card => card.Id).ToArray(),
            Choices = CreateResponseChoices(
                responseCards,
                requiredCardKind,
                pending.IncomingCard,
                card => GetEffectiveResponseKind(responder, card, requiredCardKind),
                includeBagua,
                responder: responder)
        };
    }

    private void BeginDiscardPhase()
    {
        _phase = TurnPhase.Discard;
        StartActualDiscardRecoveryPhase(ActualDiscardRecoveryPhaseKind.Discard, _currentSeat);
        _fullDiscardPhaseSuitTurn = -1;
        _fullDiscardPhaseSuits.Clear();
        _discardPhaseHandDiscardTurnNumber = -1;
        _discardPhaseHandDiscardIds.Clear();
        _status = EngineStatus.Running;
        var current = _players[_currentSeat];
        AddLog("PhaseChanged", $"{current.Name} 进入弃牌阶段。", _currentSeat);
        AdvanceEventRulesAndQueueFact(new PhaseChangedEvent(_phase, _currentSeat));
        if (TryBeginDiscardPhaseProgramWindow(current)) return;
        CompleteDiscardPhaseAfterProgramWindow(current);
    }

    private void CompleteDiscardPhaseAfterProgramWindow(CharacterState current)
    {
        if (_pendingTurnDelayedEffects.HasFlag(DelayedTurnEffects.SkipDiscardPhase))
        {
            AddLog("DelayedCardEffect", $"{current.Name} 跳过弃牌阶段。", current.Seat);
            EndTurn();
            return;
        }
        if (!_usedOrPlayedSlashDuringPlayPhase &&
            CardPolicies(current, SkillProgramCardPolicyKind.OfferSkipDiscard).Any())
        {
            BeginSkipDiscardPolicyChoice(current);
        }
    }

    private void BeginSkipDiscardPolicyChoice(CharacterState current)
    {
        var (source, policy) = CardPolicies(current,
            SkillProgramCardPolicyKind.OfferSkipDiscard).First();
        var skillName = source.Definition.Name;
        _pendingDecision = new PendingDecision(
            DecisionKind.SkipDiscardPolicy,
            current.Seat,
            $"是否发动【{skillName}】，跳过本回合的弃牌阶段？",
            [],
            [])
        {
            PromptId = CreatePromptId(),
            Choices =
            [
                new PromptChoice(
                    new ChoiceId($"discard-policy.{source.SkillId}.{policy.Id}.use"),
                    $"发动【{skillName}】，跳过弃牌阶段并保留全部手牌。",
                    [],
                    [],
                    new Dictionary<string, string>
                    {
                        ["action"] = "discard-policy-use",
                        ["skill-id"] = source.SkillId,
                        ["policy-id"] = policy.Id
                    }),
                new PromptChoice(
                    new ChoiceId($"discard-policy.{source.SkillId}.{policy.Id}.skip"),
                    $"不发动【{skillName}】，按体力上限弃牌。",
                    [],
                    [],
                    new Dictionary<string, string>
                    {
                        ["action"] = "discard-policy-skip",
                        ["skill-id"] = source.SkillId,
                        ["policy-id"] = policy.Id
                    })
            ]
        };
        _status = current.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveSkipDiscardPolicyChoice(bool useSkill)
    {
        if (_pendingDecision is not { Kind: DecisionKind.SkipDiscardPolicy, PlayerSeat: var playerSeat } ||
            playerSeat != _currentSeat ||
            _phase != TurnPhase.Discard)
        {
            throw new InvalidOperationException("There is no discard-phase policy choice to resolve.");
        }

        var current = _players[playerSeat];
        var metadata = _pendingDecision.Choices[0].Parameters;
        var skillId = metadata["skill-id"];
        var policyId = metadata["policy-id"];
        if (!CardPolicies(current, SkillProgramCardPolicyKind.OfferSkipDiscard).Any(item =>
                item.Source.SkillId == skillId && item.Policy.Id == policyId) ||
            _usedOrPlayedSlashDuringPlayPhase)
            throw new InvalidOperationException("The chosen discard policy is no longer enabled.");
        ClearPendingDecision();
        AdvanceEventRulesAndQueueFact(new ProgramCardPolicyResolvedEvent(current.Seat, skillId, policyId, useSkill));
        AddLog(
            useSkill ? "SkillTriggered" : "SkillSkipped",
            useSkill
                ? $"{current.Name} 发动【{_contentRegistry.GetSkill(skillId).Name}】，跳过弃牌阶段。"
                : $"{current.Name} 未发动【{_contentRegistry.GetSkill(skillId).Name}】。",
            current.Seat);
        if (useSkill)
        {
            EndTurn();
        }
    }

    private void AutoDiscard(CharacterState player)
    {
        var hand = GetSelfDiscardableHand(player);
        var handLimit = GetHandLimit(player);
        var count = RequiredSelfHandLimitDiscard(player);
        if (count == 0)
        {
            return;
        }

        var discarded = hand
            .OrderBy(card => GetKeepValue(card, player))
            .ThenBy(card => card.Id)
            .Take(count)
            .ToArray();

        MoveCards(
            discarded,
            CardLocation.Hand(player.Seat),
            CardLocation.DiscardPile,
            CardMoveReasons.HandLimitDiscard);

        AddLog("CardsDiscarded", $"{player.Name} 自动弃置 {discarded.Length} 张牌，将手牌调整至体力上限。", player.Seat);
    }

    private static int GetKeepValue(Card card, CharacterState owner)
    {
        var value = CardCatalog.Get(card.Kind).HandKeepValue;
        return card.Kind == CardKind.Peach && owner.Hp < owner.MaxHp
            ? value + 50
            : value;
    }

    private void EndTurn()
    {

        var previous = _players[_currentSeat];
        CancelScheduledProgramPhase();
        StartActualDiscardRecoveryPhase(ActualDiscardRecoveryPhaseKind.Ending, previous.Seat);
        if (TryBeginTurnEndingBoundary(previous)) return;

        FinalizeEndTurn(previous);
    }

    private void FinalizeEndTurn(CharacterState previous)
    {
        if (previous.Seat != _currentSeat)
            throw new InvalidOperationException("The end-turn owner changed before finalization.");

        if (previous.HasAlcoholEffect)
        {
            previous.HasAlcoholEffect = false;
            AddLog("EffectExpired", $"{previous.Name} 的酒效在回合结束时失效。", previous.Seat);
            AdvanceEventRulesAndQueueFact(new AlcoholExpiredEvent(previous.Seat));
        }

        ExpireTurnCardUseEffects(_turnNumber, previous.Seat);
        ExpireProgramSuppressions(previous.Seat);
        ReturnProgramPojunHoldsAtTurnEnd();
        ReturnPrivateTurnHoldsAtTurnEnd();

        ExpireGiftRetentionObligations();
        _phase = TurnPhase.Finished;
        AddLog("TurnEnded", $"{previous.Name} 的回合结束。", previous.Seat);
        AdvanceEventRulesAndQueueFact(new TurnEndedEvent(_turnNumber, previous.Seat));
        if (TryBeginDeferredTurnEnd(previous, skipped: false)) return;
        AdvanceAfterDeferredTurnEnd(previous, skipped: false);
    }

    private void AdvanceAfterDeferredTurnEnd(CharacterState previous, bool skipped)
    {
        AdvanceActualTurnProgression(previous);
        _phase = TurnPhase.NotStarted;
        AdvanceRulesAndPublishState();
    }

    private int FindNextAliveSeat(int fromSeat)
    {
        for (var offset = 1; offset <= _playerCount; offset++)
        {
            var seat = (fromSeat + offset) % _playerCount;
            if (_players[seat].IsAlive)
            {
                return seat;
            }
        }

        return fromSeat;
    }

    private void FinalizePlayerDeath(DyingFrame dying)
    {
        var victim = _players[dying.VictimSeat];
        var killer = dying.KillerSeat is { } killerSeat
            ? _players[killerSeat]
            : null;
        BeginPlayerDeath(
            dying.FrameId,
            victim,
            killer,
            GetDyingAttack(dying),
            dying);
    }

    private void BeginPlayerDeath(
        long parentFrameId,
        CharacterState victim,
        CharacterState? killer,
        IDamageAttempt? attack,
        DyingFrame? dying,
        long? causingProgramSkillFrameId = null)
    {
        if (!victim.IsAlive)
        {
            if (causingProgramSkillFrameId is { } programFrameId)
            {
                AdvanceRuntimeProgram(programFrameId);
            }
            return;
        }

        var returnKind = (dying, causingProgramSkillFrameId) switch
        {
            (not null, null) when dying.FrameId == parentFrameId => DeathReturnKind.Dying,
            (null, not null) when causingProgramSkillFrameId == parentFrameId => DeathReturnKind.ProgramSkill,
            _ => throw new InvalidOperationException("A death resolution needs exactly one matching parent continuation.")
        };
        var deathFrameId = BeginDeath(parentFrameId, victim.Seat, killer?.Seat, returnKind);
        try
        {
            victim.Hp = 0;
            victim.IsAlive = false;
            ExpireDeadOwnerDamageModifiers(victim.Seat);
            if (victim.IsChained)
            {
                victim.IsChained = false;
                var chainResolutionId = attack?.ResolutionId ?? parentFrameId;
                var chainSourceSeat = attack?.SourceSeat ?? victim.Seat;
                AdvanceEventRulesAndQueueFact(new IronChainStateChangedEvent(
                    chainResolutionId,
                    chainSourceSeat,
                    victim.Seat,
                    IsChained: false));
            }
            victim.RoleRevealed = true;
            if (IsTeamMode)
            {
                victim.TeamRevealed = true;
            }
            if (IsNationalWarMode)
            {
                RevealNationalInformation(victim);
            }
            else
            {
                AdvanceEventRulesAndQueueFact(new RoleRevealedEvent(victim.Seat, victim.Role));
            }
            var deathCleanupCardIds = new List<int>();
            void MoveDiedCards(IEnumerable<Card> cards, CardLocation from, CardMoveReason reason)
            {
                var materialized = cards.ToArray();
                deathCleanupCardIds.AddRange(materialized.Select(item => item.Id));
                MoveCards(materialized, from, CardLocation.DiscardPile, reason);
            }
            MoveDiedCards(
                GetHand(victim),
                CardLocation.Hand(victim.Seat),
                CardMoveReasons.DeathDiscard);
            MoveDiedCards(
                GetEquipment(victim),
                CardLocation.Equipment(victim.Seat),
                CardMoveReasons.DeathEquipmentDiscard);
            MoveDiedCards(
                GetJudgment(victim),
                CardLocation.Judgment(victim.Seat),
                CardMoveReasons.DeathDiscard);
            MoveDiedCards(
                GetBuquWounds(victim),
                CardLocation.BuquWound(victim.Seat),
                CardMoveReasons.BuquDeathDiscard);
            MoveDiedCards(
                GetAuthority(victim),
                CardLocation.Authority(victim.Seat),
                CardMoveReasons.AuthorityDeathDiscard);
            MoveDiedCards(
                _cardZones.CardsAt(CardLocation.Chunlao(victim.Seat)),
                CardLocation.Chunlao(victim.Seat),
                CardMoveReasons.ChunlaoDeathDiscard);
            var publicPileSources = PublicPileSources(victim.Seat).ToArray();
            if (publicPileSources.Length == 0)
                MoveDiedCards(_cardZones.CardsAt(PublicPileLocation(victim.Seat)), PublicPileLocation(victim.Seat), new("skill-program.public-pile.owner-death"));
            else foreach (var source in publicPileSources)
                MoveDiedCards(PublicPileCards(source), source.Location, new("skill-program.public-pile.owner-death"));
            MoveDiedCards(_cardZones.CardsAt(new CardLocation(CardZoneKind.PublicDeferredPile, victim.Seat)),
                new CardLocation(CardZoneKind.PublicDeferredPile, victim.Seat), new CardMoveReason("skill-program.deferred-pile.owner-death"));
            _deferredPublicPileDeposits.RemoveAll(item => item.OwnerSeat == victim.Seat);
            foreach (var location in _cardZones.PrivateTurnHoldLocations.Where(l => l.OwnerSeat == victim.Seat))
                MoveDiedCards(_cardZones.CardsAt(location),location,new("skill.private-turn-hold.death"));
            MoveDiedCards(
                _cardZones.CardsAt(CardLocation.PojunHold(victim.Seat)),
                CardLocation.PojunHold(victim.Seat),
                CardMoveReasons.PojunHoldDeathDiscard);
            MoveDiedCards(_cardZones.CardsAt(new CardLocation(CardZoneKind.PrivateReserve, victim.Seat)),
                new CardLocation(CardZoneKind.PrivateReserve, victim.Seat), new CardMoveReason("program.private-reserve.death"));
            UpdateDeathFrame(deathFrameId, frame => frame with { CleanedUpCardIds = deathCleanupCardIds });

            AddLog(
                "PlayerDied",
                IsNationalWarMode
                    ? $"{victim.Name} 阵亡，属于{GetFactionName(victim.NationalFactionId!)}势力。"
                    : $"{victim.Name} 阵亡，身份是【{GetRoleName(victim.Role)}】。",
                killer?.Seat,
                victim.Seat);
            MarkConvertingGiftExactDeath(deathFrameId, victim.Seat);
            AdvanceEventRulesAndQueueFact(new PlayerDiedEvent(victim.Seat, killer?.Seat));
            if (killer is { } killerState && killerState.Seat == _currentSeat && _phase == TurnPhase.Play)
                _playPhaseKillCountByCurrentPlayer++;
            ResetHengyeAfterKill(killer);
            if (!IsNationalWarMode)
            {
                NotifyAiOfDeath(killer, victim);
            }

            if (!IsTeamMode && killer is { IsAlive: true })
            {
                if (victim.Role == Role.Rebel)
                {
                    DrawCards(killer, 3, log: true);
                    AddLog("KillReward", $"{killer.Name} 击杀反贼，摸三张牌。", killer.Seat, victim.Seat);
                }
                else if (killer.Role == Role.Lord && victim.Role == Role.Loyalist)
                {
                    var penalty = GetHand(killer).ToArray();
                    MoveCards(
                        penalty,
                        CardLocation.Hand(killer.Seat),
                        CardLocation.DiscardPile,
                        CardMoveReasons.LordPenalty);

                    AddLog("LordPenalty", $"主公误杀忠臣，弃置全部 {penalty.Length} 张手牌。", killer.Seat, victim.Seat);
                }
            }

            var previousWinner = _winner;
            if (IsTeamMode)
            {
                _winnerTeamId = GameRules.EvaluateWinningTeam(_players.Select(player =>
                    new TeamLifeState(player.TeamId!, player.IsAlive)));
                _winner = _winnerTeamId is null
                    ? Winner.None
                    : GetWinnerForTeam(_winnerTeamId);
            }
            else if (IsNationalWarMode)
            {
                _winnerFactionId = GameRules.EvaluateWinningFaction(_players.Select(player =>
                    new FactionLifeState(player.NationalFactionId!, player.IsAlive)));
                _winner = _winnerFactionId is null
                    ? Winner.None
                    : GetWinnerForFaction(_winnerFactionId);
            }
            else
            {
                _winnerTeamId = null;
                _winnerFactionId = null;
                _winner = GameRules.EvaluateWinner(_players.Select(player =>
                    new PlayerLifeState(player.Role, player.IsAlive)));
            }

            if (_winner != previousWinner && _winner != Winner.None)
            {
                AdvanceEventRulesAndQueueFact(new WinnerDeterminedEvent(
                    _winner,
                    IsTeamMode ? _winnerTeamId : null,
                    IsNationalWarMode ? _winnerFactionId : null));
            }

            ContinueDeathResolution(deathFrameId);
        }
        catch
        {
            if (_resolutionStack.LastOrDefault() is DeathFrame { Id: var currentId } &&
                currentId == deathFrameId)
                PopResolutionFrame(deathFrameId, ResolutionFrameKind.Death);
            throw;
        }
    }

    private void CompleteGame()
    {
        foreach (var player in _players)
        {
            player.RoleRevealed = true;
            player.TeamRevealed = IsTeamMode;
            if (IsNationalWarMode)
            {
                RevealNationalInformation(player);
            }
        }

        _phase = TurnPhase.Finished;
        _status = EngineStatus.Completed;
        _pendingDecision = null;
        _pendingCardsMovedBatches.Clear();
        _pendingHpChanges.Clear();
        var winnerName = IsNationalWarMode
            ? _winnerFactionId is { } winnerFactionId
                ? $"{GetFactionName(winnerFactionId)}势力"
                : "未知势力"
            : GetWinnerName(_winner);
        AddLog("GameEnded", $"游戏结束：{winnerName}获胜。 ");
        AdvanceEventRulesAndQueueFact(new GameEndedEvent(
            _winner,
            IsTeamMode ? _winnerTeamId : null,
            IsNationalWarMode ? _winnerFactionId : null));
    }

    private void EndAsDraw(string reason)
    {
        if (ActiveCardAttack is not null ||
            ActiveGroupCard is not null ||
            ActiveFireAttack is not null ||
            _cardZones.Count(CardLocation.Processing) != 0)
        {
            throw new InvalidOperationException("A game cannot end as a draw during an active card resolution.");
        }

        _winner = Winner.Draw;
        _pendingCardsMovedBatches.Clear();
        _pendingHpChanges.Clear();
        foreach (var player in _players)
        {
            player.RoleRevealed = true;
            player.TeamRevealed = IsTeamMode;
            if (IsNationalWarMode)
            {
                RevealNationalInformation(player);
            }
        }

        _phase = TurnPhase.Finished;
        _status = EngineStatus.Completed;
        _pendingDecision = null;
        AddLog("GameEnded", $"{reason}，本局记为平局。");
        AdvanceEventRulesAndQueueFact(new WinnerDeterminedEvent(
            _winner,
            IsTeamMode ? _winnerTeamId : null,
            IsNationalWarMode ? _winnerFactionId : null));
        AdvanceEventRulesAndQueueFact(new GameEndedEvent(
            _winner,
            IsTeamMode ? _winnerTeamId : null,
            IsNationalWarMode ? _winnerFactionId : null));
        AdvanceRulesAndPublishState();
    }

    private IReadOnlyList<int> DrawCards(
        CharacterState player,
        int count,
        bool log,
        CardMoveReason? reason = null)
    {
        var drawnCardIds = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var card = DrawOne(player, reason ?? CardMoveReasons.Draw);
            if (card is null)
            {
                break;
            }

            drawnCardIds.Add(card.Id);
        }

        if (log && drawnCardIds.Count > 0)
        {
            AddLog("CardsDrawn", $"{player.Name} 摸了 {drawnCardIds.Count} 张牌。", player.Seat);
        }

        return drawnCardIds;
    }

    private Card? DrawOne(CharacterState player, CardMoveReason reason)
    {
        if (!EnsureDrawPile())
        {
            return null;
        }

        var drawPile = _cardZones.CardsAt(CardLocation.DrawPile);
        var card = reason != CardMoveReasons.InitialDeal && HasCardPolicy(player, SkillProgramCardPolicyKind.DrawFromBottom) ? drawPile[0] : drawPile[^1];
        MoveCard(card, CardLocation.DrawPile, CardLocation.Hand(player.Seat), reason);
        return card;
    }

    private Card? DrawOneToProcessing(CardMoveReason reason)
    {
        if (!EnsureDrawPile())
        {
            return null;
        }

        var card = _cardZones.CardsAt(CardLocation.DrawPile)[^1];
        MoveCard(card, CardLocation.DrawPile, CardLocation.Processing, reason);
        return card;
    }

    private bool EnsureDrawPile()
    {
        if (_cardZones.Count(CardLocation.DrawPile) > 0)
        {
            return true;
        }

        if (_cardZones.Count(CardLocation.DiscardPile) == 0)
        {
            return false;
        }

        MoveAllCards(
            CardLocation.DiscardPile,
            CardLocation.DrawPile,
            CardMoveReasons.Reshuffle);
        _cardZones.Shuffle(CardLocation.DrawPile, _random);
        AddLog("DeckReshuffled", "摸牌堆耗尽，洗混弃牌堆形成新的摸牌堆。");
        return true;
    }

    private void NotifyAiOfSlash(CharacterState source, CharacterState target)
    {
        NotifyAiOfPublicAttack(source, target, PublicAttackKind.Slash);
    }

    private void NotifyAiOfDuel(CharacterState source, CharacterState target)
    {
        NotifyAiOfPublicAttack(source, target, PublicAttackKind.Duel);
    }

    private void NotifyAiOfFireAttack(CharacterState source, CharacterState target)
    {
        NotifyAiOfPublicAttack(source, target, PublicAttackKind.FireAttack);
    }

    private void NotifyAiOfPublicAttack(
        CharacterState source,
        CharacterState target,
        PublicAttackKind kind)
    {
        if (IsNationalWarMode)
        {
            NotifyAiOfNationalAttack(source, target);
            return;
        }

        var lordSeat = IsTeamMode
            ? -1
            : _players.Single(player => player.Role == Role.Lord).Seat;
        var visibleTargetRole = IsTeamMode
            ? null
            : target.RoleRevealed || target.Role == Role.Lord
            ? target.Role
            : (Role?)null;
        var evidence = new PublicAttackEvidence(
            kind,
            source.Seat,
            target.Seat,
            lordSeat,
            visibleTargetRole);
        foreach (var brain in _aiBrains.Values)
        {
            if (kind == PublicAttackKind.FireAttack)
            {
                brain.ObserveFireAttack(evidence);
            }
            else
            {
                brain.ObservePublicAttack(evidence);
            }
        }
    }

    private void NotifyAiOfGroupAttack(CharacterState source, IReadOnlyList<int> targets)
    {
        if (IsNationalWarMode)
        {
            foreach (var targetSeat in targets)
            {
                NotifyAiOfPublicAttack(source, _players[targetSeat], PublicAttackKind.GroupAttack);
            }
            return;
        }
        foreach (var targetSeat in targets)
        {
            NotifyAiOfPublicAttack(source, _players[targetSeat], PublicAttackKind.GroupAttack);
        }
    }

    private void NotifyAiOfNationalAttack(CharacterState source, CharacterState target)
    {
        var visibleSourceFaction = source.FactionRevealed ? source.NationalFactionId : null;
        var visibleTargetFaction = target.FactionRevealed ? target.NationalFactionId : null;
        foreach (var brain in _aiBrains.Values)
        {
            brain.ObserveNationalAttack(
                source.Seat,
                target.Seat,
                _players[brain.Seat].NationalFactionId,
                visibleSourceFaction,
                visibleTargetFaction);
        }
    }

    private void NotifyAiOfDeath(CharacterState? killer, CharacterState victim)
    {
        foreach (var brain in _aiBrains.Values)
        {
            brain.ObserveDeath(killer?.Seat, victim.Role);
        }
    }

    private IReadOnlyList<Card> GetHand(CharacterState player) =>
        _cardZones.CardsAt(CardLocation.Hand(player.Seat));

    private IReadOnlyList<Card> GetWoodenOxGrain(CharacterState player) =>
        _cardZones.CardsAt(CardLocation.WoodenOxGrain(player.Seat));

    private IReadOnlyList<Card> GetBuquWounds(CharacterState player) =>
        _cardZones.CardsAt(CardLocation.BuquWound(player.Seat));

    private IReadOnlyList<Card> GetAuthority(CharacterState player) =>
        _cardZones.CardsAt(CardLocation.Authority(player.Seat));

    private int GetHandLimit(CharacterState player)
        => ConvertRuleValue(EvaluateHandLimit(player));

    private IReadOnlyList<Card> GetPlayableCards(CharacterState player) =>
        AppendRequestedDeckBasicCards(player, AvailableDeclarationCard(player) is {} declared ? GetHand(player).Append(declared).ToArray() :
        UsesFormalWoodenOx && GetEquipment(player).Any(card => card.Kind == CardKind.WoodenOx)
            ? GetHand(player).Concat(GetWoodenOxGrain(player)).ToArray()
            : GetHand(player));

    private IReadOnlyList<Card> GetEquipment(CharacterState player) =>
        GetEquipment(player.Seat);

    private IReadOnlyList<Card> GetEquipment(int seat) =>
        _cardZones.CardsAt(CardLocation.Equipment(seat));

    private bool HasTargetCard(CharacterState player) =>
        GetHand(player).Count > 0 ||
        GetEquipment(player).Count > 0 ||
        GetJudgment(player).Count > 0;

    private (Card Card, CardLocation Location)? FindPublicTargetCard(
        CharacterState target,
        int cardId)
    {
        var equipment = GetEquipment(target).SingleOrDefault(card => card.Id == cardId);
        if (equipment is not null)
        {
            return (equipment, CardLocation.Equipment(target.Seat));
        }

        var judgment = GetJudgment(target).SingleOrDefault(card => card.Id == cardId);
        return judgment is null
            ? null
            : (judgment, CardLocation.Judgment(target.Seat));
    }

    private IReadOnlyList<Card> GetJudgment(CharacterState player) =>
        _cardZones.CardsAt(CardLocation.Judgment(player.Seat));

    private IReadOnlyList<Card> GetJudgment(int seat) =>
        _cardZones.CardsAt(CardLocation.Judgment(seat));

    private bool HasBagua(CharacterState player) =>
        !IsArmorIneffectiveForTurn(player) && (GetEquipment(player).Any(card => card.Kind == CardKind.BaguaFormation) ||
        HasCardPolicy(player, SkillProgramCardPolicyKind.VirtualEquipment, CardKind.BaguaFormation) &&
        !GetEquipment(player).Any(card => EquipmentCatalog.Get(card.Kind).Slot == EquipmentSlot.Armor));

    private bool HasTengjia(CharacterState player) =>
        !IsArmorIneffectiveForTurn(player) && GetEquipment(player).Any(card => card.Kind == CardKind.Tengjia);

    private bool HasSilverLion(CharacterState player) =>
        !IsArmorIneffectiveForTurn(player) && GetEquipment(player).Any(card => card.Kind == CardKind.SilverLion);

    private bool CanRequestFactionDefense(CharacterState owner, CardAttackHandle attack) =>
        !attack.FactionDefenseAttempted &&
        owner.IsAlive &&
        GetFactionResponsePolicy(owner, CardKind.Dodge) is not null;

    private int GetRequiredResponseCount(
        CharacterState skillOwner,
        int sourceSeat,
        int responderSeat,
        CardKind incomingCard,
        CardKind requiredCardKind)
    {
        return GetProgramRequiredResponseCount(skillOwner, sourceSeat, responderSeat, incomingCard, requiredCardKind);
    }

    private bool CanRequestFactionSlashResponse(CharacterState owner, CardAttackHandle attack)
    {
        if (IsCardUseForbidden(owner.Seat, CardKind.Slash, CardActionType.Response) ||
            !owner.IsAlive ||
            GetFactionResponsePolicy(owner, CardKind.Slash) is null)
        {
            return false;
        }

        if (ActiveDuel is { } duel && SameAttackOwner(duel.Attack, attack))
        {
            return duel.ResponderSeat == owner.Seat && !duel.FactionSlashAttempted;
        }

        return !attack.FactionSlashAttempted;
    }

    private bool CanUseFactionSlashRequest(CharacterState owner, string providerFactionId)
    {
        if (GetFactionResponsePolicy(owner, CardKind.Slash) is not { } policy ||
            policy.FactionId != providerFactionId ||
            SlashKinds.All(kind => IsCardUseForbidden(owner.Seat, kind, CardActionType.Use)) ||
            !owner.IsAlive ||
            GetFactionProviderSeats(owner.Seat, providerFactionId).Count == 0)
        {
            return false;
        }

        return _players.Any(target => CanUseProvidedSlashTarget(owner, target));
    }

    private bool CanUseProvidedSlashTarget(
        CharacterState owner,
        CharacterState target,
        CardKind? effectiveKind = null, Suit? physicalSuit = null, bool allowAnyPhysicalSuit = true, bool? effectiveColor = null, int? effectiveRank = null) =>
        effectiveKind is { } kind
            ? target.IsAlive && target.Seat != owner.Seat &&
              CanSpendSlashUse(owner, target, ignoresCount: false, kind) &&
              (HasSlashUseDistanceBySuit(owner,kind,physicalSuit) || allowAnyPhysicalSuit && HasSlashUseDistanceBySuit(owner,kind,Suit.Diamond) || HasTurnRedSlashPolicyForColor(owner.Seat,kind,effectiveColor ?? SuitColor(physicalSuit)) || allowAnyPhysicalSuit && _turnCardUseEffects.HasRedSlashPolicy(_turnNumber,_currentSeat,owner.Seat) || HasCardDistanceExemption(owner, target, kind) ||
               HasUnlimitedTurnRuleModifier(owner.Seat, SkillRuleQuery.SlashDistanceLimit) ||
               IsWithinSpecificSlashRange(owner,target,kind,effectiveRank) || allowAnyPhysicalSuit && HasPotentialRankSlashRange(owner,target,kind)) &&
              !IsSlashProhibited(target)
            : SlashKinds.Any(candidate => CanUseProvidedSlashTarget(owner, target, candidate,physicalSuit,allowAnyPhysicalSuit,effectiveColor,effectiveRank));

    /// <summary>
    /// Zhijian activation filter: the target keeps at least one free equipment slot
    /// matching an equipment card in the owner's hand (gifts never replace equipment).
    /// </summary>
    private bool HasEmptyEquipmentSlotForOwnerHandEquipment(CharacterState owner, CharacterState target) =>
        !target.EquipmentAreaAbolished && GetHand(owner).Any(card => CanEnterEquipmentSlot(target.Seat, card));

    private IReadOnlyList<int> GetFactionProviderSeats(int ownerSeat, string factionId) =>
        Enumerable.Range(0, _playerCount)
            .Select(offset => (_currentSeat + offset) % _playerCount)
            .Where(seat => seat != ownerSeat &&
                            _players[seat].IsAlive &&
                            string.Equals(GetEffectiveFactionId(_players[seat]), factionId, StringComparison.Ordinal))
            .ToArray();

    private bool HasBlackSlashBarrier(CharacterState player) =>
        GetEquipment(player).Any(card => EquipmentCatalog.Get(card.Kind).BlocksBlackSlash);

    private bool HasArmor(CharacterState player) =>
        GetEquipment(player).Any(card => EquipmentCatalog.Get(card.Kind).Slot == EquipmentSlot.Armor);

    internal static bool IsBlackCardUse(IReadOnlyList<Card> physicalCards) =>
        physicalCards.Count > 0 &&
        physicalCards.All(card => card.Suit is Suit.Spade or Suit.Club);

    internal static bool CanYizhongNullify(IReadOnlyList<Card> physicalCards, bool hasArmor) =>
        !hasArmor && IsBlackCardUse(physicalCards);

    internal static bool IsTrickCardDamage(CardKind kind) =>
        CardCatalog.Get(kind).CategoryName == "锦囊牌";

    internal static bool CanWuyanPreventDamage(
        bool isClassicIdentityMode,
        CardKind kind,
        bool sourceHasWuyan,
        bool targetHasWuyan) =>
        isClassicIdentityMode &&
        IsTrickCardDamage(kind) &&
        (sourceHasWuyan || targetHasWuyan);

    private bool TryPreventWuyanDamage(IDamageAttempt attack, int amount)
    {
        if (attack.EffectiveCardKind is not { } cardKind)
        {
            return false;
        }

        var source = _players[attack.SourceSeat];
        var target = _players[attack.TargetSeat];
        var sourcePolicy = CardPolicies(source, SkillProgramCardPolicyKind.PreventTrickDamage,
            cardKind).FirstOrDefault().Source;
        var targetPolicy = CardPolicies(target, SkillProgramCardPolicyKind.PreventTrickDamage,
            cardKind).FirstOrDefault().Source ??
            CardPolicies(target, SkillProgramCardPolicyKind.PreventIncomingTrickDamage,
                cardKind).FirstOrDefault().Source;
        if (sourcePolicy is null && targetPolicy is null)
        {
            return false;
        }
        var skillOwner = sourcePolicy is not null ? source : target;
        var skill = sourcePolicy ?? targetPolicy!;

        AddLog(
            "DamagePrevented",
            $"{skillOwner.Name} 的【{skill.Definition.Name}】防止了【{CardCatalog.Get(cardKind).DisplayName}】造成的 {amount} 点伤害。",
            skillOwner.Seat,
            skillOwner.Seat == source.Seat ? target.Seat : source.Seat);
        AdvanceEventRulesAndQueueFact(new WuyanDamagePreventedEvent(
            attack.ResolutionId,
            source.Seat,
            target.Seat,
            cardKind,
            amount,
            skillOwner.Seat,
            skill.SkillId));
        return true;
    }

    private bool HasArmorBypass(CharacterState player) =>
        GetEquipmentRuleCards(player).Any(card => EquipmentCatalog.Get(card.Kind).IgnoresArmor);

    private bool HasProgramSkill(CharacterState player, string skillId) =>
        GetSkillBindingShard(player).ProgramInstances.Any(instance =>
            string.Equals(instance.SkillId, skillId, StringComparison.Ordinal));

    private PlayerSkillContext CreateSkillContext(CharacterState player, bool includeHandLimit = false) =>
        new(
            player.Seat,
            player.Hp,
            player.MaxHp,
            GetHand(player).Count,
            _phase,
            player.Seat == _currentSeat,
            player.IsFaceDown,
            player.IsChained,
            IsClassicIdentityMode,
            GetPublicProgramBooleanStates(player),
            GetProgramPublicCounters(player),
            includeHandLimit ? GetHandLimit(player) : null,
            includeHandLimit ? HasActuallyUsableHandCard(player) : null);

    private void CollectDiscardPhaseHandDiscard(Card card, CardLocation from, CardLocation to)
    {
        if (_phase != TurnPhase.Discard ||
            from is not { Zone: CardZoneKind.Hand, OwnerSeat: { } ownerSeat } || ownerSeat != _currentSeat ||
            to.Zone != CardZoneKind.DiscardPile)
        {
            return;
        }

        if (_discardPhaseHandDiscardTurnNumber != _turnNumber)
        {
            _discardPhaseHandDiscardTurnNumber = _turnNumber;
            _discardPhaseHandDiscardIds.Clear();
        }
        _discardPhaseHandDiscardIds.Add(card.Id);
    }

    /// <summary>
    /// Cards the current turn owner discarded from hand during this turn's discard
    /// phase. Zero unless the ledger still belongs to the turn in progress.
    /// </summary>
    private int TurnOwnerDiscardPhaseHandDiscardCount =>
        _discardPhaseHandDiscardTurnNumber == _turnNumber ? _discardPhaseHandDiscardIds.Count : 0;

    /// <summary>
    /// Guzheng: binds the turn owner's discard-phase hand discards that are still
    /// resting in the discard pile. Cards that left the pile meanwhile (shuffled
    /// into the deck, claimed by another skill) are excluded, mirroring the
    /// death-cleanup precedent. At most the earliest eight cards (discard order)
    /// bind because the subset-return primitive enumerates eight candidates.
    /// </summary>
    internal SkillProgramStepOutcome BindProgramDiscardPhaseDiscards(ProgramSkillFrame frame, string resultBind)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.OwnerSeat != frame.OwnerSeat || active.SkillId != frame.SkillId)
            throw new InvalidOperationException("A discard-phase bind requires the active program frame.");
        var context = active.WindowContext ??
            throw new InvalidOperationException("A discard-phase bind requires a trigger window context.");
        if (context.Window is not SkillProgramTriggerWindow.TurnEnding)
            throw new InvalidOperationException("A discard-phase bind requires a turnEnding window.");
        var discardPileIds = _cardZones.CardsAt(CardLocation.DiscardPile).Select(card => card.Id).ToHashSet();
        var poolIds = _discardPhaseHandDiscardTurnNumber == _turnNumber
            ? _discardPhaseHandDiscardIds.Where(discardPileIds.Contains)
                .Take(CardSubsetSelector.MaximumCandidateCount).ToArray()
            : [];
        SetProgramCardSet(frame.Id, resultBind, poolIds, SkillProgramCardSetVisibility.Public);
        return SkillProgramStepOutcome.Continue;
    }

    private void MoveCard(
        Card card,
        CardLocation from,
        CardLocation to,
        CardMoveReason reason, Action<CardMovementRecord>? beforeFact = null)
    {
        if (ClaimDeclarationPayment(card, from, to)) return;
        to = NormalizeProgramViewAsCostDestination(card, from, to, reason);
        if (card.IsGeneralWeapon && from.Zone == CardZoneKind.Equipment)
            to = CardLocation.OutsideGame;
        var adjacentIdentity = FreezeAdjacentDiscardPaymentIdentity(card, from, to, reason);
        var batch = BeginCardMovementBatch([from], [to]);
        var movements = new List<CardMovementRecord>(1);
        var committed = false;
        try
        {
            _cardZones.Move(card.Id, from, to);
            movements.Add(RecordMovement(card, from, to, reason, beforeFact, adjacentIdentity));
            ResolveEquipmentSkillGrant(card, from, to);
            ClearJudgmentEffectiveKindAfterMove(card, from, to);
            ResolveSilverLionRemoval(card, from, reason);
            ResolveWoodenOxMove(card, from, to);
            CollectDiscardPhaseHandDiscard(card, from, to);
            committed = true;
        }
        finally
        {
            CompleteCardMovementBatch(batch, movements, committed);
        }
    }

    private void MoveCards(
        IReadOnlyList<Card> cards,
        CardLocation from,
        CardLocation to,
        CardMoveReason reason)
    {
        if (from.Zone == CardZoneKind.Equipment && cards.Any(card => card.IsGeneralWeapon))
        {
            foreach (var card in cards) MoveCard(card, from, to, reason);
            return;
        }
        var batch = BeginCardMovementBatch([from], [to]);
        var adjacentIdentities = TracksAdjacentDiscardStorage ? cards.ToDictionary(card => card.Id, card => FreezeAdjacentDiscardPaymentIdentity(card, from, to, reason)) : null;
        var movements = new List<CardMovementRecord>(cards.Count);
        var committed = false;
        try
        {
            var moved = _cardZones.MoveMany(cards.Select(card => card.Id), from, to);
            foreach (var card in moved)
            {
                movements.Add(RecordMovement(card, from, to, reason, adjacentDiscardIdentity: adjacentIdentities?.GetValueOrDefault(card.Id)));
                ResolveEquipmentSkillGrant(card, from, to);
                ClearJudgmentEffectiveKindAfterMove(card, from, to);
                ResolveSilverLionRemoval(card, from, reason);
                ResolveWoodenOxMove(card, from, to);
                CollectDiscardPhaseHandDiscard(card, from, to);
            }
            committed = true;
        }
        finally
        {
            CompleteCardMovementBatch(batch, movements, committed);
        }
    }

    private void MoveAllCards(
        CardLocation from,
        CardLocation to,
        CardMoveReason reason)
    {
        if (from.Zone == CardZoneKind.Equipment && _cardZones.CardsAt(from).Any(card => card.IsGeneralWeapon))
        {
            MoveCards(_cardZones.CardsAt(from).ToArray(), from, to, reason);
            return;
        }
        var batch = BeginCardMovementBatch([from], [to]);
        var adjacentIdentities = TracksAdjacentDiscardStorage ? _cardZones.CardsAt(from).ToDictionary(card => card.Id, card => FreezeAdjacentDiscardPaymentIdentity(card, from, to, reason)) : null;
        var movements = new List<CardMovementRecord>();
        var committed = false;
        try
        {
            var cards = _cardZones.MoveAll(from, to);
            foreach (var card in cards)
            {
                movements.Add(RecordMovement(card, from, to, reason, adjacentDiscardIdentity: adjacentIdentities?.GetValueOrDefault(card.Id)));
                ResolveEquipmentSkillGrant(card, from, to);
                ClearJudgmentEffectiveKindAfterMove(card, from, to);
                ResolveSilverLionRemoval(card, from, reason);
                ResolveWoodenOxMove(card, from, to);
                CollectDiscardPhaseHandDiscard(card, from, to);
            }
            committed = true;
        }
        finally
        {
            CompleteCardMovementBatch(batch, movements, committed);
        }
    }

    private void ResolveWoodenOx(CharacterState actor, int storedCardId, int targetSeat)
    {
        var storedCard = GetHand(actor).Single(card => card.Id == storedCardId);
        MoveCard(storedCard, CardLocation.Hand(actor.Seat), CardLocation.WoodenOxGrain(actor.Seat), CardMoveReasons.WoodenOxStore);
        _woodenOxUsedThisTurn = true;
        AddLog("EquipmentEffect", $"{actor.Name} 将一张手牌扣置于【木牛流马】下。", actor.Seat);

        if (targetSeat < 0)
        {
            return;
        }

        if (_players[targetSeat].EquipmentAreaAbolished)
            throw new InvalidOperationException("Wooden Ox cannot enter an abolished equipment area.");

        var woodenOx = GetEquipment(actor).Single(card => card.Kind == CardKind.WoodenOx);
        MoveCard(woodenOx, CardLocation.Equipment(actor.Seat), CardLocation.Equipment(targetSeat), CardMoveReasons.WoodenOxTransfer);
        AddLog("EquipmentEffect", $"{actor.Name} 将【木牛流马】移动给 {_players[targetSeat].Name}。", actor.Seat);
    }

    private void ResolveWoodenOxMove(Card card, CardLocation from, CardLocation to)
    {
        if (!UsesFormalWoodenOx || card.Kind != CardKind.WoodenOx ||
            from is not { Zone: CardZoneKind.Equipment, OwnerSeat: { } ownerSeat })
        {
            return;
        }

        var grainFrom = CardLocation.WoodenOxGrain(ownerSeat);
        if (_cardZones.Count(grainFrom) == 0)
        {
            return;
        }

        if (to is { Zone: CardZoneKind.Equipment, OwnerSeat: { } targetSeat })
        {
            MoveAllCards(grainFrom, CardLocation.WoodenOxGrain(targetSeat), CardMoveReasons.WoodenOxTransfer);
        }
        else
        {
            MoveAllCards(grainFrom, CardLocation.DiscardPile, CardMoveReasons.WoodenOxGrainDiscard);
        }
    }

    private void ResolveSilverLionRemoval(Card card, CardLocation from, CardMoveReason reason)
    {
        if (!UsesFormalSilverLion ||
            card.Kind != CardKind.SilverLion ||
            from is not { Zone: CardZoneKind.Equipment, OwnerSeat: { } ownerSeat })
        {
            return;
        }

        var owner = _players[ownerSeat];
        if (!owner.IsAlive || owner.Hp >= owner.MaxHp || IsArmorIneffectiveForTurn(owner))
        {
            return;
        }

        var parentFrameId = _resolutionStack.LastOrDefault()?.Id ?? 0;
        if (TryQueueRecoveryReplacement(parentFrameId, ownerSeat, ownerSeat, 1,
            new(RecoveryAttemptProducer.SilverLion, MoveReason: reason))) return;
        var recoveryFrameId = BeginRecovery(parentFrameId, ownerSeat, ownerSeat, 1);
        try
        {
            owner.Hp++;
            AdvanceEventRulesAndQueueFact(new RecoveryAppliedEvent(ownerSeat, ownerSeat, 1, owner.Hp));
            AdvanceEventRulesAndQueueFact(new SilverLionRemovedRecoveryEvent(
                recoveryFrameId, ownerSeat, reason, 1, owner.Hp));
            AddLog("EquipmentEffect", $"{owner.Name} 失去【白银狮子】，回复 1 点体力。", ownerSeat);
        }
        finally
        {
            PopResolutionFrame(recoveryFrameId, ResolutionFrameKind.Recovery);
        }
    }

    private void ClearJudgmentEffectiveKindAfterMove(
        Card card,
        CardLocation from,
        CardLocation to)
    {
        if (from.Zone == CardZoneKind.Judgment && to.Zone != CardZoneKind.Judgment)
        {
            _judgmentEffectiveCardKinds.Remove(card.Id);
        }
    }

    private CardMovementRecord RecordMovement(
        Card card,
        CardLocation from,
        CardLocation to,
        CardMoveReason reason, Action<CardMovementRecord>? beforeFact = null, CardKind? adjacentDiscardIdentity = null)
    {
        var movement = new CardMovementRecord(
            ++_movementSequence,
            _turnNumber,
            card.Id,
            card.Kind,
            from,
            to,
            reason) { ActualPlaySlashLoss = CaptureActualPlaySlashLoss(card, from, to) };
        _cardMovements.Add(movement);
        beforeFact?.Invoke(movement);
        CaptureRequestedDeckBasicPaid(card, from, to);
        CaptureDiscardedEntityOrigin(card, movement);
        CaptureAdjacentDiscardOrigin(card, movement, adjacentDiscardIdentity);
        CollectFullDiscardPhaseSuit(card, movement);
        if (_started)
        {
            _pendingNotifications.Enqueue(new CardMovedNotification(movement));
            if (_suppressedCardMovedEventBatchIds.Count == 0)
                AdvanceEventRulesAndQueueFact(new CardMovedEvent(card.Id, card.Kind, from, to, reason));
        }
        return movement;
    }

    private bool IsPendingDamageProgramDying()
    {
        if (ActiveDamageTrigger is not { } trigger ||
            ActiveDying is not { ResumesProgramSkill: true } dying ||
            _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(frame =>
                frame.Id == dying.ParentFrameId) is not { } program ||
            program.WindowContext is not
            {
                Window: SkillProgramTriggerWindow.AfterDamageApplied,
                ParentFrameId: var parentFrameId
            } || parentFrameId != trigger.Id ||
            _resolutionStack.OfType<DyingFrame>().LastOrDefault(frame =>
                frame.Id == dying.FrameId) is not { } dyingFrame ||
            dyingFrame.ParentFrameId != program.Id)
            return false;

        return _resolutionStack.LastOrDefault() is DyingFrame topDying &&
                   topDying.Id == dying.FrameId ||
               _resolutionStack.LastOrDefault() is ProgramSkillFrame responseProgram &&
                   responseProgram.WindowContext is
                   {
                       Window: SkillProgramTriggerWindow.DyingResponse or
                           SkillProgramTriggerWindow.SelfDyingResponse,
                       ParentFrameId: var dyingParentId
                   } && dyingParentId == dying.FrameId;
    }

    private ResolutionFrame? DamageCursorEffectiveTop(bool includeNestedObservers = false)
    {
        var index = _resolutionStack.Count - 1;
        while (index >= 1 && (RequestedDeckBasicFrameRidesOn(_resolutionStack[index], _resolutionStack[index - 1]) || DamageFrameRidesOn(_resolutionStack[index], _resolutionStack[index - 1]) ||
               ProvenanceClaimTurnedOverEdge(_resolutionStack[index], _resolutionStack[index - 1]) ||
               CharacterTurnedOverFrameRidesOn(_resolutionStack[index], _resolutionStack[index - 1]) ||
               RecoveryReplacementFrameRidesOn(_resolutionStack[index], _resolutionStack[index - 1]) ||
               PileEquipmentFrameRidesOn(_resolutionStack[index], _resolutionStack[index - 1]) ||
               RandomEquipmentFrameRidesOn(_resolutionStack[index], _resolutionStack[index - 1]) ||
               RecoveryReplacementDamageObserverRidesOn(index) ||
               CharacterTurnedOverDamageObserverRidesOn(index) ||
               includeNestedObservers && DamageObserverRidesOn(_resolutionStack[index], _resolutionStack[index - 1])))
            index--;
        return index >= 0 ? _resolutionStack[index] : null;
    }

    private static bool DamageObserverRidesOn(ResolutionFrame ride, ResolutionFrame beneath) =>
        ride is ProgramSkillFrame { WindowContext: { } context } && context.ParentFrameId == beneath.Id && beneath switch
        {
            CardsMovedTriggerWindowFrame => context.Window is SkillProgramTriggerWindow.CardsMoved or
                SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.DiscardPileReceived,
            HpChangedTriggerWindowFrame => context.Window is SkillProgramTriggerWindow.AfterHpLost or
                SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHealthChanged,
            _ => false
        };

    private static bool DamageFrameRidesOn(ResolutionFrame ride, ResolutionFrame beneath) => ride switch
    {
        // Movement and HP-change windows legitimately ride on the in-flight
        // program frame their continuation suspended at; a verified ride does
        // not displace the underlying damage cursor.
        CardsMovedTriggerWindowFrame movement =>
            beneath is RecoveryReplacementFrame
                ? movement.ResumeRecoveryReplacementFrameId == beneath.Id && movement.Batch.ParentFrameId == beneath.Id
                : movement.Batch.AwaitingProgramFrameId == beneath.Id ||
                  movement.Batch.AwaitingProgramFrameId is null && movement.Batch.ParentFrameId == beneath.Id,
        HpChangedTriggerWindowFrame changed => beneath is RecoveryReplacementFrame
            ? changed.Continuation == PostEventContinuation.RecoveryReplacement &&
              changed.ResumeFrameId == beneath.Id && changed.Change.ParentFrameId == beneath.Id
            : changed.ResumeFrameId == beneath.Id,
        _ => false
    };

    private void AssertCoreInvariants()
    {
        AssertRequestedDeckBasicMaterials();
        var coreDecision = RequestedDeckBasicInvariantDecision();
        var coreTop = RequestedDeckBasicInvariantTop();
        var damageProgramDying = IsPlacedEquipmentBenefitProgramDying() || IsFireTargetBenefitProgramDying() || IsNamedAcquisitionProgramDying() || IsDynamicDiscardDamageProgramDying() || IsOwnTrickDrawProgramDying() || IsConditionalDiscardDuelProgramDying() || IsRecipientContestProgramDying() || IsProvenanceClaimProgramDying() || IsPendingDamageProgramDying() || IsAvailableBoundDamageProgramDying() || IsPaidDamageTargetMountDying() || IsPaidDamageTargetObtainDying() || IsDamageAppearanceDrawProgramDying() || IsRecoveryReplacementProgramDying() || IsCharacterTurnedOverProgramDying() || IsCappedHandRefreshProgramDying() || IsPaidHandRepaymentProgramDying() || IsBoundRankBonusMovementDying() || IsOwnedDamagePointJudgmentProgramDying() || IsPreventionDrawInsideDamageProgramDying() || IsSourceFactionYieldInsideDamageProgramDying() || IsDamageJudgmentSuitPaymentDying() || IsAppliedDamageBenefitDying() || IsSequentialDiscardProgramDying() || IsEquipmentDonationProgramDying() || IsSuitPreventionInsideDamageProgramDying() || IsSignedDamagePaymentProgramDying() || IsHalfHandPhaseDebtProgramDying() || IsPaidColorDamageClaimProgramDying() || IsActualEquipmentOrDiscardProgramDying() || IsForeignContestAidProgramDying() || IsPairBenefitProgramDying() || IsEndingPairSlashProgramDying() || IsPrepDiscardProgramDying() || IsSlashTargetBenefitProgramDying() || IsTargetPenaltyProgramDying() || IsCappedConversionBenefitProgramDying() || IsTieredRoundZeroRescueProgramDying();
        _turnCardUseEffects.AssertInvariants();
        AssertPaidHpLossModifiers();
        AssertCurrentTurnOwnSkillSuppressions();
        AssertPostEventProgramInvariants();
        AssertRecoveryReplacementInvariants();
        AssertPindianInvariant();
        AssertProgramCardWindowState();
        AssertCardEffectProgramState();
        AssertProgramJudgmentWindowState();
        AssertDiscardPromptInvariant();
        AssertYingboInvariant();
        AssertGrantedEntityDistancePolicies();
        AssertSharedSlashUseReturns();
        _cardZones.AssertInvariants(_initialCardCount + _generatedPhysicalCardIds.Count);

        foreach (var player in _players)
        {
            var equipment = GetEquipment(player);
            if (equipment.Any(card => !EquipmentCatalog.IsEquipment(card.Kind)))
            {
                throw new InvalidOperationException(
                    $"Player {player.Seat} equipment zone contains a non-equipment card.");
            }

            if (equipment.GroupBy(card => EquipmentCatalog.Get(card.Kind).Slot)
                .Any(group => group.Count() > player.EquipmentSlotCapacity(group.Key)))
            {
                throw new InvalidOperationException(
                    $"Player {player.Seat} exceeds an equipment slot's capacity.");
            }
        }

        foreach (var (cardId, effectiveKind) in _judgmentEffectiveCardKinds)
        {
            if (_cardZones.GetLocation(cardId).Zone != CardZoneKind.Judgment ||
                !IsDelayedCard(effectiveKind))
            {
                throw new InvalidOperationException(
                    $"Judgment effective card {cardId}/{effectiveKind} is outside a delayed judgment zone.");
            }
        }

        AssertRecipientScopedDamageStates();
        AssertOwnedDeathBenefitReturns();

        if (_resolutionStack.OfType<DeathFrame>().LastOrDefault() is { } currentDeath)
        {
            GetCurrentDeathFrame(currentDeath.Id);
            if (_resolutionStack.OfType<ProgramKillTriggerWindowFrame>().LastOrDefault() is not null ||
                _resolutionStack.Any(frame => frame is ProgramSkillFrame program &&
                    program.WindowContext is { Window: SkillProgramTriggerWindow.CharacterDied }))
            {
                AssertKillDiedProgramInvariant();
                return;
            }
            AssertOwnerDiedProgramInvariant();
            return;
        }

        AssertColorFireAttackReceipts();
        AssertDualColorDuels();
        AssertConditionalDiscardDuels();
        AssertPlacedEquipmentBenefits();
        AssertSelectedActorDuels();
        AssertDamageTargetDuels();
        var processing = _cardZones.CardsAt(CardLocation.Processing);
        var hasActiveCardResolution = _resolutionStack.OfType<CardUseFrame>().Any(frame => frame.ColorFireAttack?.PaidCardId is not null) || ActiveCardAttack is not null ||
            ActiveBorrowedSword is not null ||
            ActiveGroupCard is not null ||
            ActiveFireAttack is not null ||
            ActiveNullificationWindow is not null ||
            ActiveYingboGift is not null ||
            ActiveTargetCardSelection is not null ||
            ActiveJudgment is not null ||
            HasCharacterStateCardUseContinuation() ||
            _resolutionStack.Any(frame => frame is CardEffectBeforeApplyFrame or ProgramSkillFrame or ProgramCardTriggerWindowFrame or PindianFrame or HpChangedTriggerWindowFrame or RecoveryReplacementFrame or CardDeclarationFrame);
        if (!hasActiveCardResolution && processing.Count != 0)
        {
            throw new InvalidOperationException("Processing contains cards without an active resolution.");
        }

        if (AssertCardDeclarationInvariant()) return;

        if (ActiveCardAttack is { } attack &&
            !IsActiveAttackCardConsistent(attack, processing))
        {
            throw new InvalidOperationException(
                $"The active card resolution and Processing zone are inconsistent " +
                $"(attack={attack.ResolutionId}/{attack.EffectiveCardKind}, physical=[{string.Join(',', attack.PhysicalCards.Select(card => card.Id))}], " +
                $"processing=[{string.Join(',', processing.Select(card => card.Id))}], source={attack.SourceSeat},target={attack.TargetSeat},program={attack.ProgramSkillFrameId},duel={ActiveDuel?.ResolutionId},stack={string.Join(';',_resolutionStack.OfType<ProgramSkillFrame>().Select(f=>$"{f.Id}:{string.Join(',',f.SelectedTargetSeats)}"))}).");
        }

        if (ActiveBorrowedSword is { } borrowedSword)
        {
            var parentFrame = _resolutionStack.OfType<CardUseFrame>()
                .FirstOrDefault(frame => frame.Id == borrowedSword.ResolutionId);
            var parentCardInProcessing = IsTieredRoundZeroProcessingParent(borrowedSword.ResolutionId, borrowedSword.Card.Id) ||
                processing.Any(card => card.Id == borrowedSword.Card.Id);
            var responseWindow = _resolutionStack.OfType<ResponseWindowFrame>().LastOrDefault(frame =>
                frame.ParentFrameId == borrowedSword.ResolutionId);
            var awaitingOwnerChoice = borrowedSword.AwaitingSlashChoice &&
                coreDecision is { Kind: DecisionKind.RespondSlash } borrowedDecision &&
                borrowedDecision.PlayerSeat == borrowedSword.WeaponOwnerSeat &&
                responseWindow is not null &&
                responseWindow.ResponderSeat == borrowedSword.WeaponOwnerSeat;
            var awaitingFactionSlash = ActiveFactionCardRequest is { IsBorrowedSwordUse: true } borrowedFactionSlash &&
                (borrowedFactionSlash.AwaitingProviders || borrowedFactionSlash.AwaitingZhuqueFanChoice) &&
                SameContinuationOwner(borrowedFactionSlash.BorrowedSword, borrowedSword);
            var resolvingSlash = borrowedSword.ActiveAttack is { } borrowedAttack &&
                SameAttackOwner(ActiveCardAttack, borrowedAttack);
            if (parentFrame is null ||
                parentFrame.CardKind != CardKind.BorrowedSword ||
                !parentCardInProcessing ||
                (awaitingOwnerChoice ? 1 : 0) + (awaitingFactionSlash ? 1 : 0) + (resolvingSlash ? 1 : 0) != 1)
            {
                throw new InvalidOperationException(
                    "A Borrowed Sword continuation must retain its parent card and exactly one response or Slash continuation.");
            }
        }

        if (ActiveGroupCard is { Effect: GroupCardEffect.Recovery } recoveryGroup &&
            (IsTieredRoundZeroUse(recoveryGroup.ResolutionId) ? processing.Count != 0 :
                processing.Count != 1 || processing[0].Id != recoveryGroup.Card.Id))
        {
            throw new InvalidOperationException(
                "The active group recovery and Processing zone are inconsistent.");
        }

        if (ActiveGroupCard is { Effect: GroupCardEffect.PublicDraft } publicDraft &&
            (processing.Count != (IsTieredRoundZeroUse(publicDraft.ResolutionId) ? 0 : 1) + publicDraft.RevealedCardIds.Count ||
             !IsTieredRoundZeroUse(publicDraft.ResolutionId) && processing.All(card => card.Id != publicDraft.Card.Id) ||
             publicDraft.RevealedCardIds.Any(cardId => processing.All(card => card.Id != cardId))))
        {
            throw new InvalidOperationException(
                "The active FiveGrains draft and Processing zone are inconsistent.");
        }

        if (ActiveFireAttack is { } fireAttack)
        {
            var zeroFireAttack = IsTieredRoundZeroUse(fireAttack.Id);
            var expectedProcessingCount = zeroFireAttack ? 0 : 1;
            if (processing.Count != expectedProcessingCount ||
                !zeroFireAttack && processing.All(card => card.Id != fireAttack.CardId) ||
                (fireAttack.FireAttackSelection?.RevealedCardId is { } revealedId &&
                 fireAttack.ColorFireAttack?.PaidCardId is null && GetHand(_players[GetFireAttackTargetSeat(fireAttack)]).All(card => card.Id != revealedId)))
            {
                throw new InvalidOperationException(
                    "The active FireAttack and Processing zone are inconsistent.");
            }

            if (ActiveCardAttack is not null ||
                ActiveDuel is not null ||
                ActiveGroupCard is not null ||
                ActiveDying is not null ||
                ActiveDamageTrigger is not null ||
                ActiveTargetCardSelection is not null)
            {
                throw new InvalidOperationException(
                    "A FireAttack selection cannot coexist with another card continuation.");
            }

            if (coreTop is not CardUseFrame cardUse ||
                !ReferenceEquals(cardUse, fireAttack) ||
                cardUse.CardKind != CardKind.FireAttack)
            {
                throw new InvalidOperationException(
                    "A FireAttack selection must retain its CardUse frame as the stack top.");
            }

            var expectedKind = fireAttack.FireAttackSelection?.RevealedCardId is null
                ? DecisionKind.FireAttackReveal
                : DecisionKind.FireAttackDiscard;
            if (coreDecision is not { } fireDecision ||
                fireDecision.Kind != expectedKind ||
                fireDecision.PlayerSeat != (expectedKind == DecisionKind.FireAttackReveal
                    ? GetFireAttackTargetSeat(fireAttack)
                    : fireAttack.SourceSeat))
            {
                throw new InvalidOperationException(
                    "A FireAttack selection must retain a prompt for the current picker.");
            }

            var expectedFireStatus = _players[fireDecision.PlayerSeat].IsHuman
                ? EngineStatus.AwaitingHumanCardSelection
                : EngineStatus.Running;
            if (_status != expectedFireStatus)
            {
                throw new InvalidOperationException(
                    "A FireAttack selection status does not match its current picker.");
            }
        }

        if (ActiveNullificationWindow is { } nullification)
        {
            var responseWindow = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>()
                .LastOrDefault(frame => IsNullificationResponseProgramWindow(frame) &&
                    frame.ParentFrameId == nullification.Id);
            var nullificationCardUse = _resolutionStack.OfType<CardUseFrame>()
                .SingleOrDefault(frame => frame.Id == nullification.ParentFrameId);
            var completedResponseDying = TryGetCompletedNullificationDyingCosts(responseWindow, out var rescueCosts) ||
                TryGetResponseExchangeMovementDyingCosts(responseWindow, out rescueCosts);
            var counterspellPaymentRide = TryGetPolicyCounterspellPaymentRide(nullification, out var paymentRescueCosts);
            completedResponseDying |= counterspellPaymentRide && ActiveDying is not null;
            if (counterspellPaymentRide) rescueCosts = paymentRescueCosts;
            var expectedPhysicalCardIds = IsTieredRoundZeroUse(nullification.ParentFrameId) ? Array.Empty<int>() : IsDamageTargetDuelUse(nullification.ParentFrameId) ? DamageTargetDuelOuterProcessing(nullification.ParentFrameId) : (IsSelectedActorDuelUse(nullification.ParentFrameId) || IsDualColorDuelUse(nullification.ParentFrameId) || IsConditionalDiscardDuelUse(nullification.ParentFrameId)) ? Array.Empty<int>() : nullificationCardUse?.PhysicalCardIds is { Count: > 0 } physicalCardIds
                ? physicalCardIds
                : [nullification.EffectCardId];
            expectedPhysicalCardIds = expectedPhysicalCardIds.Where(id => !IsClaimedUseCardEntity(nullification.ParentFrameId,id)).ToArray();
            if (!processing.Select(card => card.Id).Order().SequenceEqual(expectedPhysicalCardIds.Concat(rescueCosts).Order()))
            {
                throw new InvalidOperationException(
                    "The active Nullification window and Processing zone are inconsistent.");
            }

            if (ActiveCardAttack is not null ||
                ActiveDuel is not null ||
                ActiveGroupCard is not null ||
                ActiveFireAttack is not null ||
                ActiveDying is not null && !completedResponseDying ||
                ActiveDamageTrigger is not null ||
                ActiveTargetCardSelection is not null)
            {
                throw new InvalidOperationException(
                    "A Nullification window cannot coexist with another card continuation.");
            }

            if (nullificationCardUse is null ||
                nullification.CandidateIndex < 0 ||
                nullification.CandidateIndex > nullification.CandidateSeats.Count ||
                nullification.ChainDepth < 0 ||
                nullification.Step != ResolutionFrameStep.AwaitingResponse)
            {
                throw new InvalidOperationException(
                    "A Nullification window must retain its response cursor below any response trigger.");
            }

            if (responseWindow is null && !counterspellPaymentRide)
            {
                var currentNullificationCards = nullification.CandidateIndex < nullification.CandidateSeats.Count
                    ? GetNullificationCards(_players[nullification.CandidateSeats[nullification.CandidateIndex]])
                        .Select(card => card.Id).ToArray()
                    : Array.Empty<int>();
                if (coreDecision is not { Kind: DecisionKind.Nullification } nullificationDecision ||
                    nullification.CandidateIndex >= nullification.CandidateSeats.Count ||
                    nullificationDecision.PlayerSeat != nullification.CandidateSeats[nullification.CandidateIndex] ||
                    nullificationDecision.SourceSeat != nullification.SourceSeat ||
                    nullificationDecision.IncomingCard != nullification.EffectCardKind ||
                    nullificationDecision.RequiredCardKind != CardKind.Nullification ||
                    !nullificationDecision.ValidCardIds.SequenceEqual(currentNullificationCards))
                {
                    throw new InvalidOperationException(
                        "A Nullification window must retain a prompt for its current responder.");
                }

                var expectedNullificationStatus = _players[nullificationDecision.PlayerSeat].IsHuman
                    ? EngineStatus.AwaitingHumanResponse
                    : EngineStatus.Running;
                if (_status != expectedNullificationStatus)
                {
                    throw new InvalidOperationException(
                        "A Nullification prompt status does not match its current responder.");
                }
            }
        }

        if (ActiveTargetCardSelection is { } targetCardSelection)
        {
            var target = _players[targetCardSelection.TargetSeat];
            var expectedSlots = Enumerable.Range(0, targetCardSelection.CandidateSlots.Count).ToArray();
            if (IsTieredRoundZeroProcessingParent(targetCardSelection.ParentFrameId, targetCardSelection.EffectCardId)
                ? processing.Count != 0 : processing.Count != 1 || processing[0].Id != targetCardSelection.EffectCardId)
            {
                throw new InvalidOperationException(
                    "The active hidden target-card selection and Processing zone are inconsistent.");
            }

            if (ActiveCardAttack is not null ||
                ActiveDuel is not null ||
                ActiveGroupCard is not null ||
                ActiveFireAttack is not null ||
                ActiveNullificationWindow is not null ||
                ActiveDying is not null ||
                ActiveDamageTrigger is not null ||
                ActiveJudgment is not null)
            {
                throw new InvalidOperationException(
                    "A hidden target-card selection cannot coexist with another continuation.");
            }

            if (!ReferenceEquals(coreTop, targetCardSelection) ||
                targetCardSelection.Step != ResolutionFrameStep.AwaitingResponse ||
                _resolutionStack.OfType<CardUseFrame>().LastOrDefault(
                    use => use.Id == targetCardSelection.ParentFrameId) is null ||
                !targetCardSelection.CandidateSlots.SequenceEqual(expectedSlots))
            {
                throw new InvalidOperationException(
                    "A hidden target-card selection must retain only its opaque slot cursor as the stack top.");
            }

            var expectedDecision = coreDecision;
            if (expectedDecision is not { Kind: DecisionKind.SelectTargetCard } decision ||
                decision.PlayerSeat != targetCardSelection.SourceSeat ||
                decision.SourceSeat != targetCardSelection.SourceSeat ||
                decision.TargetSeat != targetCardSelection.TargetSeat ||
                !decision.ValidCardIds.SequenceEqual([]) ||
                !decision.ValidTargetSeats.SequenceEqual([targetCardSelection.TargetSeat]) ||
                decision.Choices.Count != targetCardSelection.CandidateSlots.Count ||
                decision.Choices.Any(choice =>
                    choice.Cards.Count != 0 ||
                    !choice.Targets.SequenceEqual([targetCardSelection.TargetSeat]) ||
                    choice.Parameters.GetValueOrDefault("action") != "target-card-slot" ||
                    !choice.Parameters.TryGetValue("slot-index", out var slotText) ||
                    !int.TryParse(
                        slotText,
                        System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var slot) ||
                    slot < 0 ||
                    slot >= targetCardSelection.CandidateSlots.Count))
            {
                throw new InvalidOperationException(
                    "A hidden target-card selection must retain a private opaque-slot prompt.");
            }

            if (targetCardSelection.CandidateSlots.Count != GetHand(target).Count ||
                decision.Choices.Select(choice => int.Parse(
                    choice.Parameters["slot-index"],
                    System.Globalization.CultureInfo.InvariantCulture))
                    .OrderBy(slot => slot)
                    .SequenceEqual(expectedSlots) is false)
            {
                throw new InvalidOperationException(
                    "A hidden target-card selection cursor no longer matches the target hand count.");
            }

            var expectedSelectionStatus = _players[decision.PlayerSeat].IsHuman
                ? EngineStatus.AwaitingHumanCardSelection
                : EngineStatus.Running;
            if (_status != expectedSelectionStatus)
            {
                throw new InvalidOperationException(
                    "A hidden target-card prompt status does not match its source.");
            }
        }



        if (ActiveStoneAxe is { } stoneAxe)
        {
            var stoneAxeAttack = stoneAxe.Attack;
            var decision = coreDecision;
            var source = _players[stoneAxeAttack.SourceSeat];
            var target = _players[stoneAxeAttack.TargetSeat];
            var currentCandidates = GetStoneAxeDiscardCards(source).Select(card => card.Id).ToArray();
            var expectedChoiceCount = currentCandidates.Length * (currentCandidates.Length - 1) / 2 + 1;
            if (!UsesFormalStoneAxe ||
                !SameAttackOwner(ActiveCardAttack, stoneAxeAttack) ||
                !source.IsAlive ||
                !target.IsAlive ||
                !HasWeaponAbility(source, CardKind.StoneAxe) ||
                stoneAxeAttack.SuccessfulDodgeResponses != stoneAxeAttack.RequiredDodgeResponses ||
                !stoneAxe.CandidateCardIds.SequenceEqual(currentCandidates) ||
                coreTop is not CardUseFrame cardUse ||
                cardUse.Id != stoneAxeAttack.ResolutionId ||
                cardUse.SourceSeat != stoneAxeAttack.SourceSeat ||
                cardUse.CardKind != stoneAxeAttack.EffectiveCardKind ||
                cardUse.Step != ResolutionFrameStep.AwaitingResponse ||
                decision is not { Kind: DecisionKind.StoneAxe, IsPrivate: true } ||
                decision.PlayerSeat != stoneAxeAttack.SourceSeat ||
                decision.SourceSeat != stoneAxeAttack.SourceSeat ||
                decision.TargetSeat != stoneAxeAttack.TargetSeat ||
                decision.IncomingCard != stoneAxeAttack.EffectiveCardKind ||
                !decision.ValidCardIds.SequenceEqual(currentCandidates) ||
                decision.Choices.Count != expectedChoiceCount ||
                decision.Choices.Count(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "stone-axe-skip" &&
                    choice.Cards.Count == 0 &&
                    choice.Targets.Count == 0) != 1 ||
                decision.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "stone-axe-use" &&
                    (choice.Cards.Count != 2 ||
                     choice.Cards.Distinct().Count() != 2 ||
                     choice.Cards.Any(id => !currentCandidates.Contains(id)) ||
                     choice.Targets.Count != 0)))
            {
                throw new InvalidOperationException(
                    "A Stone Axe choice must retain its private two-card cost prompt and completed Dodge continuation.");
            }

            var expectedStoneAxeStatus = source.IsHuman
                ? EngineStatus.AwaitingHumanResponse
                : EngineStatus.Running;
            if (_status != expectedStoneAxeStatus)
            {
                throw new InvalidOperationException("A Stone Axe prompt status does not match its owner.");
            }
        }

        if (ActiveQinglongCrescentBlade is { } qinglong &&
            ActiveFactionCardRequest?.IsQinglongCrescentBladeUse != true)
        {
            var qinglongAttack = qinglong.Attack;
            var decision = coreDecision;
            var source = _players[qinglongAttack.SourceSeat];
            var target = _players[qinglongAttack.TargetSeat];
            var currentCandidates = GetQinglongCrescentBladeSlashCards(source, target)
                .Select(card => card.Id)
                .ToArray();
            var canRequestFactionSlash = CanRequestQinglongCrescentBladeFactionSlash(qinglong);
            if (!UsesFormalQinglongCrescentBlade ||
                !SameAttackOwner(ActiveCardAttack, qinglongAttack) ||
                qinglongAttack.SuccessfulDodgeResponses != qinglongAttack.RequiredDodgeResponses ||
                coreTop is not CardUseFrame cardUse ||
                cardUse.Id != qinglongAttack.ResolutionId ||
                cardUse.SourceSeat != qinglongAttack.SourceSeat ||
                cardUse.CardKind != qinglongAttack.EffectiveCardKind ||
                cardUse.Step != ResolutionFrameStep.AwaitingResponse ||
                decision is not { Kind: DecisionKind.QinglongCrescentBlade, IsPrivate: true } ||
                decision.PlayerSeat != qinglongAttack.SourceSeat ||
                decision.SourceSeat != qinglongAttack.SourceSeat ||
                decision.TargetSeat != qinglongAttack.TargetSeat ||
                decision.IncomingCard != qinglongAttack.EffectiveCardKind ||
                !decision.ValidCardIds.SequenceEqual(currentCandidates) ||
                !decision.ValidTargetSeats.SequenceEqual([target.Seat]) ||
                decision.Choices.Count != currentCandidates.Length + 1 + (canRequestFactionSlash ? 1 : 0) +
                    TieredRoundZeroForcedSlashChoices(source, target, qinglongAttack.ResolutionId, true).Count ||
                !AssistedChoicesEqual(decision.Choices.Where(c => c.Parameters.GetValueOrDefault("action") == "tiered-round-zero-forced-slash").ToArray(),
                    TieredRoundZeroForcedSlashChoices(source, target, qinglongAttack.ResolutionId, true)) ||
                decision.Choices.Count(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "qinglong-skip" &&
                    choice.Cards.Count == 0 &&
                    choice.Targets.Count == 0) != 1 ||
                !decision.Choices.Where(choice =>
                        choice.Parameters.GetValueOrDefault("action") == "qinglong-slash")
                    .SelectMany(choice => choice.Cards)
                    .SequenceEqual(currentCandidates) ||
                decision.Choices.Where(choice =>
                        choice.Parameters.GetValueOrDefault("action") == "qinglong-slash")
                    .Any(choice => choice.Cards.Count != 1 || !choice.Targets.SequenceEqual([target.Seat])) ||
                decision.Choices.Count(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "qinglong-jijiang" &&
                    choice.Cards.Count == 0 &&
                    choice.Targets.SequenceEqual([target.Seat])) != (canRequestFactionSlash ? 1 : 0))
            {
                throw new InvalidOperationException(
                    "A Qinglong Crescent Blade choice must retain its private exact-Slash prompt and completed Dodge continuation.");
            }

            var expectedQinglongStatus = source.IsHuman
                ? EngineStatus.AwaitingHumanResponse
                : EngineStatus.Running;
            if (_status != expectedQinglongStatus)
            {
                throw new InvalidOperationException("A Qinglong Crescent Blade prompt status does not match its owner.");
            }
        }

        if (ActiveIceSword is { } iceSword)
        {
            var iceSwordAttack = iceSword.Attack;
            var source = _players[iceSwordAttack.SourceSeat];
            var target = _players[iceSwordAttack.TargetSeat];
            var handCount = GetHand(target).Count;
            var equipmentIds = GetEquipment(target).Where(card => !IsForeignEquipmentDiscardPrevented(
                iceSwordAttack.SourceSeat, card, CardLocation.Equipment(target.Seat), OwnedCardMoveIntent.Discard))
                .Select(card => card.Id).ToArray();
            var decision = coreDecision;
            var discardChoices = decision?.Choices.Where(choice =>
                choice.Parameters.GetValueOrDefault("action") == "ice-sword-discard").ToArray() ?? [];
            var expectedSkipCount = iceSword.Activated ? 0 : 1;
            var handSlots = discardChoices
                .Where(choice => choice.Parameters.GetValueOrDefault("target-zone") == "hand")
                .Select(choice => choice.Parameters.GetValueOrDefault("slot-index"))
                .ToArray();
            if (!UsesFormalIceSword ||
                !SameAttackOwner(ActiveCardAttack, iceSwordAttack) ||
                !iceSwordAttack.IceSwordAttempted ||
                !iceSwordAttack.DamageAmountFinalized ||
                iceSwordAttack.IsChainPropagation ||
                !HasWeaponAbility(source, CardKind.IceSword) ||
                coreTop is not CardUseFrame cardUse ||
                cardUse.Id != iceSwordAttack.ResolutionId ||
                cardUse.Step != ResolutionFrameStep.AwaitingResponse ||
                decision is not { Kind: DecisionKind.IceSword, IsPrivate: true } ||
                decision.PlayerSeat != iceSwordAttack.SourceSeat ||
                decision.SourceSeat != iceSwordAttack.SourceSeat ||
                decision.TargetSeat != iceSwordAttack.TargetSeat ||
                !decision.ValidCardIds.SequenceEqual(equipmentIds) ||
                !decision.ValidTargetSeats.SequenceEqual([target.Seat]) ||
                discardChoices.Length != handCount + equipmentIds.Length ||
                decision.Choices.Count != discardChoices.Length + expectedSkipCount ||
                handSlots.Length != handCount ||
                !handSlots.SequenceEqual(Enumerable.Range(0, handCount)
                    .Select(slot => slot.ToString(System.Globalization.CultureInfo.InvariantCulture))) ||
                discardChoices.Any(choice => !choice.Targets.SequenceEqual([target.Seat])) ||
                decision.Choices.Count(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "ice-sword-damage" &&
                    choice.Cards.Count == 0 &&
                    choice.Targets.Count == 0) != expectedSkipCount ||
                iceSword.DiscardedCardIds.Count != (iceSword.Activated ? 1 : 0))
            {
                throw new InvalidOperationException(
                    "An Ice Sword choice must retain its private staged target-card prompt before Slash damage.");
            }

            var expectedIceSwordStatus = source.IsHuman
                ? EngineStatus.AwaitingHumanResponse
                : EngineStatus.Running;
            if (_status != expectedIceSwordStatus)
            {
                throw new InvalidOperationException("An Ice Sword prompt status does not match its owner.");
            }
        }

        if (ActiveQilinBow is { } qilinBow)
        {
            var qilinAttack = qilinBow.Attack;
            var source = _players[qilinAttack.SourceSeat];
            var target = _players[qilinAttack.TargetSeat];
            var mountIds = GetQilinBowTargetMounts(target).Select(card => card.Id).ToArray();
            var decision = coreDecision;
            var discardChoices = decision?.Choices.Where(choice =>
                choice.Parameters.GetValueOrDefault("action") == "qilin-bow-discard").ToArray() ?? [];
            if (!UsesFormalQilinBow ||
                !SameAttackOwner(ActiveCardAttack, qilinAttack) ||
                !qilinAttack.QilinBowAttempted ||
                !qilinAttack.DamageAmountFinalized ||
                qilinAttack.IsChainPropagation ||
                !HasWeaponAbility(source, CardKind.QilinBow) ||
                mountIds.Length == 0 ||
                coreTop is not CardUseFrame cardUse ||
                cardUse.Id != qilinAttack.ResolutionId ||
                cardUse.Step != ResolutionFrameStep.AwaitingResponse ||
                decision is not { Kind: DecisionKind.QilinBow, IsPrivate: true } ||
                decision.PlayerSeat != qilinAttack.SourceSeat ||
                decision.SourceSeat != qilinAttack.SourceSeat ||
                decision.TargetSeat != qilinAttack.TargetSeat ||
                !decision.ValidCardIds.SequenceEqual(mountIds) ||
                !decision.ValidTargetSeats.SequenceEqual([target.Seat]) ||
                !discardChoices.SelectMany(choice => choice.Cards).SequenceEqual(mountIds) ||
                discardChoices.Any(choice =>
                    choice.Cards.Count != 1 || !choice.Targets.SequenceEqual([target.Seat])) ||
                decision.Choices.Count != mountIds.Length + 1 ||
                decision.Choices.Count(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "qilin-bow-skip" &&
                    choice.Cards.Count == 0 &&
                    choice.Targets.Count == 0) != 1)
            {
                throw new InvalidOperationException(
                    "A Qilin Bow choice must retain its private exact-mount prompt before Slash damage.");
            }

            var expectedQilinStatus = source.IsHuman
                ? EngineStatus.AwaitingHumanResponse
                : EngineStatus.Running;
            if (_status != expectedQilinStatus)
            {
                throw new InvalidOperationException("A Qilin Bow prompt status does not match its owner.");
            }
        }


        if (ActiveCixiongDoubleSwords is { } cixiong)
        {
            var cixiongAttack = cixiong.Attack;
            var decision = coreDecision;
            var source = _players[cixiongAttack.SourceSeat];
            var target = _players[cixiongAttack.TargetSeat];
            var targetHandIds = GetHand(target).OrderBy(card => card.Id).Select(card => card.Id).ToArray();
            var sourceActivationPromptMatches =
                cixiong.Stage == CixiongDoubleSwordsStage.SourceActivation &&
                decision?.PlayerSeat == source.Seat &&
                decision.ValidCardIds.Count == 0 &&
                decision.ValidTargetSeats.Count == 0 &&
                decision.Choices.Count == 2 &&
                decision.Choices.All(choice => choice.Cards.Count == 0 && choice.Targets.Count == 0) &&
                decision.Choices.Count(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "cixiong-use") == 1 &&
                decision.Choices.Count(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "cixiong-skip") == 1;
            var targetChoicePromptMatches =
                cixiong.Stage == CixiongDoubleSwordsStage.TargetChoice &&
                decision?.PlayerSeat == target.Seat &&
                decision.ValidCardIds.SequenceEqual(targetHandIds) &&
                decision.ValidTargetSeats.Count == 0 &&
                decision.Choices.Count == targetHandIds.Length + 1 &&
                decision.Choices.Count(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "cixiong-draw" &&
                    choice.Cards.Count == 0 &&
                    choice.Targets.Count == 0) == 1 &&
                decision.Choices.Where(choice =>
                        choice.Parameters.GetValueOrDefault("action") == "cixiong-discard")
                    .SelectMany(choice => choice.Cards)
                    .OrderBy(id => id)
                    .SequenceEqual(targetHandIds) &&
                decision.Choices.Where(choice =>
                        choice.Parameters.GetValueOrDefault("action") == "cixiong-discard")
                    .All(choice => choice.Cards.Count == 1 && choice.Targets.Count == 0);
            if (!UsesFormalCixiongDoubleSwords ||
                !SameAttackOwner(ActiveCardAttack, cixiongAttack) ||
                !source.IsAlive ||
                !target.IsAlive ||
                source.Gender == target.Gender ||
                !HasWeaponAbility(source, CardKind.CixiongDoubleSwords) ||
                cixiongAttack.CixiongDoubleSwordsResolved ||
                coreTop is not CardUseFrame cardUse ||
                cardUse.Id != cixiongAttack.ResolutionId ||
                cardUse.SourceSeat != cixiongAttack.SourceSeat ||
                cardUse.CardKind != cixiongAttack.EffectiveCardKind ||
                cardUse.Step != ResolutionFrameStep.Declared ||
                !cardUse.TargetSeats.SequenceEqual([cixiongAttack.TargetSeat]) ||
                decision is not { Kind: DecisionKind.CixiongDoubleSwords, IsPrivate: true } ||
                decision.SourceSeat != cixiongAttack.SourceSeat ||
                decision.TargetSeat != cixiongAttack.TargetSeat ||
                decision.IncomingCard != cixiongAttack.EffectiveCardKind ||
                !(sourceActivationPromptMatches || targetChoicePromptMatches))
            {
                throw new InvalidOperationException(
                    "A Cixiong Double Swords choice must retain its private staged prompt and exact Slash continuation.");
            }

            var responder = cixiong.Stage == CixiongDoubleSwordsStage.SourceActivation
                ? source
                : target;
            var expectedCixiongStatus = responder.IsHuman
                ? EngineStatus.AwaitingHumanResponse
                : EngineStatus.Running;
            if (_status != expectedCixiongStatus)
            {
                throw new InvalidOperationException(
                    "A Cixiong Double Swords prompt status does not match its responder.");
            }
        }

        if (coreDecision is { Kind: DecisionKind.SkipDiscardPolicy } kejiDecision)
        {
            var validPolicy = kejiDecision.Choices.Count == 2 &&
                kejiDecision.Choices[0].Parameters.TryGetValue("skill-id", out var skillId) &&
                kejiDecision.Choices[0].Parameters.TryGetValue("policy-id", out var policyId) &&
                kejiDecision.Choices.All(choice =>
                    choice.Parameters.GetValueOrDefault("skill-id") == skillId &&
                    choice.Parameters.GetValueOrDefault("policy-id") == policyId) &&
                CardPolicies(_players[kejiDecision.PlayerSeat],
                    SkillProgramCardPolicyKind.OfferSkipDiscard).Any(item =>
                    item.Source.SkillId == skillId && item.Policy.Id == policyId);
            if (_phase != TurnPhase.Discard ||
                _currentSeat != kejiDecision.PlayerSeat ||
                !_players[kejiDecision.PlayerSeat].IsAlive ||
                !validPolicy ||
                _usedOrPlayedSlashDuringPlayPhase ||
                kejiDecision.Choices.Count != 2 ||
                kejiDecision.Choices.Any(choice => choice.Cards.Count != 0 || choice.Targets.Count != 0) ||
                ActiveCardAttack is not null ||
                ActiveDuel is not null ||
                ActiveGroupCard is not null ||
                ActiveFireAttack is not null ||
                ActiveNullificationWindow is not null ||
                ActiveTargetCardSelection is not null ||
                ActiveDying is not null ||
                ActiveDamageTrigger is not null ||
                ActiveJudgment is not null ||
                _resolutionStack.Count != 0)
            {
                throw new InvalidOperationException(
                    "A discard policy must retain its exact prompt at a clean discard-phase boundary.");
            }

            var expectedKejiStatus = _players[kejiDecision.PlayerSeat].IsHuman
                ? EngineStatus.AwaitingHumanResponse
                : EngineStatus.Running;
            if (_status != expectedKejiStatus)
            {
                throw new InvalidOperationException("A discard-policy prompt status does not match its owner.");
            }
        }

        if (!_setupComplete && _resolutionStack.Count != 0)
        {
            throw new InvalidOperationException("Setup cannot retain an in-flight card resolution.");
        }

        if (!hasActiveCardResolution && _resolutionStack.Count != 0 &&
            !HasForeignActualTurnStartBoundary() && !HasProgramLifecycleBoundaryFrame() && !HasDrawPhaseObligationBoundaryFrame() && !HasTurnEndingBoundaryFrame() && !HasDeferredTurnEndBoundaryFrame() &&
            !HasPlayPhaseStartingBoundaryFrame() &&
            !HasCardsMovedProgramBoundaryFrame())
        {
            throw new InvalidOperationException($"A completed card resolution left frames on the stack: {string.Join(", ", _resolutionStack.Select(frame => $"{frame.Id}/{frame.Kind}/{frame.Step}"))}.");
        }

        if (ActiveCardAttack is { } pendingAttack)
        {
            if (!pendingAttack.IsDelayedJudgmentDamage &&
                !pendingAttack.IsProgramJudgmentDamage &&
                !pendingAttack.IsProgramSkillDamage &&
                !_resolutionStack.Any(frame =>
                    frame is CardUseFrame cardUse && cardUse.Id == pendingAttack.ResolutionId))
            {
                throw new InvalidOperationException(
                    "An active Slash continuation has no parent CardUse frame.");
            }

            if (pendingAttack.IsProgramSkillDamage &&
                !_resolutionStack.Any(frame =>
                    frame is ProgramSkillFrame programSkill &&
                    programSkill.Id == pendingAttack.ProgramSkillFrameId))
            {
                throw new InvalidOperationException(
                    "An active-program damage continuation has no parent ProgramSkill frame.");
            }

            if (IsCardAttackSuspendedByProgramDamage)
            {
                // The retained card owner is suspended beneath the program damage owner.
            }
            else if (HasTargetPenaltyUseObserver(pendingAttack.ResolutionId))
            { AssertTargetPenaltyWindows(); }
            else if (HasSlashTargetBenefitUseObserver(pendingAttack.ResolutionId))
            { AssertSlashTargetBenefitWindows(); }
            else if (HasActualUseTargetObserver(pendingAttack.ResolutionId))
            {
                AssertActualUseTargetPrograms();
            }
            else if (ProgramCardAttack is not null)
            {
                // The generic window owns the pending attack until all effects finish.
                AssertProgramCardWindowState();
        AssertCardEffectProgramState();
            }
            else if (HasPaidFactionRequestCostRecovery(ActiveFactionCardRequest))
            {
                // The exact paid request owns the response while its recovery and observer children finish.
            }
            else if (HasRecoveryPaidCardUseObserver(pendingAttack.ResolutionId))
            {
                if ((DamageCursorEffectiveTop() is not CardUseFrame paidUse ||
                    paidUse.Id != pendingAttack.ResolutionId || paidUse.RecoveryPaidContinuation is null) &&
                    !IsRecoveryReplacementProgramDying(pendingAttack.ResolutionId))
                    throw new InvalidOperationException("An active paid attack recovery must retain its exact card-use continuation.");
            }
            else if (ActiveStoneAxe is { } stoneAxeContinuation)
            {
                if (!SameAttackOwner(stoneAxeContinuation.Attack, pendingAttack) ||
                    coreTop is not CardUseFrame stoneAxeCardUse ||
                    stoneAxeCardUse.Id != pendingAttack.ResolutionId ||
                    stoneAxeCardUse.Step != ResolutionFrameStep.AwaitingResponse)
                {
                    throw new InvalidOperationException(
                        "An active Stone Axe choice must retain its Slash frame as the stack top.");
                }
            }
            else if (ActiveQinglongCrescentBlade is { } qinglongContinuation)
            {
                if (!SameAttackOwner(qinglongContinuation.Attack, pendingAttack) ||
                    coreTop is not CardUseFrame qinglongCardUse ||
                    qinglongCardUse.Id != pendingAttack.ResolutionId ||
                    qinglongCardUse.Step != ResolutionFrameStep.AwaitingResponse)
                {
                    throw new InvalidOperationException(
                        "An active Qinglong Crescent Blade choice must retain its Slash frame as the stack top.");
                }
            }
            else if (ActiveIceSword is { } iceSwordContinuation)
            {
                if (!SameAttackOwner(iceSwordContinuation.Attack, pendingAttack) ||
                    coreTop is not CardUseFrame iceSwordCardUse ||
                    iceSwordCardUse.Id != pendingAttack.ResolutionId ||
                    iceSwordCardUse.Step != ResolutionFrameStep.AwaitingResponse)
                {
                    throw new InvalidOperationException(
                        "An active Ice Sword choice must retain its Slash frame as the stack top.");
                }
            }
            else if (ActiveQilinBow is { } qilinContinuation)
            {
                if (!SameAttackOwner(qilinContinuation.Attack, pendingAttack) ||
                    coreTop is not CardUseFrame qilinCardUse ||
                    qilinCardUse.Id != pendingAttack.ResolutionId ||
                    qilinCardUse.Step != ResolutionFrameStep.AwaitingResponse)
                {
                    throw new InvalidOperationException(
                        "An active Qilin Bow choice must retain its Slash frame as the stack top.");
                }
            }
            else if (ActiveCixiongDoubleSwords is { } cixiongContinuation)
            {
                if (!SameAttackOwner(cixiongContinuation.Attack, pendingAttack) ||
                    coreTop is not CardUseFrame cixiongCardUse ||
                    cixiongCardUse.Id != pendingAttack.ResolutionId ||
                    cixiongCardUse.Step != ResolutionFrameStep.Declared)
                {
                    throw new InvalidOperationException(
                        "An active Cixiong Double Swords choice must retain its declared Slash frame as the stack top.");
                }
            }
            else if ((HasDynamicDiscardDamageAttackObserver(pendingAttack) || HasSignedDamagePaymentAttackObserver(pendingAttack)))
            {
                // Only this new paid root proves the complete contiguous child pipeline.
            }
            else if (pendingAttack.IsProgramJudgmentDamage)
            {
                if (pendingAttack.ProgramJudgmentFrameId is not { } programFrameId ||
                    ActiveJudgment is null ||
                    !_resolutionStack.OfType<ProgramJudgmentTriggerWindowFrame>()
                        .Any(frame => frame.Id == programFrameId &&
                                      frame.ParentFrameId == ActiveJudgment.Id))
                {
                    throw new InvalidOperationException(
                        "Configured judgment damage must retain its judgment trigger parent.");
                }
            }
            else if (ActiveJudgment is { } judgmentContinuation)
            {
                var judgmentFrame = _resolutionStack.OfType<JudgmentFrame>().LastOrDefault();
                var judgmentIsActive = coreTop switch
                {
                    JudgmentFrame current => current.Id == judgmentContinuation.Id,
                    ProgramJudgmentTriggerWindowFrame program =>
                        program.ParentFrameId == judgmentContinuation.Id,
                    ProgramSkillFrame program =>
                        program.WindowContext?.Judgment?.JudgmentFrameId == judgmentContinuation.Id ||
                        program.WindowContext?.JudgmentReplacement?.JudgmentFrameId == judgmentContinuation.Id,
                    _ => false
                };
                if (!SameAttackOwner(GetJudgmentAttack(judgmentContinuation),
                        pendingAttack) ||
                    judgmentFrame is null ||
                    !judgmentIsActive && !HasOwnedDamagePointJudgmentRide(judgmentContinuation.Id) && !HasDamageJudgmentSuitPaymentRide(judgmentContinuation.Id) ||
                    judgmentFrame.Id != judgmentContinuation.Id ||
                    judgmentFrame.ParentFrameId != judgmentContinuation.ParentFrameId)
                {
                    throw new InvalidOperationException(
                        "An active judgment continuation must retain its Judgment frame and active child window.");
                }
            }
            else if (ActiveYingboGift is
            {
                Continuation: YingboGiftContinuation.Attack,
                Attack: { } giftAttack
            })
            {
                if (!SameAttackOwner(giftAttack, pendingAttack) ||
                    coreTop is not CardUseFrame giftCardUse ||
                    giftCardUse.Id != pendingAttack.ResolutionId)
                {
                    throw new InvalidOperationException(
                        "An active Yingbo gift must retain its Slash frame as the stack top.");
                }
            }
            else if (ActiveDying is null &&
                ActiveDamageTrigger is null &&
                !_resolutionStack.OfType<BeforeDamageProgramWindowFrame>().Any())
            {
                if ((coreTop is not ResponseWindowFrame response ||
                    response.ParentFrameId != pendingAttack.ResolutionId) &&
                    !HasProgramDodgeResponseContinuation(pendingAttack))
                {
                    throw new InvalidOperationException(
                        "An active Slash must retain its response window as the stack top.");
                }
            }
            else if (_resolutionStack.OfType<BeforeDamageProgramWindowFrame>().LastOrDefault() is
            {
                Continuation: BeforeDamageProgramContinuation.Attack
            } beforeDamage)
            {
                var effectiveTop = DamageCursorEffectiveTop(includeNestedObservers: true);
                var topMatchesWindow = effectiveTop is BeforeDamageProgramWindowFrame topWindow &&
                    topWindow.Id == beforeDamage.Id;
                var topMatchesProgram = effectiveTop is ProgramSkillFrame topProgram &&
                    topProgram.WindowContext is
                    {
                        Window: SkillProgramTriggerWindow.BeforeDamageApplied,
                        ParentFrameId: var parentFrameId
                    } && parentFrameId == beforeDamage.Id;
                if (beforeDamage.SourceSeat != pendingAttack.SourceSeat ||
                    (beforeDamage.RedirectedTargetSeat ?? beforeDamage.TargetSeat) != pendingAttack.TargetSeat ||
                    !topMatchesWindow && !topMatchesProgram && !HasPreventionDrawDying(beforeDamage.Id) && !HasSourceFactionYieldObserver(beforeDamage.Id) && !HasSuitPreventionBenefitObserver(beforeDamage.Id) && !(HasDynamicDiscardDamageObserver(beforeDamage.Id) || HasSignedDamagePaymentObserver(beforeDamage.Id)) && !HasPaidColorDamageClaimDamageObserver(beforeDamage.Id) && !HasPlacedEquipmentBenefitDamageObserver(beforeDamage.Id) && !HasActualDiscardRecoveryBeforeDamageObserver(beforeDamage.Id) && !HasForeignContestAidDamageObserver(beforeDamage.Id) && !HasPrepDiscardDamageObserver(beforeDamage.Id) && !HasSlashBenefitDamageObserver(beforeDamage.Id) && !HasTargetPenaltyDamageObserver(beforeDamage.Id))
                {
                    throw new InvalidOperationException(
                        "An active before-damage program must retain its damage and parent window.");
                }
            }
            else if (ActiveDamageTrigger is { } triggerContinuation)
            {
                var effectiveTop = DamageCursorEffectiveTop(includeNestedObservers: HasPaidDamageTargetMountObserver(triggerContinuation.Id) || HasPaidDamageTargetObtainObserver(triggerContinuation.Id) || HasDamageAppearanceDrawObserver(triggerContinuation.Id) || HasCappedHandRefreshObserver(triggerContinuation.Id) || HasPaidHandRepaymentObserver(triggerContinuation.Id) || HasBoundRankBonusMovementObserver(triggerContinuation.Id) || HasOwnedDamagePointJudgmentObserver(triggerContinuation.Id) || HasDamageJudgmentSuitPaymentObserver(triggerContinuation.Id) || HasAppliedDamageBenefitObserver(triggerContinuation.Id) || HasFireTargetBenefitObserver(triggerContinuation.Id) || HasNamedAcquisitionDamageObserver(triggerContinuation.Id) || HasProvenanceClaimObserver(triggerContinuation.Id) || HasEquipmentDonationDamageObserver(triggerContinuation.Id) || HasPaidColorDamageClaimDamageObserver(triggerContinuation.Id) || HasHalfHandPaidDamageObserver(triggerContinuation.Id) || (HasDynamicDiscardDamageObserver(triggerContinuation.Id) || HasSignedDamagePaymentObserver(triggerContinuation.Id)) || HasPairBenefitDamageObserver(triggerContinuation.Id) || HasEndingPairSlashDamageObserver(triggerContinuation.Id) || HasActualDiscardRecoveryDamageObserver(triggerContinuation.Id) || HasForeignContestAidDamageObserver(triggerContinuation.Id) || HasPrepDiscardDamageObserver(triggerContinuation.Id) || HasSlashBenefitDamageObserver(triggerContinuation.Id) || HasTargetPenaltyDamageObserver(triggerContinuation.Id) || HasCappedConversionBenefitObserver(triggerContinuation.Id) || HasPlacedEquipmentBenefitDamageObserver(triggerContinuation.Id) || HasRecipientContestDamageObserver(triggerContinuation.Id));
                var topMatchesWindow = effectiveTop is DamageTriggerWindowFrame frame &&
                    frame.Id == triggerContinuation.Id &&
                    frame.ParentFrameId == triggerContinuation.ParentFrameId &&
                    frame.CandidateIndex == triggerContinuation.CandidateIndex;
                var topMatchesProgram = effectiveTop is ProgramSkillFrame program &&
                    program.WindowContext is
                    {
                        Window: SkillProgramTriggerWindow.DamageAppliedBeforeDying or
                            SkillProgramTriggerWindow.AfterDamageApplied,
                        ParentFrameId: var parentFrameId
                    } &&
                    parentFrameId == triggerContinuation.Id;
                var topMatchesProgramPindian = coreTop is PindianFrame pindian &&
                    _resolutionStack.Count >= 2 &&
                    _resolutionStack[^2] is ProgramSkillFrame pindianProgram &&
                    pindian.ParentFrameId == pindianProgram.Id &&
                    pindianProgram.WindowContext is
                    {
                        Window: SkillProgramTriggerWindow.AfterDamageApplied,
                        ParentFrameId: var pindianWindowId
                    } &&
                    pindianWindowId == triggerContinuation.Id;
                if (!SameAttackOwner(GetDamageTriggerAttack(triggerContinuation),
                        pendingAttack) ||
                    !topMatchesWindow && !topMatchesProgram && !topMatchesProgramPindian && !damageProgramDying)
                {
                    throw new InvalidOperationException(
                        "An active damage trigger window must retain its ordered cursor as the stack top.");
                }
            }
            else if (ActiveDying is { } dyingContinuation)
            {
                if (!SameAttackOwner(GetDyingAttack(dyingContinuation),
                        pendingAttack))
                {
                    throw new InvalidOperationException(
                        "The dying continuation does not belong to the active Slash.");
                }

                var topMatchesDying = coreTop is DyingFrame dying &&
                    dying.Id == dyingContinuation.FrameId &&
                    dying.ParentFrameId == dyingContinuation.ParentFrameId;
                var topMatchesDyingProgram = coreTop is ProgramSkillFrame
                {
                    WindowContext:
                    {
                        Window: SkillProgramTriggerWindow.DyingResponse or
                        SkillProgramTriggerWindow.SelfDyingResponse,
                        ParentFrameId: var dyingParentId
                    }
                } && dyingParentId == dyingContinuation.FrameId;

                // A dying rescue card use legitimately nests its own program card-trigger
                // windows between the rescue use and the Dying frame, the same way damage
                // triggers nest above their Damage frame. Walk the contiguous window chain
                // down and require it to anchor on the active dying continuation, either
                // directly on the Dying frame or through that rescue card use.
                var topMatchesDyingCardWindow = false;
                if (coreTop is ProgramCardTriggerWindowFrame topCardWindow)
                {
                    var windowIndex = _resolutionStack.Count - 1;
                    var currentWindow = topCardWindow;
                    while (windowIndex > 0 &&
                           _resolutionStack[windowIndex - 1] is ProgramCardTriggerWindowFrame beneath &&
                           beneath.Id == currentWindow.ParentFrameId)
                    {
                        windowIndex--;
                        currentWindow = beneath;
                    }

                    topMatchesDyingCardWindow = windowIndex > 0 && _resolutionStack[windowIndex - 1] switch
                    {
                        DyingFrame directDying => currentWindow.ParentFrameId == directDying.Id &&
                            directDying.Id == dyingContinuation.FrameId,
                        CardUseFrame { DyingResponse: not null } rescue =>
                            currentWindow.ParentFrameId == rescue.Id &&
                            rescue.DyingResponse!.ResolutionId == dyingContinuation.FrameId &&
                            windowIndex >= 2 &&
                            _resolutionStack[windowIndex - 2] is DyingFrame chainedDying &&
                            chainedDying.Id == dyingContinuation.FrameId,
                        _ => false
                    };
                }

                var dyingFrameIndex = _resolutionStack.FindLastIndex(frame => frame is DyingFrame dying &&
                    dying.Id == dyingContinuation.FrameId &&
                    dying.ParentFrameId == dyingContinuation.ParentFrameId);
                var nestedResponseUseOnDying = dyingFrameIndex >= 0 &&
                    _resolutionStack
                        .Skip(dyingFrameIndex + 1)
                        .All(frame => frame is CardUseFrame or ProgramCardTriggerWindowFrame or
                            ProgramSkillFrame or HpChangedTriggerWindowFrame);
                var nestedDyingEntry = dyingFrameIndex >= 0 && dyingFrameIndex + 1 < _resolutionStack.Count &&
                    _resolutionStack[dyingFrameIndex + 1] is ProgramLifecycleTriggerWindowFrame
                        { Window: SkillProgramTriggerWindow.DyingEntering, Continuation: ProgramLifecycleContinuation.ResumeDyingEntry } entry &&
                    entry.ResumeDyingFrameId == dyingContinuation.FrameId &&
                    _resolutionStack.Skip(dyingFrameIndex + 2).All(frame => frame is ProgramSkillFrame or
                        HpChangedTriggerWindowFrame or CardsMovedTriggerWindowFrame);
                if (!topMatchesDying && !topMatchesDyingProgram && !topMatchesDyingCardWindow &&
                    !nestedResponseUseOnDying && !nestedDyingEntry && !IsExactOtherDyingRecoveryRide(dyingContinuation.FrameId) &&
                    !IsExactDyingOwnedCardRide(dyingContinuation.FrameId) &&
                    !(dyingFrameIndex >= 0 && IsRoundPricedPileAlcoholRide(dyingFrameIndex, (DyingFrame)_resolutionStack[dyingFrameIndex])))
                {
                    var top = coreTop;
                    var topShape = top is ProgramSkillFrame diagnosticProgram
                        ? $"ProgramSkillFrame({diagnosticProgram.SkillId}, window {diagnosticProgram.WindowContext?.Window.ToString() ?? "none"}, owner {diagnosticProgram.OwnerSeat})"
                        : top?.GetType().Name ?? "empty";
                    throw new InvalidOperationException(
                        "An active dying continuation must retain its Dying frame as the stack top " +
                        $"(found {topShape}, continuation frame {dyingContinuation.FrameId}).");
                }
            }
            else
            {
                throw new InvalidOperationException(
                    "An active Slash continuation has an unsupported pending state.");
            }
        }

        AssertProgramSkillState();
        AssertActualUseTargetPrograms();
        AssertSlashTargetBenefitWindows();
        AssertTargetPenaltyWindows();
        AssertHpLossMaterialSlashes();
        AssertLostHpOwnedGifts();
        AssertEquipmentDonationPrograms();
        AssertActualEquipmentOrDiscardPrograms();
        AssertShownEntityTurnPrograms();
        AssertPaidColorDamageClaims();
        AssertProgramAttackState();
        AssertFinalTargetSlashReceipts();
        AssertCurrentUsePhysicalClaims();
        AssertSequentialTrickTargets();
        if (_activeCardMovementBatchIds.Count != 0)
            throw new InvalidOperationException("An atomic card-movement batch escaped its operation boundary.");
        if (ActiveDying is not null &&
            CurrentDamageAttempt is null &&
            !ActiveDying.ResumesProgramSkill)
        {
            throw new InvalidOperationException("A dying continuation must retain its active card resolution.");
        }

        if (ActiveJudgment is { } pendingJudgment)
        {
            var programJudgmentFrame = _resolutionStack
                .OfType<ProgramJudgmentTriggerWindowFrame>()
                .LastOrDefault();
            var belongsToActiveAttack =
                ActiveCardAttack is { } activeAttack &&
                GetJudgmentAttack(pendingJudgment) is { } judgmentAttack &&
                SameAttackOwner(judgmentAttack, activeAttack);
            var belongsToProgramJudgmentDamage = programJudgmentFrame is not null &&
                _resolutionStack.OfType<ProgramSkillFrame>().Any(child =>
                    child.AttackAttempt?.ProgramJudgmentWindowId == programJudgmentFrame.Id) &&
                programJudgmentFrame.ParentFrameId == pendingJudgment.Id;
            var programSkillFrame = _resolutionStack
                .OfType<ProgramSkillFrame>()
                .LastOrDefault(frame => frame.Id == pendingJudgment.ParentFrameId);
            var belongsToProgramSkill =
                IsValidProgramJudgmentContinuation(pendingJudgment, programSkillFrame);
            var belongsToDelayedCard = IsDelayedJudgmentContinuation(pendingJudgment.Continuation) &&
                                        GetJudgmentAttack(pendingJudgment) is null &&
                                        GetDelayedJudgmentCard(pendingJudgment) is { } delayedCard &&
                                        _cardZones.GetLocation(delayedCard.Id) ==
                                        CardLocation.Judgment(pendingJudgment.TargetSeat);
            if ((!belongsToActiveAttack && !belongsToProgramJudgmentDamage &&
                  !belongsToProgramSkill && !belongsToDelayedCard) ||
                _resolutionStack.OfType<JudgmentFrame>().LastOrDefault() is not { } judgmentFrame ||
                judgmentFrame.Id != pendingJudgment.Id ||
                judgmentFrame.ParentFrameId != pendingJudgment.ParentFrameId ||
                judgmentFrame.TargetSeat != pendingJudgment.TargetSeat ||
                judgmentFrame.ReplacementCandidateIndex < 0 ||
                judgmentFrame.ReplacementCandidateIndex > judgmentFrame.ReplacementCandidates.Count ||
                GetJudgmentCard(pendingJudgment) is not { } currentJudgmentCard ||
                (_cardZones.GetLocation(currentJudgmentCard.Id) != CardLocation.Judgment(pendingJudgment.TargetSeat) &&
                 !(programJudgmentFrame is not null &&
                   _cardZones.GetLocation(currentJudgmentCard.Id) == CardLocation.Hand(pendingJudgment.TargetSeat) &&
                   programJudgmentFrame.Candidates.Take(programJudgmentFrame.CandidateIndex + 1)
                       .Any(candidate => _contentRegistry.Skills[candidate.SkillId].Program!.Triggers
                           .Single(trigger => trigger.Id == candidate.TriggerId).Effects
                           .Any(effect => effect.Op == SkillProgramEffectOp.ClaimJudgmentCard)))))
            {
                throw new InvalidOperationException(
                    "A judgment continuation must retain its public card and ordered cursor.");
            }

            var activeJudgmentProgram = _resolutionStack.OfType<ProgramSkillFrame>()
                .LastOrDefault(item =>
                    item.WindowContext?.Judgment?.JudgmentFrameId == pendingJudgment.Id ||
                    item.WindowContext?.JudgmentReplacement?.JudgmentFrameId == pendingJudgment.Id);
            var isProgramJudgmentChoice = programJudgmentFrame is not null &&
                                          ReferenceEquals(coreTop, programJudgmentFrame) &&
                                          !belongsToProgramJudgmentDamage;
            var programReplacementCandidate = activeJudgmentProgram is null && !isProgramJudgmentChoice &&
                                              pendingJudgment.Succeeded is null &&
                                              CurrentJudgmentCandidate(pendingJudgment) is { } candidate
                ? candidate
                : null;
            var programReplacementTrigger = programReplacementCandidate is not null
                ? GetProgramJudgmentReplacement(programReplacementCandidate).Trigger
                : null;
            var isProgramReplacementChoice = programReplacementTrigger is not null;
            var expectedJudgmentOwner = isProgramJudgmentChoice
                ? programJudgmentFrame!.Candidates[programJudgmentFrame.CandidateIndex].OwnerSeat
                : isProgramReplacementChoice
                    ? programReplacementCandidate!.OwnerSeat
                    : -1;
            var promptMatches = activeJudgmentProgram is not null || belongsToProgramJudgmentDamage
                ? true
                : isProgramJudgmentChoice
                ? coreDecision is null ||
                  IsProgramJudgmentPromptValid(programJudgmentFrame!)
                : isProgramReplacementChoice
                    ? coreDecision is { Kind: DecisionKind.ProgramJudgmentReplacement } replacementDecision &&
                      replacementDecision.PlayerSeat == expectedJudgmentOwner &&
                      replacementDecision.ValidCardIds.SequenceEqual(
                          GetProgramJudgmentReplacementCards(
                              _players[expectedJudgmentOwner], programReplacementTrigger!,
                              pendingJudgment.TargetSeat, pendingJudgment.Reason)
                          .Select(card => card.Id)) &&
                      replacementDecision.Choices.Count == replacementDecision.ValidCardIds.Count +
                          (programReplacementTrigger!.Optional ? 1 : 0)
                    : false;
            if (!promptMatches)
            {
                throw new InvalidOperationException(
                    "An active judgment continuation must retain its private replacement or result prompt.");
            }

            if (activeJudgmentProgram is null && !belongsToProgramJudgmentDamage)
            {
                var expectedJudgmentStatus = isProgramJudgmentChoice && coreDecision is null
                    ? EngineStatus.Running
                    : _players[expectedJudgmentOwner].IsHuman
                        ? EngineStatus.AwaitingHumanResponse
                        : EngineStatus.Running;
                if (_status != expectedJudgmentStatus)
                    throw new InvalidOperationException(
                        "A judgment prompt status does not match its current owner.");
            }
        }

        if (ActiveDying is not null &&
            ActiveDamageTrigger is not null &&
            !damageProgramDying)
        {
            throw new InvalidOperationException(
                "A damage trigger window cannot coexist with a dying continuation.");
        }

        if (ActiveDamageTrigger is { } pendingDamageTrigger)
        {
            if (_resolutionStack.All(frame => frame.Id != pendingDamageTrigger.Id) ||
                pendingDamageTrigger.CandidateIndex < 0 ||
                pendingDamageTrigger.CandidateIndex > pendingDamageTrigger.Candidates.Count)
            {
                throw new InvalidOperationException(
                    "A damage trigger continuation must retain a valid trigger window frame.");
            }

            var damageCursorTop = DamageCursorEffectiveTop(includeNestedObservers: _resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.ConvertingGift is { Observe: true } && f.WindowContext?.ParentFrameId == pendingDamageTrigger.Id) || HasAvailableBoundDamageObserver(pendingDamageTrigger.Id) || HasPaidDamageTargetMountObserver(pendingDamageTrigger.Id) || HasPaidDamageTargetObtainObserver(pendingDamageTrigger.Id) || HasDamageAppearanceDrawObserver(pendingDamageTrigger.Id) || HasCappedHandRefreshObserver(pendingDamageTrigger.Id) || HasPaidHandRepaymentObserver(pendingDamageTrigger.Id) || HasBoundRankBonusMovementObserver(pendingDamageTrigger.Id) || HasOwnedDamagePointJudgmentObserver(pendingDamageTrigger.Id) || HasDamageJudgmentSuitPaymentObserver(pendingDamageTrigger.Id) || HasAppliedDamageBenefitObserver(pendingDamageTrigger.Id) || HasFireTargetBenefitObserver(pendingDamageTrigger.Id) || HasNamedAcquisitionDamageObserver(pendingDamageTrigger.Id) || HasProvenanceClaimObserver(pendingDamageTrigger.Id) || HasEquipmentDonationDamageObserver(pendingDamageTrigger.Id) || HasPaidColorDamageClaimDamageObserver(pendingDamageTrigger.Id) || HasHalfHandPaidDamageObserver(pendingDamageTrigger.Id) || (HasDynamicDiscardDamageObserver(pendingDamageTrigger.Id) || HasSignedDamagePaymentObserver(pendingDamageTrigger.Id)) || HasPairBenefitDamageObserver(pendingDamageTrigger.Id) || HasEndingPairSlashDamageObserver(pendingDamageTrigger.Id) || HasActualDiscardRecoveryDamageObserver(pendingDamageTrigger.Id) || HasForeignContestAidDamageObserver(pendingDamageTrigger.Id) || HasPrepDiscardDamageObserver(pendingDamageTrigger.Id) || HasSlashBenefitDamageObserver(pendingDamageTrigger.Id) || HasTargetPenaltyDamageObserver(pendingDamageTrigger.Id) || HasCappedConversionBenefitObserver(pendingDamageTrigger.Id) || HasPlacedEquipmentBenefitDamageObserver(pendingDamageTrigger.Id) || HasRecipientContestDamageObserver(pendingDamageTrigger.Id));
            var activeDamageProgram = damageCursorTop is ProgramSkillFrame programFrame &&
                programFrame.WindowContext is
                {
                    Window: SkillProgramTriggerWindow.DamageAppliedBeforeDying or
                        SkillProgramTriggerWindow.AfterDamageApplied,
                    ParentFrameId: var programParentId
                } &&
                programParentId == pendingDamageTrigger.Id;
            var activeDamageProgramPindian = coreTop is PindianFrame pindianFrame &&
                _resolutionStack.Count >= 2 &&
                _resolutionStack[^2] is ProgramSkillFrame pindianParent &&
                pindianParent.Id == pindianFrame.ParentFrameId &&
                pindianParent.WindowContext is
                {
                    Window: SkillProgramTriggerWindow.AfterDamageApplied,
                    ParentFrameId: var pindianWindowId
                } &&
                pindianWindowId == pendingDamageTrigger.Id;
            var activeDamageProgramJudgment = ActiveJudgment is { } nestedJudgment &&
                _resolutionStack.OfType<ProgramSkillFrame>().Any(parent =>
                    parent.Id == nestedJudgment.ParentFrameId &&
                    parent.WindowContext is
                    {
                        Window: SkillProgramTriggerWindow.AfterDamageApplied,
                        ParentFrameId: var damageWindowId
                    } && damageWindowId == pendingDamageTrigger.Id);
            var awaitingDamageProgramPrompt = coreDecision is { Kind: DecisionKind.ProgramTrigger } &&
                pendingDamageTrigger.CandidateIndex < pendingDamageTrigger.Candidates.Count;
            var expectedTop = (HasDynamicDiscardDamageObserver(pendingDamageTrigger.Id) || HasSignedDamagePaymentObserver(pendingDamageTrigger.Id)) || damageProgramDying
                ? true
                : activeDamageProgram || activeDamageProgramPindian || activeDamageProgramJudgment
                ? true
                : damageCursorTop is DamageTriggerWindowFrame triggerFrame &&
                  triggerFrame.Id == pendingDamageTrigger.Id &&
                  triggerFrame.CandidateIndex == pendingDamageTrigger.CandidateIndex;
            if (!expectedTop)
            {
                throw new InvalidOperationException(
                    "A damage trigger continuation must retain its window or skill frame at the stack top.");
            }

            if (!activeDamageProgram && !activeDamageProgramPindian && !activeDamageProgramJudgment &&
                !awaitingDamageProgramPrompt &&
                !damageProgramDying &&
                _status != EngineStatus.Running)
            {
                throw new InvalidOperationException(
                    "An automatic damage trigger cursor must remain in the running state.");
            }
        }

        if (ActiveDuel is not null &&
            (ActiveCardAttack is null ||
             (!SameAttackOwner(ActiveDuel.Attack, ActiveCardAttack) &&
              !(ActiveCardAttack.IsProgramJudgmentDamage &&
                 SameAttackOwner(ActiveJudgment is { } duelJudgment ? GetJudgmentAttack(duelJudgment) : null, ActiveDuel.Attack)) &&
              !IsCardAttackSuspendedByProgramDamage)))
        {
            throw new InvalidOperationException("A Duel continuation must retain its active card resolution.");
        }

        if (ActiveGroupCard is { Effect: GroupCardEffect.ResponseAttack } group &&
            !(ActiveYingboGift is
            {
                Continuation: YingboGiftContinuation.GroupAttack,
                Attack: null,
                Group: { } yingboGroup
            } &&
              ReferenceEquals(yingboGroup, group) &&
              group.CurrentAttack is null &&
              ActiveCardAttack is null) &&
            (ActiveCardAttack is null ||
             (!SameAttackOwner(group.CurrentAttack, ActiveCardAttack) &&
              !(ActiveCardAttack.IsProgramJudgmentDamage &&
                 SameAttackOwner(ActiveJudgment is { } groupJudgment ? GetJudgmentAttack(groupJudgment) : null, group.CurrentAttack)) &&
              !IsCardAttackSuspendedByProgramDamage)))
        {
            throw new InvalidOperationException(
                "A group continuation must retain its current target attack or pending Yingbo gift choice.");
        }

        if (ActiveFactionDefense is { } hujia)
        {
            var responseWindow = _resolutionStack.OfType<ResponseWindowFrame>().LastOrDefault();
            var awaitingBaguaJudgment = ActiveJudgment is { Continuation: JudgmentContinuationKind.FactionDefenseBagua } judgment &&
                                         SameAttackOwner(GetJudgmentAttack(judgment), hujia.Attack) &&
                                        judgment.TargetSeat == hujia.CurrentCandidateSeat;
            var awaitingProvider = coreDecision is { Kind: DecisionKind.RespondDodge } hujiaDecision &&
                                   hujiaDecision.PlayerSeat == hujia.CurrentCandidateSeat ||
                                   HasProgramDodgeResponseContinuation(hujia.Attack);
            if (ActiveCardAttack is null ||
                !SameAttackOwner(ActiveCardAttack, hujia.Attack) ||
                hujia.OwnerSeat != hujia.Attack.TargetSeat ||
                !hujia.Attack.FactionDefenseAttempted ||
                hujia.CandidateIndex < 0 ||
                hujia.CandidateIndex >= hujia.CandidateSeats.Count ||
                responseWindow is null ||
                responseWindow.ParentFrameId != hujia.Attack.ResolutionId ||
                responseWindow.ResponderSeat != hujia.OwnerSeat ||
                awaitingBaguaJudgment == awaitingProvider)
            {
                throw new InvalidOperationException(
                    "A FactionDefense continuation must retain its attack, ordered Wei cursor and one private response.");
            }
        }

        if (ActiveFactionCardRequest is { } jijiang)
        {
            var candidateCursorValid = jijiang.CandidateIndex >= 0 &&
                                       jijiang.CandidateIndex < jijiang.CandidateSeats.Count;
            var providerPromptMatches = IsFactionRequestCostPrompt(jijiang) || HasPaidFactionRequestCostRecovery(jijiang) ||
                                        coreDecision is { Kind: DecisionKind.RespondSlash } jijiangDecision &&
                                        jijiangDecision.PlayerSeat == jijiang.CurrentCandidateSeat;
            var zhuquePromptMatches = coreDecision is { Kind: DecisionKind.ZhuqueFan } zhuqueDecision &&
                                      zhuqueDecision.PlayerSeat == jijiang.OwnerSeat &&
                                      jijiang.ZhuqueFanProviderSeat == jijiang.CurrentCandidateSeat &&
                                      jijiang.ZhuqueFanPhysicalCards.Count > 0;
            if ((!jijiang.IsProgramSkillUse && jijiang.PolicySource is null) ||
                jijiang.RequiredKind != CardKind.Slash ||
                string.IsNullOrWhiteSpace(jijiang.ProviderFactionId) ||
                jijiang.CandidateSeats.Any(seat => !IsValidPlayerSeat(seat) || seat == jijiang.OwnerSeat) ||
                !candidateCursorValid ||
                jijiang.AwaitingProviders && jijiang.AwaitingZhuqueFanChoice ||
                jijiang.AwaitingZhuqueFanChoice != zhuquePromptMatches ||
                jijiang.AwaitingZhuqueFanChoice &&
                (!UsesFormalZhuqueFan || !HasZhuqueFan(_players[jijiang.OwnerSeat])) ||
                !jijiang.AwaitingZhuqueFanChoice &&
                (jijiang.ZhuqueFanProviderSeat is not null ||
                 jijiang.ZhuqueFanPhysicalCards.Count != 0))
            {
                throw new InvalidOperationException(
                    "A faction-card request must retain its ordered provider cursor.");
            }

            if (jijiang.IsBorrowedSwordUse)
            {
                var borrowedParent = jijiang.BorrowedSword;
                var responseWindow = _resolutionStack.OfType<ResponseWindowFrame>().LastOrDefault(frame =>
                    borrowedParent is not null && frame.ParentFrameId == borrowedParent.ResolutionId);
                var activeAttackMatches = !jijiang.AwaitingProviders &&
                                          !jijiang.AwaitingZhuqueFanChoice &&
                                          jijiang.ActiveAttack is { } activeAttack &&
                                          SameAttackOwner(ActiveCardAttack, activeAttack) &&
                                          SameAttackOwner(borrowedParent?.ActiveAttack, activeAttack);
                var awaitingZhuqueMatches = jijiang.AwaitingZhuqueFanChoice &&
                                            responseWindow is not null &&
                                            zhuquePromptMatches;
                if (borrowedParent is null ||
                    !SameContinuationOwner(ActiveBorrowedSword, borrowedParent) ||
                    (jijiang.AwaitingProviders
                        ? responseWindow is null || !providerPromptMatches
                        : !awaitingZhuqueMatches && !activeAttackMatches))
                {
                    throw new InvalidOperationException(
                        "A Borrowed Sword FactionSlash continuation must retain its parent response or nested Slash.");
                }
            }
            else if (jijiang.IsQinglongCrescentBladeUse)
            {
                var qinglongParent = jijiang.QinglongCrescentBlade;
                var parentAttack = qinglongParent?.Attack;
                var activeAttackMatches = !jijiang.AwaitingProviders &&
                                          !jijiang.AwaitingZhuqueFanChoice &&
                                          jijiang.ActiveAttack is { } activeAttack &&
                                          SameAttackOwner(ActiveCardAttack, activeAttack) &&
                                          ActiveQinglongCrescentBlade is null;
                var awaitingProviderMatches = jijiang.AwaitingProviders &&
                                              SameContinuationOwner(ActiveQinglongCrescentBlade, qinglongParent) &&
                                              SameAttackOwner(ActiveCardAttack, parentAttack) &&
                                              providerPromptMatches;
                if (qinglongParent is null ||
                    jijiang.TargetSeat != parentAttack?.TargetSeat ||
                    (!awaitingProviderMatches && !activeAttackMatches))
                {
                    throw new InvalidOperationException(
                        "A Qinglong Crescent Blade FactionSlash continuation must retain its parent choice or nested Slash.");
                }
            }
            else if (jijiang.IsProgramSkillUse)
            {
                var programFrame = jijiang.ProgramSkillFrameId is { } programFrameId
                    ? _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(frame => frame.Id == programFrameId)
                    : null;
                var activeAttackMatches = !jijiang.AwaitingProviders &&
                                          !jijiang.AwaitingZhuqueFanChoice &&
                                          jijiang.ActiveAttack is { } activeAttack &&
                                          SameAttackOwner(ActiveCardAttack, activeAttack);
                if (programFrame is null || (!jijiang.IsAssistedProgramUse && programFrame.OwnerSeat != jijiang.OwnerSeat) ||
                    jijiang.TargetSeat is not { } targetSeat || !IsValidPlayerSeat(targetSeat) ||
                    (jijiang.AwaitingProviders
                        ? !providerPromptMatches
                        : jijiang.AwaitingZhuqueFanChoice
                            ? !zhuquePromptMatches
                            : !activeAttackMatches))
                {
                    throw new InvalidOperationException(
                        "A program faction-card continuation must retain either a provider prompt or its Slash attack.");
                }
                if (jijiang.IsAssistedProgramUse)
                    ValidateAssistedFactionSlashParent(jijiang, programFrame);
            }
            else
            {
                var responseWindow = _resolutionStack.OfType<ResponseWindowFrame>().LastOrDefault();
                if (!jijiang.AwaitingProviders ||
                    jijiang.ResponseAttack is not { } responseAttack ||
                    !SameAttackOwner(ActiveCardAttack, responseAttack) ||
                    responseWindow is null ||
                    responseWindow.ParentFrameId != responseAttack.ResolutionId ||
                    responseWindow.ResponderSeat != jijiang.OwnerSeat ||
                    !providerPromptMatches)
                {
                    throw new InvalidOperationException(
                        "A response FactionSlash continuation must retain its attack, response window and one private provider prompt.");
                }
            }
        }

        if (ActiveGroupCard is { Effect: GroupCardEffect.Recovery } recovery &&
            (ActiveCardAttack is not null || recovery.CurrentAttack is not null))
        {
            throw new InvalidOperationException(
                "A group recovery cannot retain an attack continuation.");
        }

        if (ActiveGroupCard is { Effect: GroupCardEffect.PublicDraft } draft)
        {
            if (ActiveCardAttack is not null || draft.CurrentAttack is not null)
            {
                throw new InvalidOperationException(
                    "A FiveGrains draft cannot retain an attack continuation.");
            }

            if (draft.TargetIndex >= draft.TargetSeats.Count)
            {
                throw new InvalidOperationException(
                    "A FiveGrains draft must finish before its target cursor reaches the end.");
            }

            var expectedPicker = draft.TargetSeats[draft.TargetIndex];
            if (coreDecision is not { Kind: DecisionKind.SelectHarvestCard } harvestDecision ||
                harvestDecision.PlayerSeat != expectedPicker ||
                !harvestDecision.ValidCardIds.SequenceEqual(draft.RevealedCardIds))
            {
                throw new InvalidOperationException(
                    "A FiveGrains draft must retain a prompt for its current picker.");
            }

            var expectedStatus = _players[expectedPicker].IsHuman
                ? EngineStatus.AwaitingHumanCardSelection
                : EngineStatus.Running;
            if (_status != expectedStatus)
            {
                throw new InvalidOperationException(
                    "A FiveGrains draft status does not match its current picker.");
            }
        }

        if (coreDecision?.Kind == DecisionKind.SelectHarvestCard &&
            ActiveGroupCard is not { Effect: GroupCardEffect.PublicDraft })
        {
            throw new InvalidOperationException(
                "A harvest selection cannot exist without a public draft.");
        }

        if (coreDecision?.Kind is DecisionKind.FireAttackReveal or DecisionKind.FireAttackDiscard &&
            ActiveFireAttack is null)
        {
            throw new InvalidOperationException(
                "A FireAttack selection cannot exist without a FireAttack resolution.");
        }

        if (coreDecision?.Kind == DecisionKind.ProgramJudgmentReplacement &&
            ActiveJudgment is null)
        {
            throw new InvalidOperationException(
                "A configured replacement prompt cannot exist without a judgment continuation.");
        }

        if (coreDecision?.Kind == DecisionKind.StoneAxe &&
            ActiveStoneAxe is null)
        {
            throw new InvalidOperationException(
                "A Stone Axe prompt cannot exist without its Slash continuation.");
        }

        if (coreDecision?.Kind == DecisionKind.CixiongDoubleSwords &&
            ActiveCixiongDoubleSwords is null)
        {
            throw new InvalidOperationException(
                "A Cixiong Double Swords prompt cannot exist without its Slash continuation.");
        }

        if (coreDecision?.Kind == DecisionKind.QinglongCrescentBlade &&
            ActiveQinglongCrescentBlade is null)
        {
            throw new InvalidOperationException(
                "A Qinglong Crescent Blade prompt cannot exist without its Slash continuation.");
        }

        if (coreDecision?.Kind == DecisionKind.IceSword &&
            ActiveIceSword is null)
        {
            throw new InvalidOperationException(
                "An Ice Sword prompt cannot exist without its Slash continuation.");
        }

        if (coreDecision?.Kind == DecisionKind.QilinBow &&
            ActiveQilinBow is null)
        {
            throw new InvalidOperationException(
                "A Qilin Bow prompt cannot exist without its Slash continuation.");
        }

        if (coreDecision?.Kind == DecisionKind.Yingbo && ActiveYingboGift is null)
        {
            throw new InvalidOperationException(
                "A Yingbo prompt cannot exist without its card continuation.");
        }

        if (coreDecision?.Kind == DecisionKind.ZhuqueFan &&
            ActiveFactionCardRequest is not { AwaitingZhuqueFanChoice: true })
        {
            throw new InvalidOperationException(
                "A Zhuque Fan prompt cannot exist without its FactionSlash continuation.");
        }

        var hasCardProgramPrompt = coreDecision is { Kind: DecisionKind.ProgramTrigger } &&
            (_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Any() || _resolutionStack.OfType<CardEffectBeforeApplyFrame>().Any()) || HasCardActionPindianContinuation();
        var hasBeforeDamageProgramPrompt = coreDecision is { Kind: DecisionKind.ProgramTrigger } &&
            _resolutionStack.OfType<BeforeDamageProgramWindowFrame>().Any();
        var awaitingHumanResponse =
            (coreDecision?.Kind is DecisionKind.RespondDodge or
                DecisionKind.RespondSlash or
                DecisionKind.StoneAxe or
                DecisionKind.CixiongDoubleSwords or
                DecisionKind.QinglongCrescentBlade or
                DecisionKind.IceSword or
                DecisionKind.QilinBow or
                DecisionKind.ZhuqueFan or
                DecisionKind.ProgramJudgmentTrigger or
                DecisionKind.ProgramJudgmentReplacement || hasCardProgramPrompt || hasBeforeDamageProgramPrompt) &&
             _status == EngineStatus.AwaitingHumanResponse;
        var awaitingHumanNullification =
            coreDecision?.Kind == DecisionKind.Nullification &&
            _status == EngineStatus.AwaitingHumanResponse;
        var awaitingHumanDying =
            coreDecision?.Kind == DecisionKind.RescueDying &&
            _status == EngineStatus.AwaitingHumanDying;
        var awaitingAiResponse = IsAiResponsePending() ||
                                  (_resolutionStack.LastOrDefault() is RequestedDeckBasicFrame &&
                                   coreDecision is { Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.QinglongCrescentBlade } originalNeed &&
                                   originalNeed.PlayerSeat != _options.HumanSeat) ||
                                  (coreDecision is { } programDecision &&
                                   (hasCardProgramPrompt || hasBeforeDamageProgramPrompt) &&
                                   !_players[programDecision.PlayerSeat].IsHuman) ||
                                 IsAiProgramJudgmentReplacementPending() ||
                                 IsAiProgramJudgmentPending();
        var awaitingAiNullification = IsAiNullificationPending();
        var awaitingNullificationProgram = _resolutionStack
            .OfType<ProgramCardTriggerWindowFrame>()
            .Any(frame => IsNullificationResponseProgramWindow(frame) &&
                ActiveNullificationWindow?.Id == frame.ParentFrameId);
        var awaitingNullificationPayment = ActiveNullificationWindow is { } paymentWindow &&
            TryGetPolicyCounterspellPaymentRide(paymentWindow, out _);
        if (!_resolutionStack.OfType<CardEffectBeforeApplyFrame>().Any() && CurrentDamageAttempt is null &&
            ActiveNullificationWindow is null &&
            ActiveJudgment is null &&
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>()
                .All(frame => frame.Continuation is not
                    (ProgramCardContinuation.DelayedCard or ProgramCardContinuation.BeforeTrickTargetEffects or ProgramCardContinuation.FinalizedTrick or ProgramCardContinuation.FinalizedSimpleCard or
                     ProgramCardContinuation.CommittedTrick or ProgramCardContinuation.CommittedSimpleCard or
                     ProgramCardContinuation.CompletedCard)) &&
            ActiveFactionCardRequest?.IsProgramSkillUse != true &&
            ActiveFactionCardRequest?.IsBorrowedSwordUse != true &&
            ActiveBorrowedSword is null &&
            ActiveDying?.ResumesProgramSkill != true &&
            (awaitingHumanResponse || awaitingHumanDying || awaitingAiResponse) &&
            !awaitingNullificationProgram)
        {
            throw new InvalidOperationException("A response continuation exists without an active Slash.");
        }

        if (ActiveNullificationWindow is not null &&
            (awaitingHumanResponse || awaitingHumanDying || awaitingAiResponse) &&
            !awaitingNullificationProgram && !awaitingNullificationPayment)
        {
            throw new InvalidOperationException(
                "A Nullification window cannot retain a non-Nullification prompt.");
        }

        if (ActiveNullificationWindow is not null &&
            !awaitingHumanNullification &&
            !awaitingAiNullification &&
            !awaitingNullificationProgram && !awaitingNullificationPayment)
        {
            throw new InvalidOperationException(
                "An active Nullification window must retain exactly one response continuation.");
        }

        if (ActiveDying is null &&
            ActiveDamageTrigger is null &&
            ActiveJudgment is null &&
            ActiveYingboGift is null &&
            ActiveCardAttack is not null &&
            awaitingHumanResponse == awaitingAiResponse &&
            !HasProgramDodgeResponseContinuation(ActiveCardAttack) &&
            !HasRecoveryPaidCardUseObserver(ActiveCardAttack.ResolutionId) &&
            !HasPaidFactionRequestCostRecovery(ActiveFactionCardRequest))
        {
            throw new InvalidOperationException("An active card resolution must have exactly one response continuation.");
        }

        if (_status == EngineStatus.Completed &&
            (coreDecision is not null ||
             ActiveCardAttack is not null ||
             ActiveDuel is not null ||
             ActiveGroupCard is not null ||
             ActiveFireAttack is not null ||
             ActiveNullificationWindow is not null ||
             ActiveTargetCardSelection is not null ||
             ActiveDying is not null ||
             ActiveDamageTrigger is not null ||
              ActiveJudgment is not null ||
             ActiveYingboGift is not null ||
             ActiveFactionDefense is not null ||
             ActiveFactionCardRequest is not null ||
             ActiveBorrowedSword is not null ||
             ActiveStoneAxe is not null ||
             ActiveCixiongDoubleSwords is not null ||
             ActiveQinglongCrescentBlade is not null ||
             ActiveIceSword is not null ||
             ActiveQilinBow is not null ||
             ActiveFangtianHalberd is not null ||
             processing.Count != 0))
        {
            throw new InvalidOperationException("A completed game cannot retain pending resolution state.");
        }
    }

    private bool IsActiveAttackCardConsistent(
        CardAttackHandle attack,
        IReadOnlyList<Card> processing)
    {
        // A rescue card can be awaiting its own use triggers while the attack
        // remains suspended below the dying frame. Its costs belong to that
        // child use and are checked by the card-window invariant separately.
        var rescueCosts = _resolutionStack.OfType<CardUseFrame>()
            .Where(frame => frame.Id != attack.ResolutionId && frame.DyingResponse is { } response &&
                response.ResolutionId == ActiveDying?.FrameId)
            .SelectMany(frame => frame.Action?.PhysicalCards ?? [])
            .Select(cost => cost.CardId).ToHashSet();
        if (attack.PhysicalCards.Any(card => rescueCosts.Contains(card.Id))) return false;
        processing = processing.Where(card => !rescueCosts.Contains(card.Id)).ToArray();
        if (_resolutionStack.LastOrDefault() is PindianFrame pindian)
        {
            var contestIds = CurrentPindianProcessingIds(pindian);
            if (contestIds.Any(id => (pindian.ParentProcessingCardIds ?? []).Contains(id))) return false;
            processing = processing.Where(card => !contestIds.Contains(card.Id)).ToArray();
        }
        if (HasPendingProgramBoundCards)
        {
            return IsProgramProcessingConsistent(attack, processing);
        }

        if (_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault() is { } programFrame &&
            programFrame.Action.Type == CardActionType.Response)
        {
            var alternativeResponseIds = programFrame.Action.PhysicalCards
                .Where(cost => IsProgramAlternativeCost(programFrame.Action, cost.CardId))
                .Select(cost => cost.CardId).ToHashSet();
            if (processing.Any(card => alternativeResponseIds.Contains(card.Id))) return false;
            var responseIds = programFrame.Action.PhysicalCards
                .Where(cost => !alternativeResponseIds.Contains(cost.CardId) && !IsExchangedCardClaim(programFrame.Action.ActionId,cost.CardId))
                .Select(cost => cost.CardId).ToHashSet();
            if (programFrame.CompletedResponseReturn is { IsCommitted: false } || programFrame.CompletedResponseReturn is { IsCommitted: true, Kind: ProgramCompletedResponseKind.Nullification })
            {
                if (processing.Any(card => responseIds.Contains(card.Id))) return false;
            }
            else if (responseIds.Any(id => processing.All(card => card.Id != id))) return false;
            processing = processing.Where(card => !responseIds.Contains(card.Id)).ToArray();
        }
        if (IsExactHpLossMaterialSlash(attack, processing)) return true;
        if (IsTieredRoundZeroAttackConsistent(attack, processing) || IsTieredRoundZeroBorrowedAttackConsistent(attack, processing)) return true;
        if (TryValidateProgramAlternativeCostAttack(attack, processing, out var alternativeCostValid))
            return alternativeCostValid;
        if (attack.IsProgramJudgmentDamage)
        {
            var retainedParentCardIds = (ActiveJudgment is { } parentJudgment ? GetJudgmentAttack(parentJudgment) : null)?.PhysicalCards
                .Where(card => _cardZones.GetLocation(card.Id) == CardLocation.Processing)
                .Select(card => card.Id)
                .Order()
                .ToArray() ?? [];
            return attack.Card is null &&
                   processing.Select(card => card.Id).Order().SequenceEqual(retainedParentCardIds) &&
                   attack.ProgramJudgmentFrameId is { } frameId &&
                   _resolutionStack.OfType<ProgramJudgmentTriggerWindowFrame>()
                       .Any(frame => frame.Id == frameId);
        }

        if (attack.Card is null && (attack.EffectiveCardKind == CardKind.Slash ||
            LifecycleCardUse(attack.ResolutionId) is { } fireUse && IsCurrentSlashFireChangedUse(fireUse) ||
            IsSlashCard(attack.EffectiveCardKind ?? CardKind.Slash) && LifecycleCardUse(attack.ResolutionId)?.VirtualBasicReturn is not null) &&
            (attack.ProgramSkillCardUseFrameId is not null || LifecycleCardUse(attack.ResolutionId)?.VirtualBasicReturn is not null || IsForeignPublicPileSlashUse(attack.ResolutionId)))
        {
            return processing.Count == 0 && _resolutionStack.OfType<CardUseFrame>().Any(frame =>
                frame.Id == attack.ResolutionId && frame.CardId == 0 && frame.PhysicalCardIds?.Count is 0);
        }

        if (attack.Card is null && attack.EffectiveCardKind == CardKind.Duel && IsDamageTargetDuelUse(attack.ResolutionId))
            return processing.Select(c => c.Id).Order().SequenceEqual(DamageTargetDuelOuterProcessing(attack.ResolutionId).Order()) && LifecycleCardUse(attack.ResolutionId)?.DamageTargetDuelOrigin is { AttackStarted: true } obtained &&
                attack.ProgramSkillCardUseFrameId == obtained.ParentProgramFrameId;

        if (attack.Card is null && attack.EffectiveCardKind == CardKind.Duel && IsConditionalDiscardDuelUse(attack.ResolutionId))
            return processing.Count == 0 && LifecycleCardUse(attack.ResolutionId)?.ConditionalDiscardDuelOrigin is { AttackStarted: true } conditional &&
                attack.ProgramSkillCardUseFrameId == conditional.ParentProgramFrameId;

        if (attack.Card is null && attack.EffectiveCardKind == CardKind.Duel && IsDualColorDuelUse(attack.ResolutionId))
            return processing.Count == 0 && LifecycleCardUse(attack.ResolutionId)?.DualColorDuelOrigin is { AttackStarted: true } dualColor &&
                attack.ProgramSkillCardUseFrameId == dualColor.ParentProgramFrameId;

        if (attack.Card is null && attack.EffectiveCardKind == CardKind.Duel && IsSelectedActorDuelUse(attack.ResolutionId))
            return processing.Count == 0 && LifecycleCardUse(attack.ResolutionId)?.SelectedActorDuelOrigin is { AttackStarted: true } origin &&
                attack.ProgramSkillCardUseFrameId == origin.ParentProgramFrameId;

        if (attack.Card is null && attack.EffectiveCardKind == CardKind.Duel &&
            attack.ProgramSkillFrameId == attack.ResolutionId && ActiveDuel is { } virtualDuel &&
            virtualDuel.Attack.ResolutionId == attack.ResolutionId)
        {
            return processing.Count == 0 && _resolutionStack.OfType<ProgramSkillFrame>().Any(frame =>
                frame.Id == attack.ResolutionId && frame.SelectedTargetSeats.Count == 2 &&
                frame.SelectedTargetSeats.Distinct().Count() == 2 &&
                frame.SelectedTargetSeats.Contains(attack.SourceSeat) && frame.SelectedTargetSeats.Contains(attack.TargetSeat));
        }

        var attackCard = attack.Card;
        if (attackCard is null)
        {
            return false;
        }

        if (attack.IsDelayedJudgmentDamage)
        {
            var delayedLocation = _cardZones.GetLocation(attackCard.Id);
            return delayedLocation.Zone is CardZoneKind.Judgment or
                CardZoneKind.Hand or
                CardZoneKind.DiscardPile;
        }

        if (ActiveBorrowedSword is { ActiveAttack: { } borrowedAttack } borrowedSword &&
            SameAttackOwner(borrowedAttack, attack))
        {
            if (processing.All(card => card.Id != borrowedSword.Card.Id))
            {
                return false;
            }

            var allowedIds = attack.PhysicalCards.Select(card => card.Id)
                .Append(borrowedSword.Card.Id)
                .ToHashSet();
            var retainedClaimCosts = attack.PhysicalCards.Where(card => !IsCurrentUsePhysicalCardClaim(attack.ResolutionId, card.Id)).Select(card => card.Id).ToHashSet();
            if (retainedClaimCosts.Count != attack.PhysicalCards.Count &&
                processing.Count == retainedClaimCosts.Count + 1 &&
                processing.All(card => card.Id == borrowedSword.Card.Id || retainedClaimCosts.Contains(card.Id))) return true;
            var allAttackCardsInProcessing = attack.PhysicalCards.All(card =>
                _cardZones.GetLocation(card.Id) == CardLocation.Processing);
            if (allAttackCardsInProcessing)
            {
                return processing.Count == attack.PhysicalCards.Count + 1 &&
                       processing.All(card => allowedIds.Contains(card.Id));
            }

            if (_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault() is
                { Continuation: ProgramCardContinuation.CompletedSlash } completed &&
                completed.Candidates.Any(candidate => _contentRegistry.GetSkill(candidate.SkillId).Program?.Triggers
                    .Where(trigger => trigger.Id == candidate.TriggerId).SelectMany(trigger => trigger.Effects)
                    .Any(effect => effect.Op == SkillProgramEffectOp.StoreTopCardInPublicPile) == true) &&
                completed.ParentFrameId == attack.ResolutionId &&
                _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(frame => frame.Id == attack.ResolutionId) is
                    { Step: ResolutionFrameStep.Completed, Action: { } finishedAction } &&
                completed.Action.ActionId == finishedAction.ActionId && completed.Action.ActorSeat == attack.CardUserSeat &&
                completed.Action.PhysicalCards.Select(cost => cost.CardId).SequenceEqual(attack.PhysicalCards.Select(card => card.Id)) &&
                _resolutionStack.OfType<CardUseFrame>().Any(frame => frame.Id == borrowedSword.ResolutionId &&
                    frame.CardKind == CardKind.BorrowedSword && frame.CardId == borrowedSword.Card.Id))
                return processing.Count == 1 && processing[0].Id == borrowedSword.Card.Id &&
                    attack.PhysicalCards.All(card => _cardZones.GetLocation(card.Id).Zone is
                        CardZoneKind.DiscardPile or CardZoneKind.Hand or CardZoneKind.DrawPile);

            return HasDamageTriggerForAttack(attack) &&
                   processing.All(card => allowedIds.Contains(card.Id)) &&
                   attack.PhysicalCards.All(card =>
                       _cardZones.GetLocation(card.Id).Zone is CardZoneKind.Processing or
                           CardZoneKind.DrawPile or CardZoneKind.Hand or CardZoneKind.DiscardPile);
        }

        if (ActiveFangtianHalberd is { CurrentAttack: { } fangtianAttack } fangtian &&
            SameAttackOwner(fangtianAttack, attack))
        {
            if (IsExactNextActualUseMaterialSlash(fangtian, attack, processing)) return true;
            var fangtianCardLocation = _cardZones.GetLocation(fangtian.Card.Id);
            return attack.ResolutionId == fangtian.ResolutionId &&
                   attack.SourceSeat == fangtian.SourceSeat &&
                   attack.EffectiveCardKind == fangtian.EffectiveCardKind &&
                   attack.PhysicalCards.Count == 1 &&
                   attack.PhysicalCards[0].Id == fangtian.Card.Id &&
                   fangtianCardLocation.Zone is CardZoneKind.Processing or CardZoneKind.Hand or
                       CardZoneKind.DrawPile or CardZoneKind.DiscardPile &&
                   processing.All(card => card.Id == fangtian.Card.Id);
        }

        var attackCardIds = attack.PhysicalCards.Select(card => card.Id).ToHashSet();
        if (_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault() is
            { Continuation: ProgramCardContinuation.CompletedSlash, Action.ActionId: var completedActionId } &&
            _resolutionStack.OfType<CardUseFrame>().LastOrDefault(frame => frame.Id == attack.ResolutionId)?.Action?.ActionId == completedActionId)
        {
            return processing.Count == 0 && attack.PhysicalCards.All(card =>
                _cardZones.GetLocation(card.Id).Zone is CardZoneKind.DiscardPile or
                    CardZoneKind.Hand or CardZoneKind.DrawPile);
        }
        // A completed gift can reuse the original use's entity in a distinct
        // recipient use. Its exact typed producer owns that current payment.
        if (_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Any(window =>
                TryGetCompletedGiftRecipientSlashRide(window, out var recipientUse) && recipientUse.Id == attack.ResolutionId) &&
            processing.Count == attack.PhysicalCards.Count && processing.All(card => attackCardIds.Contains(card.Id)) &&
            attack.PhysicalCards.All(card => _cardZones.GetLocation(card.Id) == CardLocation.Processing)) return true;

        // An outer multi-target use keeps its own card in Processing for the
        // whole resolution while an inner triggered resolution (such as a
        // nested attack) runs above its response window; its live frame on the
        // stack proves the card is held, not leaked. Once that frame pops, any
        // residue fails this invariant again.
        var heldByOuterUses = _resolutionStack.OfType<CardUseFrame>()
            .Where(frame => frame.Id != attack.ResolutionId && frame.CardId != 0)
            .Select(frame => frame.CardId).ToHashSet();
        var toleratedProcessing = processing
            .Where(card => !heldByOuterUses.Contains(card.Id)).ToArray();
        var retainedAttackIds = attack.PhysicalCards.Where(card=>!IsClaimedUseCardEntity(attack.ResolutionId,card.Id)).Select(card=>card.Id).ToHashSet();
        if (retainedAttackIds.Count != attack.PhysicalCards.Count && toleratedProcessing.Select(card=>card.Id).ToHashSet().SetEquals(retainedAttackIds)) return true;
        if (toleratedProcessing.Length == attack.PhysicalCards.Count &&
            toleratedProcessing.All(card => attackCardIds.Contains(card.Id)))
        {
            return true;
        }

        if (ActiveQinglongFollowup is { } qinglongSuspension &&
            qinglongSuspension.OuterResolutionId != attack.ResolutionId)
        {
            // A Qinglong Crescent Blade follow-up runs nested while its parent
            // attack is suspended with targets still awaiting their dodge
            // windows, so the parent's physical cards legitimately remain in
            // Processing until the parent resumes.
            var suspendedParentIds = qinglongSuspension.NextAttack?.PhysicalCards
                .Select(card => card.Id).ToHashSet() ?? new HashSet<int>();
            return processing.All(card =>
                       attackCardIds.Contains(card.Id) || suspendedParentIds.Contains(card.Id)) &&
                   attack.PhysicalCards.All(card =>
                       _cardZones.GetLocation(card.Id).Zone is CardZoneKind.Processing or
                           CardZoneKind.DrawPile or CardZoneKind.Hand or CardZoneKind.DiscardPile);
        }

        if (HasDamageTriggerForAttack(attack))
        {
            return processing.All(card => attackCardIds.Contains(card.Id)) &&
                   attack.PhysicalCards.All(card =>
                       _cardZones.GetLocation(card.Id).Zone is CardZoneKind.Processing or
                           CardZoneKind.DrawPile or CardZoneKind.Hand or CardZoneKind.DiscardPile);
        }

        if (attack.IsChainPropagation)
        {
            var propagatedLocation = _cardZones.GetLocation(attackCard.Id);
            return propagatedLocation.Zone is CardZoneKind.DrawPile or
                CardZoneKind.Hand or
                CardZoneKind.DiscardPile;
        }

        if (ActiveDying is not null)
        {
            var dyingLocation = _cardZones.GetLocation(attackCard.Id);
            return dyingLocation.Zone is CardZoneKind.DrawPile or
                CardZoneKind.Hand or
                CardZoneKind.DiscardPile;
        }

        if (ActiveGroupCard is not { Effect: GroupCardEffect.ResponseAttack } group ||
            processing.Count != 0)
        {
            return false;
        }

        var location = _cardZones.GetLocation(attackCard.Id);
        // The effect continues after a target claims its physical card. That target can
        // subsequently die, discard it, or move it again before the next target responds.
        return group.DamageClaimedPhysicalCardIds.Contains(attackCard.Id) && location.Zone is
            CardZoneKind.Hand or CardZoneKind.DiscardPile or CardZoneKind.DrawPile;
    }

    internal static bool IsResolvedGroupPhysicalCardDestinationAllowed(
        CardLocation location,
        bool damageCardClaimed,
        IReadOnlyList<int> targetSeats) =>
        location == CardLocation.DiscardPile ||
        damageCardClaimed && location.Zone is CardZoneKind.DrawPile or CardZoneKind.Hand ||
        location is { Zone: CardZoneKind.Hand, OwnerSeat: { } ownerSeat } &&
        targetSeats.Contains(ownerSeat);

    internal static bool RecordClaimedGroupPhysicalCard(
        ISet<int> claimedCardIds,
        long groupResolutionId,
        IReadOnlyList<Card> groupPhysicalCards,
        long attackResolutionId,
        int claimedCardId)
    {
        ArgumentNullException.ThrowIfNull(claimedCardIds);
        ArgumentNullException.ThrowIfNull(groupPhysicalCards);
        if (groupResolutionId != attackResolutionId ||
            !groupPhysicalCards.Any(card => card.Id == claimedCardId))
            return false;
        return claimedCardIds.Add(claimedCardId);
    }

    private bool IsHumanDecisionPending() =>
        _pendingDecision is { } decision && decision.PlayerSeat == _options.HumanSeat;

    private bool IsAiResponsePending() =>
        ((_pendingDecision?.Kind is DecisionKind.RespondDodge or
            DecisionKind.RespondSlash or
            DecisionKind.StoneAxe or
            DecisionKind.CixiongDoubleSwords or
            DecisionKind.QinglongCrescentBlade or
            DecisionKind.IceSword or
            DecisionKind.QilinBow or
            DecisionKind.ZhuqueFan)) &&
        _pendingDecision.PlayerSeat != _options.HumanSeat;

    private bool IsAiNullificationPending() =>
        ActiveNullificationWindow is { } pending &&
        _pendingDecision is { Kind: DecisionKind.Nullification } decision &&
        pending.CandidateIndex < pending.CandidateSeats.Count &&
        decision.PlayerSeat == pending.CandidateSeats[pending.CandidateIndex] &&
        !_players[decision.PlayerSeat].IsHuman;

    private bool IsAiHarvestPending() =>
        ActiveGroupCard is { Effect: GroupCardEffect.PublicDraft } &&
        _pendingDecision?.Kind == DecisionKind.SelectHarvestCard &&
        _pendingDecision.PlayerSeat != _options.HumanSeat;

    private bool IsAiTargetCardSelectionPending() =>
        ActiveTargetCardSelection is { } pending &&
        _pendingDecision is
        {
            Kind: DecisionKind.SelectTargetCard,
            PlayerSeat: var playerSeat
        } decision &&
        decision.PlayerSeat == pending.SourceSeat &&
        !_players[playerSeat].IsHuman;

    private bool IsAiFireAttackPending() =>
        ActiveFireAttack is not null &&
        _pendingDecision is
        {
            Kind: DecisionKind.FireAttackReveal or DecisionKind.FireAttackDiscard,
            PlayerSeat: var playerSeat
        } &&
        playerSeat != _options.HumanSeat;





    private bool IsAiSkipDiscardPolicyPending() =>
        _pendingDecision is { Kind: DecisionKind.SkipDiscardPolicy, PlayerSeat: var playerSeat } &&
        playerSeat == _currentSeat &&
        !_players[playerSeat].IsHuman;

    private void ResolvePendingAiSkipDiscardPolicy()
    {
        if (!IsAiSkipDiscardPolicyPending())
        {
            throw new InvalidOperationException("There is no AI discard-policy choice to resolve.");
        }

        ResolveSkipDiscardPolicyChoice(useSkill: true);
        AdvanceRulesAndPublishState();
    }

    private bool IsAiDyingResponsePending() =>
        ActiveDying is { } dying &&
        _pendingDecision is null &&
        dying.ResponderIndex < dying.ResponderSeats.Count &&
        !_players[dying.ResponderSeat].IsHuman;

    private void RequireHumanDecision(DecisionKind expected)
    {
        EnsureStarted();
        if (_pendingDecision?.Kind != expected ||
            _pendingDecision.PlayerSeat != _options.HumanSeat)
        {
            throw new InvalidOperationException($"The engine is not waiting for human decision {expected}.");
        }
    }

    private void ClearPendingDecision()
    {
        _pendingDecision = null;
        _status = _winner == Winner.None ? EngineStatus.Running : EngineStatus.Completed;
    }

    private void EnsureStarted()
    {
        if (!_started)
        {
            throw new InvalidOperationException("Call Start before advancing the game.");
        }
    }

    private EngineRunResult BuildResult()
    {
        var state = State;
        return new EngineRunResult(_status, _winner, state, state.PendingDecision, _revision)
        {
            WinnerTeamId = IsTeamMode ? _winnerTeamId : null,
            WinnerFactionId = IsNationalWarMode ? _winnerFactionId : null
        };
    }

    private void RefreshPendingDecisionRevision()
    {
        if (_pendingDecision is { } pending)
        {
            _pendingDecision = pending with { Revision = _revision };
        }
    }

    private void PublishState()
    {
        _pendingStatePublication = true;
    }

    private void AddLog(string type, string message, int? actor = null, int? target = null)
    {
        var entry = new GameLogEntry(++_logSequence, _turnNumber, type, message, actor, target);
        _log.Add(entry);
        _pendingNotifications.Enqueue(new LogNotification(entry));
    }

    private void AddThought(AiThoughtRecord thought)
    {
        _aiThoughts.Add(thought);
        _pendingNotifications.Enqueue(new AiThoughtNotification(thought));
    }

    private void AddGeneralThought(AiGeneralThought thought)
    {
        _aiGeneralThoughts.Add(thought);
        _pendingNotifications.Enqueue(new AiGeneralThoughtNotification(thought));
    }

    private void QueueGameEvent(IGameEvent payload)
    {
        if (_started)
        {
            _pendingEvents.Add(payload);
        }
    }

    private void FlushNotifications(GameSnapshot preparedSnapshot)
    {
        while (_pendingNotifications.TryDequeue(out var notification))
        {
            switch (notification)
            {
                case LogNotification log:
                    InvokeObservers(LogAdded, log.Entry, nameof(LogAdded));
                    break;
                case AiThoughtNotification thought:
                    InvokeObservers(AiThoughtAdded, thought.Thought, nameof(AiThoughtAdded));
                    break;
                case AiGeneralThoughtNotification generalThought:
                    InvokeObservers(
                        AiGeneralThoughtAdded,
                        generalThought.Thought,
                        nameof(AiGeneralThoughtAdded));
                    break;
                case CardMovedNotification movement:
                    InvokeObservers(CardMoved, movement.Movement, nameof(CardMoved));
                    break;
                case EventNotification typedEvent:
                    InvokeObservers(EventCommitted, typedEvent.Event, nameof(EventCommitted));
                    break;
                default:
                    throw new InvalidOperationException($"Unknown engine notification {notification.GetType().Name}.");
            }
        }

        if (_pendingStatePublication)
        {
            _pendingStatePublication = false;
            InvokeObservers(StateChanged, preparedSnapshot, nameof(StateChanged));
        }
    }

    private void InvokeObservers<T>(Action<T>? observers, T payload, string notificationType)
    {
        if (observers is null)
        {
            return;
        }

        foreach (Action<T> observer in observers.GetInvocationList())
        {
            try
            {
                observer(payload);
            }
            catch (Exception exception)
            {
                RecordObserverFailure(notificationType, exception);
            }
        }
    }

    private void RecordObserverFailure(string notificationType, Exception exception)
    {
        const int failureLimit = 128;
        if (_observerFailures.Count == failureLimit)
        {
            _observerFailures.RemoveAt(0);
        }

        _observerFailures.Add(new ObserverFailure(
            ++_observerFailureSequence,
            notificationType,
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message));
    }

    private static CardSnapshot ToSnapshot(Card card) =>
        new(card.Id, card.Kind, card.Suit, card.Rank, card.DisplayName, card.RankText);

    private CardSnapshot ToJudgmentSnapshot(Card card)
    {
        var effectiveKind = GetJudgmentEffectiveCardKind(card);
        return new CardSnapshot(
            card.Id,
            effectiveKind,
            card.Suit,
            card.Rank,
            CardCatalog.Get(effectiveKind).DisplayName,
            card.RankText);
    }

    private static PendingDecision CloneDecision(PendingDecision decision) =>
        decision with
        {
            ValidCardIds = Array.AsReadOnly(decision.ValidCardIds.ToArray()),
            ValidTargetSeats = Array.AsReadOnly(decision.ValidTargetSeats.ToArray()),
            Choices = Array.AsReadOnly(decision.Choices.Select(CloneChoice).ToArray()),
            ValidContentIds = Array.AsReadOnly(decision.ValidContentIds.ToArray()),
        };

    private static PromptChoice CloneChoice(PromptChoice choice) =>
        choice with
        {
            Cards = Array.AsReadOnly(choice.Cards.ToArray()),
            Targets = Array.AsReadOnly(choice.Targets.ToArray()),
            ContentIds = Array.AsReadOnly(choice.ContentIds.ToArray()),
            Parameters = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(choice.Parameters))
        };

    private static GameCommand CloneCommand(GameCommand command) =>
        command switch
        {
            PlayCardCommand play => play with
            {
                TargetSeats = Array.AsReadOnly(play.TargetSeats.ToArray()),
                AdditionalConversionSources = play.AdditionalConversionSources is { } sources
                    ? Array.AsReadOnly(sources.ToArray())
                    : null
            },
            DiscardCardsCommand discard => discard with { CardIds = Array.AsReadOnly(discard.CardIds.ToArray()) },
            UseEquipmentEffectCommand equipment => equipment with
            {
                CardIds = Array.AsReadOnly(equipment.CardIds.ToArray()),
                TargetSeats = Array.AsReadOnly(equipment.TargetSeats.ToArray())
            },
            UseProgramSkillCommand program => program with
            {
                CardIds = Array.AsReadOnly(program.CardIds.ToArray()),
                TargetSeats = Array.AsReadOnly(program.TargetSeats.ToArray())
            },
            _ => command
        };

    private static GeneralDefinition CreateHiddenGeneral() =>
        new(
            "",
            "未知武将",
            "",
            [new GeneralSkillDefinition("未知", "武将尚未公开。")]);

    private static string GetRoleName(Role role) => role switch
    {
        Role.Lord => "主公",
        Role.Loyalist => "忠臣",
        Role.Rebel => "反贼",
        Role.Renegade => "内奸",
        Role.TeamA => "青队",
        Role.TeamB => "赤队",
        _ => role.ToString()
    };

    private string GetPublicGeneralName(CharacterState player) =>
        IsNationalWarMode && !player.GeneralRevealed
            ? "暗将"
            : player.General.Name;

    private static string GetWinnerName(Winner winner) => winner switch
    {
        Winner.LordAndLoyalists => "主公与忠臣",
        Winner.Rebels => "反贼",
        Winner.Renegade => "内奸",
        Winner.TeamA => "青队",
        Winner.TeamB => "赤队",
        Winner.NationalFactionA => "魏势力",
        Winner.NationalFactionB => "蜀势力",
        Winner.NationalFactionC => "独立势力",
        Winner.Draw => "无人",
        _ => "尚未决出"
    };

    private Winner GetWinnerForTeam(string teamId) =>
        GetModeTeamIds().Select((candidate, index) => (candidate, index))
            .Where(item => item.candidate == teamId)
            .Select(item => item.index)
            .FirstOrDefault(-1) switch
        {
            0 => Winner.TeamA,
            1 => Winner.TeamB,
            _ => throw new InvalidOperationException(
                $"Mode '{_modeDefinition.Id}' does not define a supported winning team '{teamId}'.")
        };

    private Winner GetWinnerForFaction(string factionId) =>
        GetModeFactionIds().Select((candidate, index) => (candidate, index))
            .Where(item => item.candidate == factionId)
            .Select(item => item.index)
            .FirstOrDefault(-1) switch
        {
            0 => Winner.NationalFactionA,
            1 => Winner.NationalFactionB,
            _ when GetModeFactionIds().Count > 2 => Winner.NationalFactionC,
            _ => throw new InvalidOperationException(
                $"Mode '{_modeDefinition.Id}' does not define a supported winning faction '{factionId}'.")
        };

    private sealed class CardAttackHandle : IDamageAttempt
    {
        private readonly GameEngine _engine;
        private CardAttackState State => _engine.GetCardAttackState(ResolutionId);
        private void Update(Func<CardAttackState, CardAttackState> update) =>
            _engine.UpdateCardAttackState(ResolutionId, state => update(state ??
                throw new InvalidOperationException("An attack handle lost its owned state.")));
        public CardAttackHandle(GameEngine engine, long resolutionId,
        int sourceSeat,
        int targetSeat,
        Card? card,
        int damageAmount = 1,
        CardKind? playedCardKind = null,
        bool ignoresArmor = false,
        bool isDelayedJudgmentDamage = false,
        int? delayedJudgmentSeat = null,
        DamageNature? damageNatureOverride = null,
        IReadOnlyList<Card>? physicalCards = null,
        long? programJudgmentFrameId = null,
        long? programSkillFrameId = null,
        CardConversionSource? conversionSource = null,
        long? programSkillCardUseFrameId = null)
        {
            _engine = engine;
            ResolutionId = resolutionId;
            engine.InitializeCardAttackState(resolutionId, new CardAttackState
            {
                Active = false,
                SourceSeat = sourceSeat,
                CardUserSeat = sourceSeat,
                TargetSeat = targetSeat,
                DamageAmount = damageAmount,
                IgnoresArmor = ignoresArmor,
                EffectiveCardKind = playedCardKind ?? card?.Kind,
                IsDelayedJudgmentDamage = isDelayedJudgmentDamage,
                ProgramJudgmentFrameId = programJudgmentFrameId,
                ProgramSkillFrameId = programSkillFrameId,
                ProgramSkillCardUseFrameId = programSkillCardUseFrameId,
                DelayedJudgmentSeat = delayedJudgmentSeat,
                DamageNatureOverride = damageNatureOverride,
                ConversionSource = conversionSource,
                CardId = card?.Id,
                AppearanceKind = card?.Kind, AppearanceSuit = card?.Suit, AppearanceRank = card?.Rank,
                PhysicalCardIds = Array.AsReadOnly((physicalCards ?? (card is null ? [] : [card])).Select(item => item.Id).ToArray()),
            });
        }
        public CardAttackHandle(GameEngine engine, long ownerId)
        { _engine = engine; ResolutionId = ownerId; }
        public long ResolutionId { get; }
        public int SourceSeat { get => State.SourceSeat; private set => Update(state => state with { SourceSeat = value }); }
        public int CardUserSeat { get => State.CardUserSeat; private set => Update(state => state with { CardUserSeat = value }); }
        public int TargetSeat { get => State.TargetSeat; private set => Update(state => state with { TargetSeat = value }); }
        public Card? Card => State.CardId is { } id ? _engine.GetAttackCard(id) with
        { Kind = State.AppearanceKind!.Value, Suit = State.AppearanceSuit!.Value, Rank = State.AppearanceRank!.Value } : null;
        public IReadOnlyList<Card> PhysicalCards => State.PhysicalCardIds.Select(_engine.GetAttackCard).ToArray();
        public CardConversionSource? ConversionSource { get => State.ConversionSource; }


        public bool IsTwoCardVirtualSlash =>
            PhysicalCards.Count == 2 && EffectiveCardKind == CardKind.Slash;
        public int DamageAmount { get => State.DamageAmount; private set => Update(state => state with { DamageAmount = value }); }
        public bool DamageAmountFinalized { get => State.DamageAmountFinalized; private set => Update(state => state with { DamageAmountFinalized = value }); }
        public CardKind? EffectiveCardKind { get => State.EffectiveCardKind; }
        public bool IgnoresArmor { get => State.IgnoresArmor; private set => Update(state => state with { IgnoresArmor = value }); }
        public bool IsDelayedJudgmentDamage { get => State.IsDelayedJudgmentDamage; }
        public bool IsSourceLess { get => State.IsSourceLess; private set => Update(state => state with { IsSourceLess = value }); }
        public long? ProgramJudgmentFrameId { get => State.ProgramJudgmentFrameId; }
        public bool IsProgramJudgmentDamage => ProgramJudgmentFrameId is not null;
        public long? ProgramSkillFrameId { get => State.ProgramSkillFrameId; }
        public bool IsProgramSkillDamage => ProgramSkillFrameId is not null;
        public long? ProgramSkillCardUseFrameId { get => State.ProgramSkillCardUseFrameId; }
        public int? DelayedJudgmentSeat { get => State.DelayedJudgmentSeat; }
        public DamageNature? DamageNatureOverride { get => State.DamageNatureOverride; }
        public IReadOnlyList<int> ChainedTargetSeats { get => State.ChainedTargetSeats; private set => Update(state => state with { ChainedTargetSeats = value }); }
        public int ChainedTargetIndex { get => State.ChainedTargetIndex; private set => Update(state => state with { ChainedTargetIndex = value }); }
        public bool IsChainPropagation { get => State.IsChainPropagation; private set => Update(state => state with { IsChainPropagation = value }); }
        public bool FactionDefenseAttempted { get => State.FactionDefenseAttempted; private set => Update(state => state with { FactionDefenseAttempted = value }); }
        public bool FactionSlashAttempted { get => State.FactionSlashAttempted; private set => Update(state => state with { FactionSlashAttempted = value }); }
        public bool CixiongDoubleSwordsResolved { get => State.CixiongDoubleSwordsResolved; private set => Update(state => state with { CixiongDoubleSwordsResolved = value }); }
        public bool IceSwordAttempted { get => State.IceSwordAttempted; private set => Update(state => state with { IceSwordAttempted = value }); }
        public bool QilinBowAttempted { get => State.QilinBowAttempted; private set => Update(state => state with { QilinBowAttempted = value }); }
        public bool DamageWasApplied { get => State.DamageWasApplied; private set => Update(state => state with { DamageWasApplied = value }); }
        public int SourceToTargetDistanceAtDamage { get => State.SourceToTargetDistanceAtDamage; private set => Update(state => state with { SourceToTargetDistanceAtDamage = value }); }
        private long? ResolvedDyingDamageFrameId { get => State.ResolvedDyingDamageFrameId; set => Update(state => state with { ResolvedDyingDamageFrameId = value }); }
        public bool CardUseCausedDamage { get => _engine.GetCardUseCausedDamage(ResolutionId); private set => _engine.SetCardUseCausedDamage(ResolutionId, value); }
        public bool PendingRedBladeDamageBonus { get => State.PendingRedBladeDamageBonus; set => Update(state => state with { PendingRedBladeDamageBonus = value }); }
        public bool DamageRedirected { get => State.DamageRedirected; private set => Update(state => state with { DamageRedirected = value }); }
        public bool BeforeDamageProgramsResolved { get => State.BeforeDamageProgramsResolved; private set => Update(state => state with { BeforeDamageProgramsResolved = value }); }
        private ProgramDamageTransferFollowup? DamageTransferFollowup
        {
            get => State.DamageTransferFollowup is { } receipt
                ? new(receipt.SkillId, receipt.OwnerSeat, receipt.TargetSeat, receipt.DrawLostHp) : null;
            set => Update(state => state with { DamageTransferFollowup = value is { } receipt
                ? new(receipt.SkillId, receipt.OwnerSeat, receipt.TargetSeat, receipt.DrawLostHp) : null });
        }
        public bool ProhibitsDodge { get => State.ProhibitsDodge; private set => Update(state => state with { ProhibitsDodge = value }); }
        public bool ProhibitsTargetHandResponses { get => State.ProhibitsTargetHandResponses; private set => Update(state => state with { ProhibitsTargetHandResponses = value }); }
        public IReadOnlyList<string> ResponseProhibitingSkillNames { get => State.ResponseProhibitingSkillNames; private set => Update(state => state with { ResponseProhibitingSkillNames = value }); }

        public void ProhibitDodgeBy(string skillName)
        {
            if (string.IsNullOrWhiteSpace(skillName))
                throw new ArgumentException("A response prohibition requires a skill name.", nameof(skillName));
            ProhibitsDodge = true;
            if (!ResponseProhibitingSkillNames.Contains(skillName, StringComparer.Ordinal))
                ResponseProhibitingSkillNames = Array.AsReadOnly(ResponseProhibitingSkillNames.Append(skillName).ToArray());
        }
        public void ProhibitTargetHandResponses() => ProhibitsTargetHandResponses = true;
        public int RequiredDodgeResponses { get => State.RequiredDodgeResponses; private set => Update(state => state with { RequiredDodgeResponses = value }); }
        public int SuccessfulDodgeResponses { get => State.SuccessfulDodgeResponses; private set => Update(state => state with { SuccessfulDodgeResponses = value }); }

        public void SetDamageParticipants(int sourceSeat, int targetSeat)
        {
            if (DamageAmountFinalized || IsChainPropagation)
            {
                throw new InvalidOperationException("Damage participants cannot change after damage begins.");
            }

            SourceSeat = sourceSeat;
            TargetSeat = targetSeat;
        }


        public void SetIgnoresArmor(bool value) => IgnoresArmor = value;

        public void ReplaceCardUser(int actorSeat)
        {
            if (DamageAmountFinalized || DamageWasApplied || IsChainPropagation)
                throw new InvalidOperationException("The card user cannot change after damage begins.");
            SourceSeat = actorSeat;
            CardUserSeat = actorSeat;
        }

        public void MarkBeforeDamageProgramsResolved() => BeforeDamageProgramsResolved = true;

        public void ReduceFinalizedDamageAmount(int amount)
        {
            if (!DamageAmountFinalized || amount <= 0 || amount > DamageAmount)
                throw new InvalidOperationException("Damage reduction requires a positive part of the frozen amount.");
            DamageAmount -= amount;
        }

        public void MarkDamageApplied() => DamageWasApplied = true;
        public void IncreaseFinalizedDamageAmount(int amount)
        {
            if (!DamageAmountFinalized || DamageWasApplied || amount <= 0)
                throw new InvalidOperationException("Damage increase requires a positive amount before frozen damage is applied.");
            DamageAmount = checked(DamageAmount + amount);
        }

        public void CaptureDamageDistance(int distance) => SourceToTargetDistanceAtDamage = distance;

        public void MarkDyingResolvedForDamage(long damageFrameId) =>
            ResolvedDyingDamageFrameId = damageFrameId;

        public bool HasResolvedDyingForDamage(long damageFrameId) =>
            ResolvedDyingDamageFrameId == damageFrameId;

        public void SetCardUseCausedDamage(bool value) => CardUseCausedDamage = value;



        public void RedirectFinalizedDamageTarget(int targetSeat,
            ProgramDamageTransferFollowup followup)
        {
            if (!DamageAmountFinalized || DamageRedirected || TargetSeat == targetSeat)
                throw new InvalidOperationException("Damage redirection requires a new recipient and a finalized amount.");
            TargetSeat = targetSeat;
            DamageRedirected = true;
            DamageTransferFollowup = followup;
        }

        public bool TryConsumeProgramDamageTransferFollowup(out ProgramDamageTransferFollowup followup)
        {
            followup = DamageTransferFollowup!;
            DamageTransferFollowup = null;
            return followup is not null;
        }

        public void FinalizeDamageAmount(int bonus, int? maximum = null)
        {
            if (DamageAmountFinalized)
            {
                throw new InvalidOperationException("The attack damage amount is already final.");
            }

            DamageAmount = checked(DamageAmount + bonus);
            if (maximum is { } cap)
            {
                DamageAmount = Math.Min(DamageAmount, cap);
            }
            DamageAmountFinalized = true;
        }

        public void MarkFactionDefenseAttempted()
        {
            if (FactionDefenseAttempted)
            {
                throw new InvalidOperationException("FactionDefense has already been requested for this response window.");
            }

            FactionDefenseAttempted = true;
        }

        public void MarkFactionSlashAttempted()
        {
            if (FactionSlashAttempted)
            {
                throw new InvalidOperationException("FactionSlash has already been requested for this response window.");
            }

            FactionSlashAttempted = true;
        }

        public void SetRequiredDodgeResponses(int count)
        {
            if (count < 1 || SuccessfulDodgeResponses != 0)
            {
                throw new InvalidOperationException("The required Dodge count cannot change after responses begin.");
            }

            RequiredDodgeResponses = count;
        }

        public bool RegisterDodgeResponse()
        {
            if (SuccessfulDodgeResponses >= RequiredDodgeResponses)
            {
                throw new InvalidOperationException("All required Dodge responses have already resolved.");
            }

            SuccessfulDodgeResponses++;
            FactionDefenseAttempted = false;
            return SuccessfulDodgeResponses >= RequiredDodgeResponses;
        }

        public void MarkCixiongDoubleSwordsResolved() => CixiongDoubleSwordsResolved = true;

        public void MarkIceSwordAttempted()
        {
            if (IceSwordAttempted)
            {
                throw new InvalidOperationException("Ice Sword has already resolved for this damage event.");
            }

            IceSwordAttempted = true;
        }

        public void MarkQilinBowAttempted()
        {
            if (QilinBowAttempted)
            {
                throw new InvalidOperationException("Qilin Bow has already resolved for this damage event.");
            }

            QilinBowAttempted = true;
        }

        public void SetChainedTargets(IReadOnlyList<int> targetSeats)
        {
            if (ChainedTargetSeats.Count != 0 || ChainedTargetIndex != 0)
            {
                throw new InvalidOperationException("The elemental chain targets have already been captured.");
            }

            ChainedTargetSeats = Array.AsReadOnly(targetSeats.ToArray());
        }

        public bool TryAdvanceChainedTarget(
            Func<int, bool> isAlive,
            bool resetDamageForTargetModifiers,
            out int fromSeat)
        {
            fromSeat = TargetSeat;
            while (ChainedTargetIndex < ChainedTargetSeats.Count)
            {
                var nextSeat = ChainedTargetSeats[ChainedTargetIndex++];
                if (!isAlive(nextSeat))
                {
                    continue;
                }

                TargetSeat = nextSeat;
                IsChainPropagation = true;
                if (resetDamageForTargetModifiers)
                {
                    DamageAmountFinalized = false;
                }
                _engine.RestoreRecipientScopedDamageBase(this, fromSeat);
                return true;
            }

            return false;
        }
    }

    private sealed class FactionDefenseHandle : ICardContinuationHandle
    {
        private readonly GameEngine _engine;
        public long OwnerFrameId { get; }
        private FactionDefenseState State => _engine.GetCardContinuations(OwnerFrameId).FactionDefense ??
            throw new InvalidOperationException("The FactionDefense continuation lost its owner state.");
        private void Update(Func<FactionDefenseState, FactionDefenseState> update) =>
            _engine.UpdateCardContinuations(OwnerFrameId, state => state with { FactionDefense = update(state.FactionDefense!) });
        public FactionDefenseHandle(GameEngine engine, CardAttackHandle attack,
        int ownerSeat,
        IReadOnlyList<int> candidateSeats,
        FactionResponsePolicySource source)
        {
            _engine = engine;
            OwnerFrameId = attack.ResolutionId;
            engine.UpdateCardContinuations(OwnerFrameId, state => state with { FactionDefense = new FactionDefenseState
            {
                Active = false,
                Source = source,
                OwnerSeat = ownerSeat,
                CandidateSeats = Array.AsReadOnly(candidateSeats.ToArray()),
            } });
        }
        public FactionDefenseHandle(GameEngine engine, long ownerId) { _engine = engine; OwnerFrameId = ownerId; }
        public FactionResponsePolicySource Source { get => State.Source; }
        public CardAttackHandle Attack => new(_engine, OwnerFrameId);
        public int OwnerSeat { get => State.OwnerSeat; }
        public IReadOnlyList<int> CandidateSeats { get => State.CandidateSeats; }
        public int CandidateIndex { get => State.CandidateIndex; set => Update(state => state with { CandidateIndex = value }); }
        public int CurrentCandidateSeat =>
            CandidateIndex < CandidateSeats.Count ? CandidateSeats[CandidateIndex] : -1;
        }

    private sealed class StoneAxeHandle : ICardContinuationHandle
    {
        private readonly GameEngine _engine;
        public long OwnerFrameId { get; }
        private StoneAxeState State => _engine.GetCardContinuations(OwnerFrameId).StoneAxe ??
            throw new InvalidOperationException("The StoneAxe continuation lost its owner state.");
        private void Update(Func<StoneAxeState, StoneAxeState> update) =>
            _engine.UpdateCardContinuations(OwnerFrameId, state => state with { StoneAxe = update(state.StoneAxe!) });
        public StoneAxeHandle(GameEngine engine, CardAttackHandle attack,
        IReadOnlyList<int> candidateCardIds)
        {
            _engine = engine;
            OwnerFrameId = attack.ResolutionId;
            engine.UpdateCardContinuations(OwnerFrameId, state => state with { StoneAxe = new StoneAxeState
            {
                Active = false,
                CandidateCardIds = Array.AsReadOnly(candidateCardIds.ToArray())
            } });
        }
        public StoneAxeHandle(GameEngine engine, long ownerId) { _engine = engine; OwnerFrameId = ownerId; }
        public CardAttackHandle Attack => new(_engine, OwnerFrameId);
        public IReadOnlyList<int> CandidateCardIds => State.CandidateCardIds;
        }



    private sealed class CixiongDoubleSwordsHandle : ICardContinuationHandle
    {
        private readonly GameEngine _engine;
        public long OwnerFrameId { get; }
        private CixiongDoubleSwordsState State => _engine.GetCardContinuations(OwnerFrameId).CixiongDoubleSwords ??
            throw new InvalidOperationException("The CixiongDoubleSwords continuation lost its owner state.");
        private void Update(Func<CixiongDoubleSwordsState, CixiongDoubleSwordsState> update) =>
            _engine.UpdateCardContinuations(OwnerFrameId, state => state with { CixiongDoubleSwords = update(state.CixiongDoubleSwords!) });
        public CixiongDoubleSwordsHandle(GameEngine engine, CardAttackHandle attack)
        {
            _engine = engine;
            OwnerFrameId = attack.ResolutionId;
            engine.UpdateCardContinuations(OwnerFrameId, state => state with { CixiongDoubleSwords = new CixiongDoubleSwordsState
            {
                Active = false,

            } });
        }
        public CixiongDoubleSwordsHandle(GameEngine engine, long ownerId) { _engine = engine; OwnerFrameId = ownerId; }
        public CardAttackHandle Attack => new(_engine, OwnerFrameId);
        public CixiongDoubleSwordsStage Stage { get => State.Stage; set => Update(state => state with { Stage = value }); }
        }

    private sealed class QinglongCrescentBladeHandle : ICardContinuationHandle
    {
        private readonly GameEngine _engine;
        public long OwnerFrameId { get; }
        private QinglongCrescentBladeState State => _engine.GetCardContinuations(OwnerFrameId).QinglongCrescentBlade ??
            throw new InvalidOperationException("The QinglongCrescentBlade continuation lost its owner state.");
        private void Update(Func<QinglongCrescentBladeState, QinglongCrescentBladeState> update) =>
            _engine.UpdateCardContinuations(OwnerFrameId, state => state with { QinglongCrescentBlade = update(state.QinglongCrescentBlade!) });
        public QinglongCrescentBladeHandle(GameEngine engine, CardAttackHandle attack)
        {
            _engine = engine;
            OwnerFrameId = attack.ResolutionId;
            engine.UpdateCardContinuations(OwnerFrameId, state => state with { QinglongCrescentBlade = new QinglongCrescentBladeState
            {
                Active = false,

            } });
        }
        public QinglongCrescentBladeHandle(GameEngine engine, long ownerId) { _engine = engine; OwnerFrameId = ownerId; }
        public CardAttackHandle Attack => new(_engine, OwnerFrameId);
        public bool FactionSlashAttempted { get => State.FactionSlashAttempted; set => Update(state => state with { FactionSlashAttempted = value }); }
        }

    // A Qinglong follow-up Slash resolves while its dodged parent attack may
    // still own a multi-target continuation. Finishing the parent can yield on
    // the next target's response (Fangtian Halberd or a program target-count
    // policy, or a group-card response cursor), so the follow-up suspends that
    // yielded state and restores it once its nested card uses unwind back to
    // the parent's frame.
    private sealed class QinglongFollowupHandle(GameEngine engine, long ownerId) : ICardContinuationHandle
    {
        public long OwnerFrameId => ownerId;
        private QinglongFollowupState State => engine.GetCardContinuations(ownerId).QinglongFollowup ??
            throw new InvalidOperationException("Qinglong lost its suspended continuation.");
        public long OuterResolutionId => ownerId;
        public CardAttackHandle? NextAttack => State.NextAttackOwnerId is { } id ? new(engine, id) : null;
        public FangtianHalberdHandle? FangtianContinuation => State.FangtianOwnerId is { } id ? new(engine, id) : null;
        public PendingDecision? NextDecision => State.Decision is { } decision ? RestoreSuspendedDecision(decision) : null;
        public EngineStatus Status => State.Status;
    }

    private sealed class IceSwordHandle : ICardContinuationHandle
    {
        private readonly GameEngine _engine;
        public long OwnerFrameId { get; }
        private IceSwordState State => _engine.GetCardContinuations(OwnerFrameId).IceSword ??
            throw new InvalidOperationException("The IceSword continuation lost its owner state.");
        private void Update(Func<IceSwordState, IceSwordState> update) =>
            _engine.UpdateCardContinuations(OwnerFrameId, state => state with { IceSword = update(state.IceSword!) });
        public IceSwordHandle(GameEngine engine, CardAttackHandle attack,
        int preventedDamageAmount)
        {
            _engine = engine;
            OwnerFrameId = attack.ResolutionId;
            engine.UpdateCardContinuations(OwnerFrameId, state => state with { IceSword = new IceSwordState
            {
                Active = false,
                PreventedDamageAmount = preventedDamageAmount,
                DiscardedCardIds = [],
            } });
        }
        public IceSwordHandle(GameEngine engine, long ownerId) { _engine = engine; OwnerFrameId = ownerId; }
        public CardAttackHandle Attack => new(_engine, OwnerFrameId);
        public int PreventedDamageAmount { get => State.PreventedDamageAmount; }
        public bool Activated { get => State.Activated; set => Update(state => state with { Activated = value }); }
        public IReadOnlyList<int> DiscardedCardIds { get => State.DiscardedCardIds; }
            public void AddDiscardedCard(int id) => Update(state => state with { DiscardedCardIds = Array.AsReadOnly(state.DiscardedCardIds.Append(id).ToArray()) });
    }

    private sealed class QilinBowHandle : ICardContinuationHandle
    {
        private readonly GameEngine _engine;
        public long OwnerFrameId { get; }
        private QilinBowState State => _engine.GetCardContinuations(OwnerFrameId).QilinBow ??
            throw new InvalidOperationException("The QilinBow continuation lost its owner state.");
        private void Update(Func<QilinBowState, QilinBowState> update) =>
            _engine.UpdateCardContinuations(OwnerFrameId, state => state with { QilinBow = update(state.QilinBow!) });
        public QilinBowHandle(GameEngine engine, CardAttackHandle attack)
        {
            _engine = engine;
            OwnerFrameId = attack.ResolutionId;
            engine.UpdateCardContinuations(OwnerFrameId, state => state with { QilinBow = new QilinBowState
            {
                Active = false,

            } });
        }
        public QilinBowHandle(GameEngine engine, long ownerId) { _engine = engine; OwnerFrameId = ownerId; }
        public CardAttackHandle Attack => new(_engine, OwnerFrameId);
        }

    private sealed class FangtianHalberdHandle : ICardContinuationHandle
    {
        private readonly GameEngine _engine;
        public long OwnerFrameId { get; }
        private FangtianHalberdState State => _engine.GetCardContinuations(OwnerFrameId).FangtianHalberd ??
            throw new InvalidOperationException("The FangtianHalberd continuation lost its owner state.");
        private void Update(Func<FangtianHalberdState, FangtianHalberdState> update) =>
            _engine.UpdateCardContinuations(OwnerFrameId, state => state with { FangtianHalberd = update(state.FangtianHalberd!) });
        public FangtianHalberdHandle(GameEngine engine, long resolutionId,
        int sourceSeat,
        Card card,
        CardKind effectiveCardKind,
        bool ignoresArmor,
        int damageAmount,
        IReadOnlyList<int> targetSeats,
        bool usesFangtian,
        bool countedTowardSlashLimit,
        CardConversionSource? conversionSource = null)
        {
            _engine = engine;
            OwnerFrameId = resolutionId;
            engine.UpdateCardContinuations(OwnerFrameId, state => state with { FangtianHalberd = new FangtianHalberdState
            {
                Active = false,
                SourceSeat = sourceSeat,
                Card = new(card.Id, card.Kind, card.Suit, card.Rank),
                EffectiveCardKind = effectiveCardKind,
                IgnoresArmor = ignoresArmor,
                DamageAmount = damageAmount,
                UsesFangtian = usesFangtian,
                CountedTowardSlashLimit = countedTowardSlashLimit,
                ConversionSource = conversionSource,
                TargetSeats = Array.AsReadOnly(targetSeats.ToArray()),
            } });
        }
        public FangtianHalberdHandle(GameEngine engine, long ownerId) { _engine = engine; OwnerFrameId = ownerId; }
        public long ResolutionId => OwnerFrameId;
        public int SourceSeat { get => State.SourceSeat; }
        public Card Card { get => _engine.ReadCardAppearance(State.Card); }
        public CardKind EffectiveCardKind { get => State.EffectiveCardKind; }
        public bool IgnoresArmor { get => State.IgnoresArmor; }
        public int DamageAmount { get => State.DamageAmount; }
        public bool UsesFangtian { get => State.UsesFangtian; }
        public bool CountedTowardSlashLimit { get => State.CountedTowardSlashLimit; }
        public CardConversionSource? ConversionSource { get => State.ConversionSource; }


        public bool DamageWasApplied { get => _engine.GetCardUseCausedDamage(OwnerFrameId); set => _engine.SetCardUseCausedDamage(OwnerFrameId, value); }
        public IReadOnlyList<int> TargetSeats { get => State.TargetSeats; }
        public int TargetIndex { get => _engine.GetCardUseTargetIndex(OwnerFrameId); set => _engine.SetCardUseTargetIndex(OwnerFrameId, value); }
        public CardAttackHandle? CurrentAttack { get => State.CurrentAttackOwnerId is { } id ? new(_engine, id) : null; set => Update(state => state with { CurrentAttackOwnerId = value?.ResolutionId }); }
        }

    private sealed class BorrowedSwordHandle : ICardContinuationHandle
    {
        private readonly GameEngine _engine;
        public long OwnerFrameId { get; }
        private BorrowedSwordState State => _engine.GetCardContinuations(OwnerFrameId).BorrowedSword ??
            throw new InvalidOperationException("The BorrowedSword continuation lost its owner state.");
        private void Update(Func<BorrowedSwordState, BorrowedSwordState> update) =>
            _engine.UpdateCardContinuations(OwnerFrameId, state => state with { BorrowedSword = update(state.BorrowedSword!) });
        public BorrowedSwordHandle(GameEngine engine, long resolutionId,
        int sourceSeat,
        int weaponOwnerSeat,
        int slashTargetSeat,
        Card card)
        {
            _engine = engine;
            OwnerFrameId = resolutionId;
            engine.UpdateCardContinuations(OwnerFrameId, state => state with { BorrowedSword = new BorrowedSwordState
            {
                Active = false,
                SourceSeat = sourceSeat,
                WeaponOwnerSeat = weaponOwnerSeat,
                SlashTargetSeat = slashTargetSeat,
                Card = new(card.Id, card.Kind, card.Suit, card.Rank),
            } });
        }
        public BorrowedSwordHandle(GameEngine engine, long ownerId) { _engine = engine; OwnerFrameId = ownerId; }
        public long ResolutionId => OwnerFrameId;
        public int SourceSeat { get => State.SourceSeat; }
        public int WeaponOwnerSeat { get => State.WeaponOwnerSeat; }
        public int SlashTargetSeat { get => State.SlashTargetSeat; }
        public Card Card { get => _engine.ReadTieredRoundUseAppearance(OwnerFrameId, State.Card); }
        public bool AwaitingSlashChoice { get => State.AwaitingSlashChoice; set => Update(state => state with { AwaitingSlashChoice = value }); }
        public bool FactionSlashAttempted { get => State.FactionSlashAttempted; set => Update(state => state with { FactionSlashAttempted = value }); }
        public CardAttackHandle? ActiveAttack { get => State.ActiveAttackOwnerId is { } id ? new(_engine, id) : null; set => Update(state => state with { ActiveAttackOwnerId = value?.ResolutionId }); }
        public int? SlashCardId { get => State.SlashCardId; set => Update(state => state with { SlashCardId = value }); }
        public CardKind? EffectiveSlashKind { get => State.EffectiveSlashKind; set => Update(state => state with { EffectiveSlashKind = value }); }
        }

    private sealed class FactionCardRequestHandle : ICardContinuationHandle
    {
        private readonly GameEngine _engine;
        public long OwnerFrameId { get; }
        private FactionCardRequestState State => _engine.GetCardContinuations(OwnerFrameId).FactionCardRequest ??
            throw new InvalidOperationException("The FactionCardRequest continuation lost its owner state.");
        private void Update(Func<FactionCardRequestState, FactionCardRequestState> update) =>
            _engine.UpdateCardContinuations(OwnerFrameId, state => state with { FactionCardRequest = update(state.FactionCardRequest!) });
        public FactionCardRequestHandle(GameEngine engine, long resolutionId,
        FactionCardRequestPurpose purpose,
        int ownerSeat,
        IReadOnlyList<int> candidateSeats,
        string providerFactionId,
        CardAttackHandle? responseAttack = null,
        int? targetSeat = null,
        long? programSkillFrameId = null,
        CardKind requiredKind = CardKind.Slash,
        BorrowedSwordHandle? borrowedSword = null,
        QinglongCrescentBladeHandle? qinglongCrescentBlade = null,
        FactionResponsePolicySource? policySource = null,
        string? assistedResultBind = null)
        {
            _engine = engine;
            OwnerFrameId = resolutionId;
            engine.UpdateCardContinuations(OwnerFrameId, state => state with { FactionCardRequest = new FactionCardRequestState
            {
                Active = false,
                PolicySource = policySource,
                AssistedResultBind = assistedResultBind,
                Purpose = purpose,
                OwnerSeat = ownerSeat,
                CandidateSeats = Array.AsReadOnly(candidateSeats.ToArray()),
                ResponseAttackOwnerId = responseAttack?.ResolutionId,
                TargetSeat = targetSeat,
                ProgramSkillFrameId = programSkillFrameId,
                ProviderFactionId = providerFactionId,
                RequiredKind = requiredKind,
                BorrowedSwordOwnerId = borrowedSword?.ResolutionId,
                QinglongCrescentBladeOwnerId = qinglongCrescentBlade?.OwnerFrameId,
                AwaitingProviders = true,
                ZhuqueFanPhysicalCardIds = [],
            } });
        }
        public FactionCardRequestHandle(GameEngine engine, long ownerId) { _engine = engine; OwnerFrameId = ownerId; }
        public FactionResponsePolicySource? PolicySource { get => State.PolicySource; }
        public string? AssistedResultBind { get => State.AssistedResultBind; }
        public long ResolutionId => OwnerFrameId;
        public FactionCardRequestPurpose Purpose { get => State.Purpose; }
        public int OwnerSeat { get => State.OwnerSeat; }
        public IReadOnlyList<int> CandidateSeats { get => State.CandidateSeats; }
        public CardAttackHandle? ResponseAttack { get => State.ResponseAttackOwnerId is { } id ? new(_engine, id) : null; }
        public int? TargetSeat { get => State.TargetSeat; }
        public long? ProgramSkillFrameId { get => State.ProgramSkillFrameId; }
        public string ProviderFactionId { get => State.ProviderFactionId; }
        public CardKind RequiredKind { get => State.RequiredKind; }
        public BorrowedSwordHandle? BorrowedSword { get => State.BorrowedSwordOwnerId is { } id ? new(_engine, id) : null; }
        public QinglongCrescentBladeHandle? QinglongCrescentBlade { get => State.QinglongCrescentBladeOwnerId is { } id ? new(_engine, id) : null; }
        public int CandidateIndex { get => State.CandidateIndex; set => Update(state => state with { CandidateIndex = value }); }
        public bool AwaitingProviders { get => State.AwaitingProviders; set => Update(state => state with { AwaitingProviders = value }); }
        public bool CostPaid { get => State.CostPaid; set => Update(state => state with { CostPaid = value }); }
        public bool ProviderRewarded { get => State.ProviderRewarded; set => Update(state => state with { ProviderRewarded = value }); }
        public bool AwaitingZhuqueFanChoice { get => State.AwaitingZhuqueFanChoice; set => Update(state => state with { AwaitingZhuqueFanChoice = value }); }
        public int? ZhuqueFanProviderSeat { get => State.ZhuqueFanProviderSeat; set => Update(state => state with { ZhuqueFanProviderSeat = value }); }
        public IReadOnlyList<Card> ZhuqueFanPhysicalCards { get => State.ZhuqueFanPhysicalCardIds.Select(_engine.GetAttackCard).ToArray(); set => Update(state => state with { ZhuqueFanPhysicalCardIds = Array.AsReadOnly(value.Select(card => card.Id).ToArray()) }); }
        public CardAttackHandle? ActiveAttack { get => State.ActiveAttackOwnerId is { } id ? new(_engine, id) : null; set => Update(state => state with { ActiveAttackOwnerId = value?.ResolutionId }); }
        public int CurrentCandidateSeat =>
            CandidateIndex < CandidateSeats.Count ? CandidateSeats[CandidateIndex] : -1;
        public bool IsProgramSkillUse => Purpose is FactionCardRequestPurpose.ProgramSkillUse or FactionCardRequestPurpose.AssistedProgramUse;
        public bool IsAssistedProgramUse => Purpose == FactionCardRequestPurpose.AssistedProgramUse;
        public bool IsBorrowedSwordUse => Purpose == FactionCardRequestPurpose.BorrowedSwordUse;
        public bool IsQinglongCrescentBladeUse => Purpose == FactionCardRequestPurpose.QinglongCrescentBladeUse;
        }

    [Flags]
    private enum DelayedTurnEffects
    {
        None = 0,
        SkipDrawPhase = 1,
        SkipPlayPhase = 2,
        SkipJudgmentPhase = 4,
        SkipDiscardPhase = 8
    }



    private sealed class JizhiResolution(
        int playerSeat,
        long resolutionId,
        Card card,
        CardKind effectiveCardKind,
        IReadOnlyList<int> targetSeats,
        LegalActionKind actionKind,
        int? targetCardId,
        CardKind? requiredCardKind)
    {
        public int PlayerSeat { get; } = playerSeat;
        public long ResolutionId { get; } = resolutionId;
        public Card Card { get; } = card;
        public CardKind EffectiveCardKind { get; } = effectiveCardKind;
        public IReadOnlyList<int> TargetSeats { get; } = Array.AsReadOnly(targetSeats.ToArray());
        public LegalActionKind ActionKind { get; } = actionKind;
        public int? TargetCardId { get; } = targetCardId;
        public CardKind? RequiredCardKind { get; } = requiredCardKind;
    }

    private sealed class DuelHandle : ICardContinuationHandle
    {
        private readonly GameEngine _engine;
        public long OwnerFrameId { get; }
        private DuelState State => _engine.GetCardContinuations(OwnerFrameId).Duel ??
            throw new InvalidOperationException("The Duel continuation lost its owner state.");
        private void Update(Func<DuelState, DuelState> update) =>
            _engine.UpdateCardContinuations(OwnerFrameId, state => state with { Duel = update(state.Duel!) });
        public DuelHandle(GameEngine engine, CardAttackHandle attack)
        {
            _engine = engine;
            OwnerFrameId = attack.ResolutionId;
            engine.UpdateCardContinuations(OwnerFrameId, state => state with { Duel = new DuelState
            {
                Active = false,
                ResponderSeat = attack.TargetSeat,
            } });
        }
        public DuelHandle(GameEngine engine, long ownerId) { _engine = engine; OwnerFrameId = ownerId; }
        public CardAttackHandle Attack => new(_engine, OwnerFrameId);
        public long ResolutionId => Attack.ResolutionId;
        public int SourceSeat => Attack.SourceSeat;
        public int TargetSeat => Attack.TargetSeat;
        public int ResponderSeat { get => State.ResponderSeat; set => Update(state => state with { ResponderSeat = value }); }
        public int OpponentSeat => ResponderSeat == SourceSeat ? TargetSeat : SourceSeat;
        public bool FactionSlashAttempted { get => State.FactionSlashAttempted; set => Update(state => state with { FactionSlashAttempted = value }); }
        public int SuccessfulSlashResponses { get => State.SuccessfulSlashResponses; private set => Update(state => state with { SuccessfulSlashResponses = value }); }

        public bool RegisterSlashResponse(int requiredCount)
        {
            if (requiredCount < 1 || SuccessfulSlashResponses >= requiredCount)
            {
                throw new InvalidOperationException("The Duel Slash response count is invalid.");
            }

            SuccessfulSlashResponses++;
            FactionSlashAttempted = false;
            if (SuccessfulSlashResponses < requiredCount)
            {
                return false;
            }

            SuccessfulSlashResponses = 0;
            ResponderSeat = OpponentSeat;
            return true;
        }
        }



    private enum TargetCardEffect
    {
        Discard,
        Take
    }

    private sealed class GroupCardHandle : ICardContinuationHandle
    {
        private readonly GameEngine _engine;
        public long OwnerFrameId { get; }
        private GroupCardState State => _engine.GetCardContinuations(OwnerFrameId).GroupCard ??
            throw new InvalidOperationException("The GroupCard continuation lost its owner state.");
        private void Update(Func<GroupCardState, GroupCardState> update) =>
            _engine.UpdateCardContinuations(OwnerFrameId, state => state with { GroupCard = update(state.GroupCard!) });
        public GroupCardHandle(GameEngine engine, long resolutionId,
        int sourceSeat,
        Card card,
        IReadOnlyList<int> targetSeats,
        GroupCardEffect effect,
        CardKind? requiredCardKind,
        IReadOnlyList<Card>? physicalCards = null)
        {
            _engine = engine;
            OwnerFrameId = resolutionId;
            engine.UpdateCardContinuations(OwnerFrameId, state => state with { GroupCard = new GroupCardState
            {
                Active = false,
                SourceSeat = sourceSeat,
                DamageSourceSeat = sourceSeat,
                Card = new(card.Id, card.Kind, card.Suit, card.Rank),
                TargetSeats = Array.AsReadOnly(targetSeats.ToArray()),
                Effect = effect,
                RequiredCardKind = requiredCardKind,
                PhysicalCardIds = Array.AsReadOnly((physicalCards ?? [card]).Select(card => card.Id).ToArray()),
                DamageClaimedPhysicalCardIds = [],
                RevealedCardIds = [],
            } });
        }
        public GroupCardHandle(GameEngine engine, long ownerId) { _engine = engine; OwnerFrameId = ownerId; }
        public long ResolutionId => OwnerFrameId;
        public int SourceSeat { get => State.SourceSeat; }
        public int DamageSourceSeat { get => State.DamageSourceSeat; set => Update(state => state with { DamageSourceSeat = value }); }
        public Card Card { get => _engine.ReadTieredRoundUseAppearance(OwnerFrameId, State.Card); }
        public IReadOnlyList<int> TargetSeats { get => State.TargetSeats; }
        public GroupCardEffect Effect { get => State.Effect; }
        public CardKind? RequiredCardKind { get => State.RequiredCardKind; }
        public IReadOnlyList<Card> PhysicalCards { get => State.PhysicalCardIds.Select(_engine.GetAttackCard).ToArray(); }
        public int TargetIndex { get => _engine.GetCardUseTargetIndex(OwnerFrameId); set => _engine.SetCardUseTargetIndex(OwnerFrameId, value); }
        public CardAttackHandle? CurrentAttack { get => State.CurrentAttackOwnerId is { } id ? new(_engine, id) : null; set => Update(state => state with { CurrentAttackOwnerId = value?.ResolutionId }); }
        public IReadOnlyList<int> DamageClaimedPhysicalCardIds { get => State.DamageClaimedPhysicalCardIds; }
        public IReadOnlyList<int> RevealedCardIds { get => State.RevealedCardIds; }
            public void AddRevealedCard(int id) => Update(state => state with { RevealedCardIds = Array.AsReadOnly(state.RevealedCardIds.Append(id).ToArray()) });
        public void RemoveRevealedCard(int id) => Update(state => state with { RevealedCardIds = Array.AsReadOnly(state.RevealedCardIds.Where(item => item != id).ToArray()) });
        public void ClearRevealedCards() => Update(state => state with { RevealedCardIds = [] });
        public void SetClaimedPhysicalCards(IReadOnlyList<int> ids) => Update(state => state with { DamageClaimedPhysicalCardIds = Array.AsReadOnly(ids.ToArray()) });
    }

    private abstract record EngineNotification;

    private sealed record DyingCompletionReceipt(
        long FrameId,
        long ParentFrameId,
        int VictimSeat,
        DyingContinuationKind Continuation);

    private readonly record struct JudgmentCompletionReceipt(JudgmentFrame Frame, bool Succeeded);


    private sealed record LogNotification(GameLogEntry Entry) : EngineNotification;

    private sealed record AiThoughtNotification(AiThoughtRecord Thought) : EngineNotification;

    private sealed record AiGeneralThoughtNotification(AiGeneralThought Thought) : EngineNotification;

    private sealed record CardMovedNotification(CardMovementRecord Movement) : EngineNotification;

    private sealed record EventNotification(EventEnvelope Event) : EngineNotification;
}
