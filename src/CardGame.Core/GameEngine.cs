namespace CardGame.Core;

/// <summary>
/// A synchronous, explicit state machine. Advance runs AI turns until it reaches
/// a human decision. WPF stays responsive by calling these short methods itself;
/// no UI type, dispatcher, timer, or blocking wait exists in the rules assembly.
/// </summary>
public sealed class GameEngine
{
    private readonly GameOptions _options;
    private readonly ContentRegistry? _contentRegistry;
    private readonly ContentModeDefinition _modeDefinition;
    private readonly IReadOnlyList<GeneralDefinition> _generalPool;
    private readonly DeterministicRandom _random;
    private readonly int _playerCount;
    private readonly List<PlayerRuntime> _players = [];
    private readonly List<GeneralDefinition> _availableGenerals = [];
    private readonly List<int> _selectionOrder = [];
    private readonly CardZoneStore _cardZones;
    private readonly List<GameLogEntry> _log = [];
    private readonly List<AiThoughtRecord> _aiThoughts = [];
    private readonly List<AiGeneralThought> _aiGeneralThoughts = [];
    private readonly List<CardMovementRecord> _cardMovements = [];
    private readonly List<ResolutionFrame> _resolutionStack = [];
    private readonly List<EventEnvelope> _events = [];
    private readonly List<IGameEvent> _pendingEvents = [];
    private readonly List<GameCommand> _acceptedCommands = [];
    private readonly List<ObserverFailure> _observerFailures = [];
    private readonly Queue<EngineNotification> _pendingNotifications = [];
    private readonly Dictionary<int, SimpleAiBrain> _aiBrains = [];
    private GameSnapshot? _pendingStateSnapshot;

    private EngineStatus _status = EngineStatus.NotStarted;
    private Winner _winner = Winner.None;
    private TurnPhase _phase = TurnPhase.NotStarted;
    private int _turnNumber;
    private int _currentSeat;
    private int _slashCountThisTurn;
    private int _logSequence;
    private int _thoughtSequence;
    private int _movementSequence;
    private int _observerFailureSequence;
    private long _eventSequence;
    private long _resolutionSequence;
    private int _initialCardCount;
    private int _initialHandSize;
    private int _drawPerTurn;
    private long _revision;
    private long _nextPromptId;
    private int _selectionIndex;
    private bool _started;
    private bool _setupComplete;
    private bool _isExecutingPublicOperation;
    private PendingDecision? _pendingDecision;
    private AttackResolution? _pendingAttack;
    private DuelResolution? _pendingDuel;
    private GroupCardResolution? _pendingGroupCard;
    private FireAttackResolution? _pendingFireAttack;
    private DyingResolution? _pendingDying;
    private DamageTriggerResolution? _pendingDamageTrigger;
    private DamageSkillResolution? _pendingDamageSkill;

    private GameEngine(GameOptions options, ContentRegistry? contentRegistry)
    {
        ValidateOptions(options);
        _options = options;
        _contentRegistry = contentRegistry;
        _modeDefinition = ResolveModeDefinition(contentRegistry, options);
        _generalPool = CreateRuntimeGeneralPool(contentRegistry, _modeDefinition);
        _playerCount = options.PlayerCount;
        _cardZones = new CardZoneStore(_playerCount);
        _random = new DeterministicRandom(options.Seed);
        var deckDefinition = ResolveDeckDefinition(
            contentRegistry,
            options.DeckId ?? _modeDefinition.DeckId);
        _initialHandSize = deckDefinition?.InitialHandSize ?? StandardDeckCatalog.BasicDemo.InitialHandSize;
        _drawPerTurn = deckDefinition?.DrawPerTurn ?? StandardDeckCatalog.BasicDemo.DrawPerTurn;
        SetupPlayers();
        SetupDeck();
        if (!options.UseInteractiveSetup)
        {
            DealInitialHands();
            _setupComplete = true;
        }
        _currentSeat = _players.Single(player => player.Role == Role.Lord).Seat;

        foreach (var player in _players.Where(player => !player.IsHuman))
        {
            _aiBrains[player.Seat] = new SimpleAiBrain(
                player.Seat,
                unchecked(options.Seed * 397) ^ (player.Seat + 1));
        }
    }

    public static GameEngine CreateStandard(
        GameOptions? options = null,
        ContentRegistry? contentRegistry = null) =>
        new(options ?? new GameOptions(), contentRegistry);

    /// <summary>The deterministic match seed for local diagnostics and replay labels.</summary>
    public int Seed => _options.Seed;

    /// <summary>
    /// Monotonically increasing public state revision. Player commands must carry
    /// the revision they observed so stale decisions cannot overwrite newer state.
    /// </summary>
    public long Revision => _revision;

    /// <summary>
    /// The immutable content registry used by this match, when a host supplied
    /// the formal package catalogue. A null value means legacy Core content.
    /// </summary>
    public ContentRegistry? ContentRegistry => _contentRegistry;

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

        var baseDistance = GetAliveSeatDistance(sourceSeat, targetSeat);
        var sourceModifier = GetEquipment(sourceSeat)
            .Select(card => EquipmentCatalog.Get(card.Kind).OutgoingDistanceModifier)
            .Sum();
        var targetModifier = GetEquipment(targetSeat)
            .Select(card => EquipmentCatalog.Get(card.Kind).IncomingDistanceModifier)
            .Sum();
        return Math.Max(1, baseDistance + sourceModifier + targetModifier);
    }

    /// <summary>
    /// Returns the current public attack range. A player without a weapon has
    /// range one; equipment may increase it through a data-only modifier.
    /// </summary>
    public int GetAttackRange(int sourceSeat)
    {
        ValidatePlayerSeat(sourceSeat, nameof(sourceSeat));
        var bonus = GetEquipment(sourceSeat)
            .Select(card => EquipmentCatalog.Get(card.Kind).AttackRangeBonus)
            .Sum();
        return Math.Max(1, 1 + bonus);
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
    /// Submits one data-only command through the same exclusive boundary used by
    /// the legacy WPF methods. Player-input errors are typed results, not exceptions.
    /// </summary>
    public CommandResult Submit(GameCommand? command)
    {
        if (_isExecutingPublicOperation)
        {
            return Reject(
                CommandErrorCode.ReentrantOperation,
                "GameEngine is dispatching a committed operation; submit again after the callback returns.");
        }

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

        var result = command switch
        {
            StartGameCommand start => SubmitStart(start),
            AdvanceCommand advance => SubmitAdvance(advance),
            SelectGeneralCommand selectGeneral => SubmitSelectGeneral(selectGeneral),
            PlayCardCommand play => SubmitPlayCard(play),
            EndPlayPhaseCommand end => SubmitEndPlay(end),
            AnswerPromptCommand answer => SubmitPromptAnswer(
                answer.ActorSeat,
                answer.Prompt,
                answer.Choice),
            RespondCommand response => SubmitPromptAnswer(
                response.ActorSeat,
                response.Prompt,
                response.Choice),
            _ => Reject(CommandErrorCode.UnsupportedCommand, "The command type is not supported by this engine.")
        };

        if (result.Accepted)
        {
            _acceptedCommands.Add(CloneCommand(command));
        }

        return result;
    }

    private CommandResult SubmitStart(StartGameCommand command)
    {
        if (_started)
        {
            return Reject(CommandErrorCode.AlreadyStarted, "The game has already started.");
        }

        return Accept(StartCore);
    }

    private CommandResult SubmitAdvance(AdvanceCommand command)
    {
        if (command.ActorSeat != -1)
        {
            return Reject(CommandErrorCode.InvalidActor, "Advance is a host command and must use actor seat -1.");
        }

        if (!_started)
        {
            return Reject(CommandErrorCode.NotStarted, "Call StartGameCommand before advancing the game.");
        }

        if (_winner != Winner.None)
        {
            return Reject(CommandErrorCode.Completed, "The game is already completed.");
        }

        return Accept(AdvanceToHumanBoundary);
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

        return Accept(() => HumanSelectGeneralCore(command.GeneralId, advanceToHumanBoundary: true));
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
        if (!GetHand(actor).Any(card => card.Id == command.CardId))
        {
            return Reject(CommandErrorCode.InvalidCard, "The selected card is not in the actor's hand.");
        }

        var legal = BuildLegalActions(actor)
            .Where(action => action.CardId == command.CardId && action.Kind != LegalActionKind.EndPlay)
            .ToArray();
        var card = GetHand(actor).Single(candidate => candidate.Id == command.CardId);
        var action = SelectPlayAction(
            legal,
            card,
            targets,
            command.PlayedCardKind);

        if (action is null)
        {
            return Reject(
                legal.Length == 0 ? CommandErrorCode.IllegalAction : CommandErrorCode.InvalidTarget,
                "The selected card and exact target list are not a legal choice for this prompt.");
        }

        return Accept(() => HumanPlayCore(
            command.CardId,
            action.TargetSeat,
            advanceToHumanBoundary: true,
            playedCardKind: action.PlayedCardKind));
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

        return Accept(() => HumanEndPlayCore(advanceToHumanBoundary: true));
    }

    private CommandResult SubmitPromptAnswer(int actorSeat, PromptId prompt, ChoiceId choice)
    {
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
            pending.Kind is not (DecisionKind.RespondDodge or
                DecisionKind.RespondSlash or
                DecisionKind.RescueDying or
                DecisionKind.SelectHarvestCard or
                DecisionKind.FireAttackReveal or
                DecisionKind.FireAttackDiscard or
                DecisionKind.Feedback or
                DecisionKind.Yiji or
                DecisionKind.Jieming or
                DecisionKind.Yuanhu))
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

        if (pending.Kind == DecisionKind.RescueDying)
        {
            return SubmitDyingPromptAnswer(selected);
        }

        if (pending.Kind == DecisionKind.SelectHarvestCard)
        {
            return SubmitHarvestPromptAnswer(selected);
        }

        if (pending.Kind is DecisionKind.FireAttackReveal or DecisionKind.FireAttackDiscard)
        {
            return SubmitFireAttackPromptAnswer(selected);
        }

        if (pending.Kind == DecisionKind.Feedback)
        {
            return SubmitFeedbackPromptAnswer(selected);
        }

        if (pending.Kind == DecisionKind.Yiji)
        {
            return SubmitYijiPromptAnswer(selected);
        }

        if (pending.Kind == DecisionKind.Jieming)
        {
            return SubmitJiemingPromptAnswer(selected);
        }

        if (pending.Kind == DecisionKind.Yuanhu)
        {
            return SubmitYuanhuPromptAnswer(selected);
        }

        if (pending.Kind == DecisionKind.RespondSlash)
        {
            return SubmitSlashPromptAnswer(selected);
        }

        if (!selected.Parameters.TryGetValue("response", out var response))
        {
            return Reject(CommandErrorCode.InvalidChoice, "The prompt choice has no supported response effect.");
        }

        return response switch
        {
            "dodge" when selected.Cards.Count == 1 => Accept(() => HumanRespondCore(
                useDodge: true,
                requestedDodgeCardId: selected.Cards[0],
                requestedResponseCardKind: ReadResponseCardKind(selected),
                advanceToHumanBoundary: true)),
            "take-damage" when selected.Cards.Count == 0 => Accept(() => HumanRespondCore(
                useDodge: false,
                requestedDodgeCardId: null,
                requestedResponseCardKind: null,
                advanceToHumanBoundary: true)),
            _ => Reject(CommandErrorCode.InvalidChoice, "The prompt choice is malformed for this response window.")
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
            advanceToHumanBoundary: true));
    }

    private CommandResult SubmitFireAttackPromptAnswer(PromptChoice selected)
    {
        if (_pendingFireAttack is not { } pending ||
            !selected.Parameters.TryGetValue("response", out var response))
        {
            return Reject(CommandErrorCode.InvalidPrompt, "There is no FireAttack selection to answer.");
        }

        return response switch
        {
            "fire-attack-reveal" when pending.RevealedCardId is null &&
                                      selected.Cards.Count == 1 &&
                                      selected.Targets.Count == 0 =>
                Accept(() => HumanFireAttackCardCore(
                    selected.Cards[0],
                    advanceToHumanBoundary: true)),
            "fire-attack-discard" when pending.RevealedCardId is { } &&
                                       selected.Cards.Count == 1 &&
                                       selected.Targets.Count == 0 =>
                Accept(() => HumanFireAttackCardCore(
                    selected.Cards[0],
                    advanceToHumanBoundary: true)),
            "fire-attack-skip" when pending.RevealedCardId is { } &&
                                   selected.Cards.Count == 0 &&
                                   selected.Targets.Count == 0 =>
                Accept(() => HumanFireAttackSkipCore(advanceToHumanBoundary: true)),
            _ => Reject(CommandErrorCode.InvalidChoice, "The choice is malformed for the FireAttack prompt.")
        };
    }

    private CommandResult SubmitFeedbackPromptAnswer(PromptChoice selected)
    {
        if (_pendingDamageSkill is not { } pending)
        {
            return Reject(CommandErrorCode.InvalidPrompt, "There is no Feedback continuation to answer.");
        }

        if (!selected.Parameters.TryGetValue("response", out var response))
        {
            return Reject(CommandErrorCode.InvalidChoice, "The Feedback prompt has no supported response effect.");
        }

        return response switch
        {
            "feedback" when selected.Cards.Count == 1 &&
                            selected.Cards[0] == pending.Card.Id &&
                            selected.Targets.Count == 0 =>
                Accept(() => HumanFeedbackCore(useFeedback: true, advanceToHumanBoundary: true)),
            "take-damage" when selected.Cards.Count == 0 && selected.Targets.Count == 0 =>
                Accept(() => HumanFeedbackCore(useFeedback: false, advanceToHumanBoundary: true)),
            _ => Reject(CommandErrorCode.InvalidChoice, "The choice is malformed for the Feedback trigger.")
        };
    }

    private CommandResult SubmitYijiPromptAnswer(PromptChoice selected)
    {
        if (_pendingDamageSkill is not { } pending ||
            pending.Effect != DamageSkillEffectKind.GiftDrawnCard ||
            _pendingDecision is not { Kind: DecisionKind.Yiji } decision)
        {
            return Reject(CommandErrorCode.InvalidPrompt, "There is no Yiji continuation to answer.");
        }

        if (!selected.Parameters.TryGetValue("response", out var response))
        {
            return Reject(CommandErrorCode.InvalidChoice, "The Yiji prompt has no supported response effect.");
        }

        return response switch
        {
            "yiji-gift" when selected.Cards.Count == 1 &&
                             selected.Targets.Count == 1 &&
                             pending.EffectCardIds.Contains(selected.Cards[0]) &&
                             selected.Targets[0] != pending.OwnerSeat &&
                             decision.ValidTargetSeats.Contains(selected.Targets[0]) &&
                             IsValidPlayerSeat(selected.Targets[0]) =>
                Accept(() => HumanYijiCore(
                    selected.Cards[0],
                    selected.Targets[0],
                    advanceToHumanBoundary: true)),
            "yiji-skip" when selected.Cards.Count == 0 && selected.Targets.Count == 0 =>
                Accept(() => HumanYijiCore(
                    selectedCardId: null,
                    selectedTargetSeat: null,
                    advanceToHumanBoundary: true)),
            _ => Reject(CommandErrorCode.InvalidChoice, "The choice is malformed for the Yiji trigger.")
        };
    }

    private CommandResult SubmitJiemingPromptAnswer(PromptChoice selected)
    {
        if (_pendingDamageSkill is not { Effect: DamageSkillEffectKind.DrawToMaxHand } pending ||
            _pendingDecision is not { Kind: DecisionKind.Jieming } decision)
        {
            return Reject(CommandErrorCode.InvalidPrompt, "There is no Jieming continuation to answer.");
        }

        if (!selected.Parameters.TryGetValue("response", out var response))
        {
            return Reject(CommandErrorCode.InvalidChoice, "The Jieming prompt has no supported response effect.");
        }

        return response switch
        {
            "jieming-draw" when selected.Cards.Count == 0 &&
                                selected.Targets.Count == 1 &&
                                decision.ValidTargetSeats.Contains(selected.Targets[0]) &&
                                IsValidPlayerSeat(selected.Targets[0]) =>
                Accept(() => HumanJiemingCore(
                    selected.Targets[0],
                    advanceToHumanBoundary: true)),
            "jieming-skip" when selected.Cards.Count == 0 && selected.Targets.Count == 0 =>
                Accept(() => HumanJiemingCore(
                    selectedTargetSeat: null,
                    advanceToHumanBoundary: true)),
            _ => Reject(CommandErrorCode.InvalidChoice, "The choice is malformed for the Jieming trigger.")
        };
    }

    private CommandResult SubmitYuanhuPromptAnswer(PromptChoice selected)
    {
        if (_pendingDamageSkill is not { Effect: DamageSkillEffectKind.RecoverDamageTarget } pending ||
            _pendingDecision is not { Kind: DecisionKind.Yuanhu } decision)
        {
            return Reject(CommandErrorCode.InvalidPrompt, "There is no Yuanhu continuation to answer.");
        }

        if (!selected.Parameters.TryGetValue("response", out var response))
        {
            return Reject(CommandErrorCode.InvalidChoice, "The Yuanhu prompt has no supported response effect.");
        }

        return response switch
        {
            "yuanhu" when selected.Cards.Count == 1 &&
                           selected.Targets.Count == 1 &&
                           decision.ValidCardIds.Contains(selected.Cards[0]) &&
                           decision.ValidTargetSeats.Contains(selected.Targets[0]) &&
                           selected.Targets[0] == pending.Attack.TargetSeat &&
                           IsValidPlayerSeat(selected.Targets[0]) =>
                Accept(() => HumanYuanhuCore(
                    selected.Cards[0],
                    advanceToHumanBoundary: true)),
            "yuanhu-skip" when selected.Cards.Count == 0 && selected.Targets.Count == 0 =>
                Accept(() => HumanYuanhuCore(
                    selectedCardId: null,
                    advanceToHumanBoundary: true)),
            _ => Reject(CommandErrorCode.InvalidChoice, "The choice is malformed for the Yuanhu trigger.")
        };
    }

    private CommandResult SubmitSlashPromptAnswer(PromptChoice selected)
    {
        if (!selected.Parameters.TryGetValue("response", out var response))
        {
            return Reject(CommandErrorCode.InvalidChoice, "The Slash response prompt has no supported response effect.");
        }

        return response switch
        {
            "slash" when selected.Cards.Count == 1 => Accept(() => HumanSlashResponseCore(
                useSlash: true,
                requestedSlashCardId: selected.Cards[0],
                requestedResponseCardKind: ReadResponseCardKind(selected),
                advanceToHumanBoundary: true)),
            "take-damage" when selected.Cards.Count == 0 => Accept(() => HumanSlashResponseCore(
                useSlash: false,
                requestedSlashCardId: null,
                requestedResponseCardKind: null,
                advanceToHumanBoundary: true)),
            _ => Reject(CommandErrorCode.InvalidChoice, "The choice is malformed for this Slash response window.")
        };
    }

    private CommandResult SubmitDyingPromptAnswer(PromptChoice selected)
    {
        if (!selected.Parameters.TryGetValue("response", out var response))
        {
            return Reject(CommandErrorCode.InvalidChoice, "The dying prompt choice has no supported response effect.");
        }

        return response switch
        {
            "peach" when selected.Cards.Count == 1 => Accept(() => HumanDyingResponseCore(
                usePeach: true,
                requestedPeachCardId: selected.Cards[0],
                useAlcohol: false,
                requestedAlcoholCardId: null,
                advanceToHumanBoundary: true)),
            "alcohol" when selected.Cards.Count == 1 => Accept(() => HumanDyingResponseCore(
                usePeach: false,
                requestedPeachCardId: null,
                useAlcohol: true,
                requestedAlcoholCardId: selected.Cards[0],
                advanceToHumanBoundary: true)),
            "let-die" when selected.Cards.Count == 0 => Accept(() => HumanDyingResponseCore(
                usePeach: false,
                requestedPeachCardId: null,
                useAlcohol: false,
                requestedAlcoholCardId: null,
                advanceToHumanBoundary: true)),
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

    private CommandResult Accept(Func<EngineRunResult> operation)
    {
        var result = ExecuteExclusive(operation);
        return new CommandResult(true, null, _revision, result);
    }

    private CommandResult Reject(CommandErrorCode code, string message) =>
        new(false, new CommandError(code, message), _revision, BuildResult());

    public EngineRunResult Start() => ExecuteExclusive(StartCore);

    private EngineRunResult StartCore()
    {
        if (_started)
        {
            throw new InvalidOperationException("The game has already started.");
        }

        _started = true;
        _status = EngineStatus.Running;
        AddLog("GameStarted", $"{_playerCount}人身份局开始，主公先行动。");
        if (_setupComplete)
        {
            QueueGameEvent(new GameStartedEvent(_playerCount, _modeDefinition.Id));
        }
        else
        {
            QueueGameEvent(new SetupStartedEvent(_playerCount, _modeDefinition.Id));
        }
        var yijiRules = _players.Any(player => player.General.Skill == SkillKind.Yiji)
            ? "，郭嘉的遗计在受伤后摸两张牌并私有选择一张交给其他存活角色"
            : string.Empty;
        var jiemingRules = _players.Any(player => player.General.Skill == SkillKind.Jieming)
            ? "，荀彧的节命在受伤后可令一名手牌数少于体力上限的角色摸牌至上限"
            : string.Empty;
        var yuanhuRules = _players.Any(player => player.General.Skill == SkillKind.Yuanhu)
            ? "，援护者可在其他角色受伤后弃牌令其回复 1 点体力"
            : string.Empty;
        AddLog(
            "Rules",
            $"{FormatRoleSummary(_modeDefinition)}；模式 {_modeDefinition.Id}；牌堆含杀、火杀、雷杀、闪、桃、酒、决斗、无中生有、南蛮入侵、万箭齐发、桃园结义、五谷丰登、过河拆桥、顺手牵羊、火攻。演示版不计算距离，酒可使本回合下一张直接杀伤害 +1，也可在濒死时仅自救 1 点体力，桃可在出牌阶段自救或在基础濒死窗口救援，桃园结义按座次使所有存活角色各回复 1 点体力，五谷丰登公开翻牌并按座次私有选牌，火攻通过目标私有展示和攻击者同花色弃牌决定是否造成火焰伤害{yijiRules}{jiemingRules}{yuanhuRules}，弃牌自动处理。");
        PublishState();
        return AdvanceToHumanBoundary();
    }

    /// <summary>
    /// Runs deterministic AI decisions until a human input boundary or game end.
    /// Calling this while input is already pending is harmless and returns immediately.
    /// </summary>
    public EngineRunResult Advance() => ExecuteExclusive(AdvanceToHumanBoundary);

    private EngineRunResult AdvanceToHumanBoundary()
    {
        EnsureStarted();
        if (_winner != Winner.None || IsHumanDecisionPending())
        {
            return BuildResult();
        }

        var guard = 0;
        while (_winner == Winner.None && !IsHumanDecisionPending())
        {
            if (++guard > 20_000)
            {
                // Never cut an in-flight response in half. The current demo has a
                // single-step AI Dodge continuation, so finish it and evaluate the
                // guard again at the next stable boundary.
                if (IsAiResponsePending() ||
                    IsAiDyingResponsePending() ||
                    IsAiHarvestPending() ||
                    IsAiFireAttackPending() ||
                    IsAiDamageSkillPending())
                {
                    RunOneEngineStep();
                    continue;
                }

                EndAsDraw("规则循环超过安全上限");
                break;
            }

            RunOneEngineStep();
        }

        return BuildResult();
    }

    /// <summary>
    /// Advances exactly one state-machine step (begin a turn, execute one AI play,
    /// request human input, or finish discard/end-turn) and then returns. This is
    /// useful for a UI that wants to animate or inspect each AI decision.
    /// </summary>
    public EngineRunResult AdvanceOneStep() => ExecuteExclusive(AdvanceOneStepCore);

    private EngineRunResult AdvanceOneStepCore()
    {
        EnsureStarted();
        if (_winner == Winner.None && !IsHumanDecisionPending())
        {
            RunOneEngineStep();
        }

        return BuildResult();
    }

    public IReadOnlyList<LegalAction> GetHumanLegalActions()
    {
        if (_pendingDecision?.Kind != DecisionKind.PlayCard ||
            _pendingDecision.PlayerSeat != _options.HumanSeat)
        {
            return [];
        }

        return BuildLegalActions(_players[_options.HumanSeat]);
    }

    /// <summary>
    /// Compatibility adapter for a local UI that has not migrated to
    /// <see cref="SelectGeneralCommand"/> yet.
    /// </summary>
    public EngineRunResult HumanSelectGeneral(
        string generalId,
        bool advanceToHumanBoundary = true) =>
        ExecuteExclusive(() => HumanSelectGeneralCore(generalId, advanceToHumanBoundary));

    private EngineRunResult HumanSelectGeneralCore(
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
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    public EngineRunResult HumanPlay(
        int cardId,
        int? targetSeat = null,
        bool advanceToHumanBoundary = true,
        CardKind? playedCardKind = null) =>
        ExecuteExclusive(() => HumanPlayCore(
            cardId,
            targetSeat,
            advanceToHumanBoundary,
            playedCardKind));

    private EngineRunResult HumanPlayCore(
        int cardId,
        int? targetSeat,
        bool advanceToHumanBoundary,
        CardKind? playedCardKind = null)
    {
        RequireHumanDecision(DecisionKind.PlayCard);
        var actor = _players[_options.HumanSeat];
        var card = GetHand(actor).SingleOrDefault(candidate => candidate.Id == cardId);
        var legal = card is null
            ? []
            : BuildLegalActions(actor)
                .Where(action => action.CardId == cardId && action.Kind != LegalActionKind.EndPlay)
                .ToArray();
        var action = card is null
            ? null
            : SelectPlayAction(
                legal,
                card,
                targetSeat is { } target ? [target] : [],
                playedCardKind);

        if (action is null || action.Kind == LegalActionKind.EndPlay)
        {
            throw new InvalidOperationException("The selected card and target are not a legal play.");
        }

        ClearPendingDecision();
        ExecutePlay(actor, action);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    public EngineRunResult HumanEndPlay(bool advanceToHumanBoundary = true) =>
        ExecuteExclusive(() => HumanEndPlayCore(advanceToHumanBoundary));

    private EngineRunResult HumanEndPlayCore(bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.PlayCard);
        ClearPendingDecision();
        BeginDiscardPhase();
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    public EngineRunResult HumanRespond(
        bool useDodge,
        bool advanceToHumanBoundary = true) =>
        ExecuteExclusive(() => HumanRespondCore(
            useDodge,
            requestedDodgeCardId: null,
            requestedResponseCardKind: null,
            advanceToHumanBoundary: advanceToHumanBoundary));

    /// <summary>
    /// Compatibility adapter for answering the optional Feedback trigger
    /// without constructing an <see cref="AnswerPromptCommand"/>.
    /// </summary>
    public EngineRunResult HumanRespondFeedback(
        bool useFeedback,
        bool advanceToHumanBoundary = true) =>
        ExecuteExclusive(() => HumanFeedbackCore(useFeedback, advanceToHumanBoundary));

    /// <summary>
    /// Compatibility adapter for a local host that wants to answer a dying
    /// response without constructing an <see cref="AnswerPromptCommand"/>.
    /// </summary>
    public EngineRunResult HumanRespondDying(
        bool usePeach,
        int? requestedPeachCardId = null,
        bool advanceToHumanBoundary = true,
        bool useAlcohol = false,
        int? requestedAlcoholCardId = null) =>
        ExecuteExclusive(() => HumanDyingResponseCore(
            usePeach,
            requestedPeachCardId,
            useAlcohol,
            requestedAlcoholCardId,
            advanceToHumanBoundary));

    /// <summary>
    /// Compatibility adapter for answering a Slash-response prompt without
    /// constructing an <see cref="AnswerPromptCommand"/>.
    /// </summary>
    public EngineRunResult HumanRespondSlash(
        bool useSlash,
        int? requestedSlashCardId = null,
        bool advanceToHumanBoundary = true) =>
        ExecuteExclusive(() => HumanSlashResponseCore(
            useSlash,
            requestedSlashCardId,
            requestedResponseCardKind: null,
            advanceToHumanBoundary));

    /// <summary>
    /// Compatibility adapter for answering the private choice in a public
    /// FiveGrains draft without constructing an <see cref="AnswerPromptCommand"/>.
    /// </summary>
    public EngineRunResult HumanSelectHarvestCard(
        int cardId,
        bool advanceToHumanBoundary = true) =>
        ExecuteExclusive(() => HumanHarvestCardCore(cardId, advanceToHumanBoundary));

    /// <summary>
    /// Compatibility adapter for answering either private FireAttack card
    /// selection without constructing an <see cref="AnswerPromptCommand"/>.
    /// The prompt still determines whether the card is revealed or discarded.
    /// </summary>
    public EngineRunResult HumanSelectFireAttackCard(
        int cardId,
        bool advanceToHumanBoundary = true) =>
        ExecuteExclusive(() => HumanFireAttackCardCore(cardId, advanceToHumanBoundary));

    private EngineRunResult HumanRespondCore(
        bool useDodge,
        int? requestedDodgeCardId,
        CardKind? requestedResponseCardKind,
        bool advanceToHumanBoundary)
    {
        if (_pendingDecision?.Kind == DecisionKind.Feedback)
        {
            return HumanFeedbackCore(
                useFeedback: false,
                advanceToHumanBoundary: advanceToHumanBoundary);
        }

        if (_pendingDecision?.Kind == DecisionKind.Jieming)
        {
            return HumanJiemingCore(
                selectedTargetSeat: null,
                advanceToHumanBoundary: advanceToHumanBoundary);
        }

        if (_pendingDecision?.Kind == DecisionKind.Yuanhu)
        {
            return HumanYuanhuCore(
                selectedCardId: null,
                advanceToHumanBoundary: advanceToHumanBoundary);
        }

        if (_pendingGroupCard is { Effect: GroupCardEffect.ResponseAttack })
        {
            return HumanGroupResponseCore(
                useResponse: useDodge,
                requestedResponseCardId: requestedDodgeCardId,
                requestedResponseCardKind: requestedResponseCardKind,
                advanceToHumanBoundary: advanceToHumanBoundary);
        }

        RequireHumanDecision(DecisionKind.RespondDodge);
        var attack = _pendingAttack ??
            throw new InvalidOperationException("There is no Slash awaiting resolution.");
        var defender = _players[attack.TargetSeat];
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
        else
        {
            if (!ApplyAttackDamage(attack))
            {
                CompleteAttack(attack);
            }
        }

        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private EngineRunResult HumanDyingResponseCore(
        bool usePeach,
        int? requestedPeachCardId,
        bool useAlcohol,
        int? requestedAlcoholCardId,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.RescueDying);
        var pending = _pendingDying ??
            throw new InvalidOperationException("There is no dying response awaiting resolution.");
        var responder = _players[pending.ResponderSeat];
        if (responder.Seat != _options.HumanSeat)
        {
            throw new InvalidOperationException("The current dying responder is not the human seat.");
        }

        if (usePeach && useAlcohol)
        {
            throw new InvalidOperationException("A dying response can use either Peach or Alcohol, not both.");
        }

        if (usePeach)
        {
            var peach = GetHand(responder).FirstOrDefault(card =>
                card.Kind == CardKind.Peach &&
                (!requestedPeachCardId.HasValue || card.Id == requestedPeachCardId.Value));
            if (peach is null)
            {
                throw new InvalidOperationException("The responding player has no requested Peach card.");
            }
        }

        if (useAlcohol)
        {
            if (responder.Seat != pending.VictimSeat)
            {
                throw new InvalidOperationException("Alcohol can only rescue its dying holder.");
            }
            var alcohol = GetHand(responder).FirstOrDefault(card =>
                card.Kind == CardKind.Alcohol &&
                (!requestedAlcoholCardId.HasValue || card.Id == requestedAlcoholCardId.Value));
            if (alcohol is null)
            {
                throw new InvalidOperationException("The responding player has no requested Alcohol card.");
            }
        }

        ClearPendingDecision();
        ApplyDyingResponse(
            responder,
            usePeach,
            requestedPeachCardId,
            useAlcohol,
            requestedAlcoholCardId);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private EngineRunResult HumanHarvestCardCore(
        int cardId,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.SelectHarvestCard);
        var group = _pendingGroupCard ??
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
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private EngineRunResult HumanFireAttackCardCore(
        int cardId,
        bool advanceToHumanBoundary)
    {
        var pending = _pendingFireAttack ??
            throw new InvalidOperationException("There is no FireAttack selection awaiting a choice.");
        var decisionKind = pending.RevealedCardId is null
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

        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private EngineRunResult HumanFireAttackSkipCore(bool advanceToHumanBoundary)
    {
        var pending = _pendingFireAttack ??
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
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private EngineRunResult HumanSlashResponseCore(
        bool useSlash,
        int? requestedSlashCardId,
        CardKind? requestedResponseCardKind,
        bool advanceToHumanBoundary) =>
        _pendingDecision?.Kind == DecisionKind.Feedback
            ? HumanFeedbackCore(
                useFeedback: false,
                advanceToHumanBoundary: advanceToHumanBoundary)
            : _pendingDecision?.Kind == DecisionKind.Yuanhu
            ? HumanYuanhuCore(
                selectedCardId: null,
                advanceToHumanBoundary: advanceToHumanBoundary)
            : _pendingGroupCard is { Effect: GroupCardEffect.ResponseAttack }
            ? HumanGroupResponseCore(
                useResponse: useSlash,
                requestedResponseCardId: requestedSlashCardId,
                requestedResponseCardKind: requestedResponseCardKind,
                advanceToHumanBoundary: advanceToHumanBoundary)
            : HumanDuelResponseCore(
                useSlash,
                requestedSlashCardId,
                requestedResponseCardKind,
                advanceToHumanBoundary);

    private EngineRunResult HumanFeedbackCore(
        bool useFeedback,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.Feedback);
        var pending = _pendingDamageSkill ??
            throw new InvalidOperationException("There is no Feedback trigger awaiting a response.");
        if (pending.OwnerSeat != _options.HumanSeat)
        {
            throw new InvalidOperationException("The current Feedback owner is not the human seat.");
        }

        ClearPendingDecision();
        ResolveDamageSkillChoice(pending, useFeedback);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private EngineRunResult HumanYijiCore(
        int? selectedCardId,
        int? selectedTargetSeat,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.Yiji);
        var pending = _pendingDamageSkill ??
            throw new InvalidOperationException("There is no Yiji trigger awaiting a response.");
        if (pending.OwnerSeat != _options.HumanSeat ||
            pending.Effect != DamageSkillEffectKind.GiftDrawnCard)
        {
            throw new InvalidOperationException("The current Yiji owner is not the human seat.");
        }

        if ((selectedCardId is null) != (selectedTargetSeat is null))
        {
            throw new InvalidOperationException("Yiji must select both a card and a target, or skip.");
        }

        if (selectedCardId is { } cardId &&
            (!pending.EffectCardIds.Contains(cardId) ||
             selectedTargetSeat is not { } targetSeat ||
             targetSeat == pending.OwnerSeat ||
             !_pendingDecision!.ValidTargetSeats.Contains(targetSeat) ||
             !IsValidPlayerSeat(targetSeat) ||
             !_players[targetSeat].IsAlive))
        {
            throw new InvalidOperationException("The selected Yiji card or target is not legal.");
        }

        ClearPendingDecision();
        ResolveDamageSkillChoice(
            pending,
            useSkill: selectedCardId.HasValue,
            selectedCardId,
            selectedTargetSeat);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    /// <summary>
    /// Compatibility adapter for answering the private Jieming target prompt
    /// without constructing an <see cref="AnswerPromptCommand"/>.
    /// </summary>
    public EngineRunResult HumanRespondJieming(
        int? targetSeat,
        bool advanceToHumanBoundary = true) =>
        ExecuteExclusive(() => HumanJiemingCore(targetSeat, advanceToHumanBoundary));

    private EngineRunResult HumanJiemingCore(
        int? selectedTargetSeat,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.Jieming);
        var pending = _pendingDamageSkill ??
            throw new InvalidOperationException("There is no Jieming trigger awaiting a response.");
        if (pending.OwnerSeat != _options.HumanSeat ||
            pending.Effect != DamageSkillEffectKind.DrawToMaxHand)
        {
            throw new InvalidOperationException("The current Jieming owner is not the human seat.");
        }

        if (selectedTargetSeat is { } targetSeat &&
            (!_pendingDecision!.ValidTargetSeats.Contains(targetSeat) ||
             !IsValidPlayerSeat(targetSeat)))
        {
            throw new InvalidOperationException("The selected Jieming target is not legal.");
        }

        ClearPendingDecision();
        ResolveDamageSkillChoice(
            pending,
            useSkill: selectedTargetSeat.HasValue,
            selectedTargetSeat: selectedTargetSeat);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    /// <summary>
    /// Compatibility adapter for paying the private cross-seat Yuanhu trigger
    /// without constructing an <see cref="AnswerPromptCommand"/>.
    /// </summary>
    public EngineRunResult HumanRespondYuanhu(
        int? discardCardId,
        bool advanceToHumanBoundary = true) =>
        ExecuteExclusive(() => HumanYuanhuCore(discardCardId, advanceToHumanBoundary));

    private EngineRunResult HumanYuanhuCore(
        int? selectedCardId,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.Yuanhu);
        var pending = _pendingDamageSkill ??
            throw new InvalidOperationException("There is no Yuanhu trigger awaiting a response.");
        if (pending.OwnerSeat != _options.HumanSeat ||
            pending.Effect != DamageSkillEffectKind.RecoverDamageTarget)
        {
            throw new InvalidOperationException("The current Yuanhu owner is not the human seat.");
        }

        if (selectedCardId is { } cardId &&
            !_pendingDecision!.ValidCardIds.Contains(cardId))
        {
            throw new InvalidOperationException("The selected Yuanhu discard card is not a legal choice.");
        }

        ClearPendingDecision();
        ResolveDamageSkillChoice(
            pending,
            useSkill: selectedCardId.HasValue,
            selectedCardId,
            selectedTargetSeat: selectedCardId.HasValue ? pending.Attack.TargetSeat : null);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private EngineRunResult HumanDuelResponseCore(
        bool useSlash,
        int? requestedSlashCardId,
        CardKind? requestedResponseCardKind,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.RespondSlash);
        var duel = _pendingDuel ??
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
        SetCardUseStep(duel.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        ClearPendingDecision();
        ResolveDuelResponse(duel, responder, selectedSlash);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private EngineRunResult HumanGroupResponseCore(
        bool useResponse,
        int? requestedResponseCardId,
        CardKind? requestedResponseCardKind,
        bool advanceToHumanBoundary)
    {
        var group = _pendingGroupCard ??
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
        ResolveGroupResponse(group, responder, selectedResponse);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    /// <summary>
    /// Creates a viewer-safe snapshot. Pass revealAll only for diagnostics/tests or
    /// an explicit post-game reveal screen.
    /// </summary>
    public GameSnapshot CreateSnapshot(int viewerSeat, bool revealAll = false)
    {
        var snapshots = _players.Select(player =>
        {
            var playerHand = GetHand(player);
            var canSeeRole = revealAll ||
                             player.Role == Role.Lord ||
                             player.RoleRevealed ||
                             player.Seat == viewerSeat;
            var canSeeHand = revealAll || player.Seat == viewerSeat;
            var hand = canSeeHand
                ? playerHand.Select(ToSnapshot).ToArray()
                : [];
            var equipment = GetEquipment(player)
                .Select(ToSnapshot)
                .ToArray();
            var canSeeGeneral = player.GeneralSelected &&
                                (revealAll || player.GeneralRevealed || player.Seat == viewerSeat);
            var general = canSeeGeneral ? player.General : CreateHiddenGeneral();

            return new PlayerSnapshot(
                player.Seat,
                player.Name,
                player.IsHuman,
                canSeeRole ? player.Role : null,
                player.RoleRevealed || player.Role == Role.Lord,
                general.Id,
                general.Name,
                general.PortraitKey,
                general.Skill,
                general.SkillName,
                general.SkillDescription,
                player.Hp,
                player.MaxHp,
                player.IsAlive,
                playerHand.Count,
                hand,
                player.GeneralSelected && player.GeneralRevealed,
                player.HasAlcoholEffect)
            {
                Equipment = Array.AsReadOnly(equipment)
            };
        }).ToArray();

        var visibleDecision = _pendingDecision?.PlayerSeat == viewerSeat
            ? CloneDecision(_pendingDecision)
            : null;
        var publicRevealedCards = _pendingGroupCard is { Effect: GroupCardEffect.PublicDraft } publicDraft
            ? publicDraft.RevealedCardIds
                .Select(cardId => _cardZones.CardsAt(CardLocation.Processing)
                    .Single(card => card.Id == cardId))
                .Select(ToSnapshot)
                .ToArray()
            : _pendingFireAttack?.RevealedCardId is { } fireAttackRevealedCardId
                ? _cardZones.CardsAt(CardLocation.Processing)
                    .Where(card => card.Id == fireAttackRevealedCardId)
                    .Select(ToSnapshot)
                    .ToArray()
                : Array.Empty<CardSnapshot>();

        return new GameSnapshot(
            revealAll ? _options.Seed : null,
            _options.HumanSeat,
            _status,
            _winner,
            _turnNumber,
            _currentSeat,
            _phase,
            _cardZones.Count(CardLocation.DrawPile),
            _cardZones.Count(CardLocation.DiscardPile),
            snapshots,
            visibleDecision,
            _cardZones.Count(CardLocation.Processing),
            _revision)
        {
            PublicRevealedCards = Array.AsReadOnly(publicRevealedCards)
        };
    }

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
        if (options.PlayerCount is not (5 or 8))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.PlayerCount),
                "The identity adapter currently supports 5 or 8 players.");
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
        ContentRegistry? contentRegistry,
        GameOptions options)
    {
        var modeId = options.ModeId ?? $"identity:standard-{options.PlayerCount}";
        if (contentRegistry is not null)
        {
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

        var roleCounts = options.PlayerCount switch
        {
            8 => new Dictionary<string, int>
            {
                [nameof(Role.Lord)] = 1,
                [nameof(Role.Loyalist)] = 2,
                [nameof(Role.Rebel)] = 4,
                [nameof(Role.Renegade)] = 1
            },
            5 => new Dictionary<string, int>
            {
                [nameof(Role.Lord)] = 1,
                [nameof(Role.Loyalist)] = 1,
                [nameof(Role.Rebel)] = 2,
                [nameof(Role.Renegade)] = 1
            },
            _ => throw new ArgumentOutOfRangeException(nameof(options.PlayerCount))
        };
        return new ContentModeDefinition(
            modeId,
            $"身份局（{options.PlayerCount}人兼容模式）",
            options.PlayerCount,
            options.PlayerCount,
            roleCounts);
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

    private static IReadOnlyList<GeneralDefinition> CreateRuntimeGeneralPool(
        ContentRegistry? contentRegistry,
        ContentModeDefinition mode)
    {
        if (contentRegistry is null)
        {
            return GeneralCatalog.DemoGenerals;
        }

        var ids = mode.GeneralPoolIds ?? contentRegistry.Generals.Keys
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        return ids.Select(id =>
            {
                var definition = contentRegistry.Generals.TryGetValue(id, out var general)
                    ? general
                    : throw new InvalidOperationException(
                        $"Mode '{mode.Id}' references unknown general '{id}'.");
                var skill = contentRegistry.GetSkill(definition.SkillId);
                return new GeneralDefinition(
                    definition.Id,
                    definition.Name,
                    definition.PortraitKey,
                    skill.LegacyKind ?? SkillKind.None,
                    skill.Name,
                    skill.Description);
            })
            .ToArray();
    }

    private static ContentDeckRecipe? ResolveDeckDefinition(
        ContentRegistry? contentRegistry,
        string? deckId)
    {
        if (contentRegistry is null)
        {
            if (deckId is not null && deckId is not ("basic-demo" or "standard:basic-demo"))
            {
                throw new InvalidOperationException(
                    $"The legacy Core content path does not contain deck '{deckId}'.");
            }

            return null;
        }

        var requestedId = deckId ?? "standard:basic-demo";
        if (!contentRegistry.Decks.TryGetValue(requestedId, out var definition))
        {
            throw new InvalidOperationException(
                $"The supplied content registry does not contain deck '{requestedId}'.");
        }

        if (definition.InitialHandSize < 0 || definition.DrawPerTurn < 0 || definition.Cards.Count == 0)
        {
            throw new InvalidOperationException("The supplied deck recipe is invalid.");
        }

        return definition;
    }

    private void SetupPlayers()
    {
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
            var maxHp = role == Role.Lord ? 5 : 4;
            _players.Add(new PlayerRuntime
            {
                Seat = seat,
                Name = seat == _options.HumanSeat ? "你" : $"AI {seat + 1}",
                IsHuman = seat == _options.HumanSeat,
                Role = role,
                RoleRevealed = role == Role.Lord,
                General = _options.UseInteractiveSetup ? hiddenGeneral : generals[seat],
                GeneralSelected = !_options.UseInteractiveSetup,
                GeneralRevealed = !_options.UseInteractiveSetup,
                MaxHp = maxHp,
                Hp = maxHp
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

    private static IReadOnlyList<Card> CreateDeckFromRegistry(
        ContentRegistry contentRegistry,
        ContentDeckRecipe definition)
    {
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
        var cards = _contentRegistry is null
            ? StandardDeckCatalog.CreateBasicDemoDeck()
            : CreateDeckFromRegistry(
                _contentRegistry,
                ResolveDeckDefinition(_contentRegistry, _options.DeckId ?? _modeDefinition.DeckId)!);
        _cardZones.LoadInitialDeck(cards);
        _initialCardCount = _cardZones.TotalCards;
        if (!_options.UseInteractiveSetup)
        {
            _cardZones.Shuffle(CardLocation.DrawPile, _random);
        }
        AssertCoreInvariants();
    }

    private void DealInitialHands()
    {
        for (var cardIndex = 0; cardIndex < _initialHandSize; cardIndex++)
        {
            foreach (var player in _players)
            {
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

        _turnNumber++;
        _slashCountThisTurn = 0;
        _phase = TurnPhase.Draw;
        AddLog("TurnStarted", $"第 {_turnNumber} 回合：{current.Name}（{current.General.Name}）行动。", current.Seat);
        QueueGameEvent(new TurnStartedEvent(_turnNumber, current.Seat));

        var drawCount = _drawPerTurn + GetEquipment(current)
            .Select(card => EquipmentCatalog.Get(card.Kind).DrawCountBonus)
            .Sum();
        var context = CreateSkillContext(current);
        drawCount = SkillRegistry.Get(current.General.Skill).ModifyDrawCount(context, drawCount);
        DrawCards(current, drawCount, log: true);

        _phase = TurnPhase.Play;
        AddLog("PhaseChanged", $"{current.Name} 进入出牌阶段。", current.Seat);
        QueueGameEvent(new PhaseChangedEvent(_phase, current.Seat));
        PublishState();
    }

    private void RunOneSetupStep()
    {
        if (_selectionIndex >= _selectionOrder.Count)
        {
            CompleteSetup();
            return;
        }

        var seat = _selectionOrder[_selectionIndex];
        var player = _players[seat];
        var candidates = GetAvailableGeneralCandidates();
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                $"Mode '{_modeDefinition.Id}' ran out of generals during setup.");
        }

        QueueGameEvent(new GeneralSelectionRequestedEvent(
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
            player.Role,
            candidates,
            ++_thoughtSequence);
        AddGeneralThought(thought);
        ApplyGeneralSelection(player, general);
        PublishState();
    }

    private IReadOnlyList<GeneralDefinition> GetAvailableGeneralCandidates() =>
        _availableGenerals
            .Take(Math.Min(_modeDefinition.GeneralCandidateCount, _availableGenerals.Count))
            .ToArray();

    private void RequestHumanGeneralSelection(
        PlayerRuntime player,
        IReadOnlyList<GeneralDefinition> candidates)
    {
        var choices = candidates.Select(general =>
        {
            var choice = new PromptChoice(
                new ChoiceId($"setup.general.{general.Id}"),
                $"选择 {general.Name}（{general.SkillName}）",
                [],
                [],
                new Dictionary<string, string>
                {
                    ["action"] = "select-general",
                    ["general-id"] = general.Id
                });
            choice = choice with { ContentIds = [general.Id] };
            return choice;
        }).ToArray();

        _pendingDecision = new PendingDecision(
            DecisionKind.SelectGeneral,
            player.Seat,
            "请选择你的武将。候选仅对你可见，所有人完成后才会公开结果。",
            [],
            [])
        {
            PromptId = CreatePromptId(),
            Choices = choices,
            ValidContentIds = candidates.Select(general => general.Id).ToArray()
        };
        _status = EngineStatus.AwaitingHumanGeneralSelection;
        PublishState();
    }

    private void ApplyGeneralSelection(PlayerRuntime player, GeneralDefinition general)
    {
        if (!_availableGenerals.Remove(general))
        {
            throw new InvalidOperationException(
                $"General '{general.Id}' is not available for seat {player.Seat}.");
        }

        player.General = general;
        player.GeneralSelected = true;
        player.GeneralRevealed = false;
        AddLog("GeneralSelected", $"座位 {player.Seat + 1} 完成私有选将。", player.Seat);
        QueueGameEvent(new GeneralSelectedEvent(player.Seat, general.Id));
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
            _players.Any(player => !player.GeneralSelected))
        {
            throw new InvalidOperationException("Setup cannot complete before every seat selects a general.");
        }

        _cardZones.Shuffle(CardLocation.DrawPile, _random);
        DealInitialHands();
        foreach (var player in _players)
        {
            player.GeneralRevealed = true;
        }

        _setupComplete = true;
        _status = EngineStatus.Running;
        AddLog(
            "SetupCompleted",
            $"选将完成并公开：{string.Join("、", _players.OrderBy(player => player.Seat).Select(player => player.General.Name))}。",
            _currentSeat);
        QueueGameEvent(new GameStartedEvent(_playerCount, _modeDefinition.Id));
        QueueGameEvent(new SetupCompletedEvent(_players.Count, _modeDefinition.Id));
        PublishState();
    }

    private void RunOneEngineStep()
    {
        if (_pendingDying is not null)
        {
            RunOneDyingStep();
            return;
        }

        if (IsAiFireAttackPending())
        {
            ResolvePendingAiFireAttack();
            return;
        }

        if (IsAiDamageSkillPending())
        {
            ResolvePendingAiDamageSkill();
            return;
        }

        if (_pendingDamageTrigger is not null)
        {
            RunOneDamageTriggerStep();
            return;
        }

        if (IsAiResponsePending())
        {
            ResolvePendingAiResponse();
            return;
        }

        if (IsAiHarvestPending())
        {
            ResolvePendingAiHarvest();
            return;
        }

        if (_pendingGroupCard is { Effect: GroupCardEffect.Recovery })
        {
            RunOneGroupRecoveryStep();
            return;
        }

        if (!_setupComplete)
        {
            RunOneSetupStep();
            return;
        }

        if (_phase is TurnPhase.NotStarted or TurnPhase.Finished)
        {
            BeginTurn();
            return;
        }

        if (_phase == TurnPhase.Play)
        {
            var current = _players[_currentSeat];
            if (!current.IsAlive)
            {
                EndTurn();
                return;
            }

            if (current.IsHuman)
            {
                RequestHumanPlay();
                return;
            }

            RunOneAiPlayDecision(current);
            return;
        }

        if (_phase == TurnPhase.Discard)
        {
            AutoDiscard(_players[_currentSeat]);
            EndTurn();
            return;
        }

        throw new InvalidOperationException($"Unknown or unsupported turn phase: {_phase}.");
    }

    private void RunOneAiPlayDecision(PlayerRuntime player)
    {
        var legalActions = BuildLegalActions(player);
        var view = CreateSnapshot(player.Seat);
        var (action, thought) = _aiBrains[player.Seat].ChoosePlay(view, legalActions, ++_thoughtSequence);
        AddThought(thought);

        if (action.Kind == LegalActionKind.EndPlay)
        {
            BeginDiscardPhase();
            PublishState();
            return;
        }

        ExecutePlay(player, action);
        PublishState();
    }

    private void ExecutePlay(PlayerRuntime actor, LegalAction action)
    {
        var card = GetHand(actor).SingleOrDefault(candidate => candidate.Id == action.CardId) ??
            throw new InvalidOperationException("The chosen card is no longer in the actor's hand.");

        switch (action.Kind)
        {
            case LegalActionKind.Slash:
                if (action.TargetSeat is null)
                {
                    throw new InvalidOperationException("Slash requires a target.");
                }

                ResolveSlash(
                    actor,
                    _players[action.TargetSeat.Value],
                    card,
                    action.PlayedCardKind ?? card.Kind);
                break;
            case LegalActionKind.Peach:
                ResolvePeach(actor, card);
                break;
            case LegalActionKind.Duel:
                if (action.TargetSeat is null)
                {
                    throw new InvalidOperationException("Duel requires a target.");
                }

                ResolveDuel(actor, _players[action.TargetSeat.Value], card);
                break;
            case LegalActionKind.DrawTwo:
                ResolveDrawTwo(actor, card);
                break;
            case LegalActionKind.Alcohol:
                ResolveAlcohol(actor, card);
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
            case LegalActionKind.Dismantlement:
                if (action.TargetSeat is null)
                {
                    throw new InvalidOperationException("Dismantlement requires a target.");
                }

                ResolveDismantlement(actor, _players[action.TargetSeat.Value], card);
                break;
            case LegalActionKind.Snatch:
                if (action.TargetSeat is null)
                {
                    throw new InvalidOperationException("Snatch requires a target.");
                }

                ResolveSnatch(actor, _players[action.TargetSeat.Value], card);
                break;
            case LegalActionKind.FireAttack:
                if (action.TargetSeat is null)
                {
                    throw new InvalidOperationException("FireAttack requires a target.");
                }

                ResolveFireAttack(actor, _players[action.TargetSeat.Value], card);
                break;
            default:
                throw new InvalidOperationException($"Unsupported action {action.Kind}.");
        }
    }

    private void ResolveEquip(PlayerRuntime source, Card equipment)
    {
        var definition = EquipmentCatalog.Get(equipment.Kind);
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
            CardLocation.Hand(source.Seat),
            CardLocation.Processing,
            CardMoveReasons.EquipmentUse);
        SetCardUseStep(resolutionId, ResolutionFrameStep.ResolvingEffect);

        var replaced = GetEquipment(source)
            .SingleOrDefault(card => EquipmentCatalog.Get(card.Kind).Slot == definition.Slot);
        if (replaced is not null)
        {
            MoveCard(
                replaced,
                CardLocation.Equipment(source.Seat),
                CardLocation.DiscardPile,
                CardMoveReasons.EquipmentReplace);
        }

        MoveCard(
            equipment,
            CardLocation.Processing,
            CardLocation.Equipment(source.Seat),
            CardMoveReasons.EquipmentEnter);
        QueueGameEvent(new EquipmentChangedEvent(
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
        FinishCardUse(resolutionId, equipment);
    }

    private void ResolveDrawTwo(PlayerRuntime source, Card drawTwo)
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
            CardLocation.Hand(source.Seat),
            CardLocation.Processing,
            CardMoveReasons.Use);
        SetCardUseStep(resolutionId, ResolutionFrameStep.ResolvingEffect);
        DrawCards(source, 2, log: true);
        MoveCard(
            drawTwo,
            CardLocation.Processing,
            CardLocation.DiscardPile,
            CardMoveReasons.UseFinished);
        FinishCardUse(resolutionId, drawTwo);
        AddLog("CardEffect", $"{source.Name} 使用【无中生有】，摸两张牌。", source.Seat);
    }

    private void ResolveAlcohol(PlayerRuntime source, Card alcohol)
    {
        var stillLegal = BuildLegalActions(source).Any(action =>
            action.Kind == LegalActionKind.Alcohol &&
            action.CardId == alcohol.Id &&
            action.TargetSeat is null);
        if (!stillLegal)
        {
            throw new InvalidOperationException("Alcohol became illegal before resolution.");
        }

        var resolutionId = BeginCardUse(alcohol, source.Seat, []);
        MoveCard(
            alcohol,
            CardLocation.Hand(source.Seat),
            CardLocation.Processing,
            CardMoveReasons.Use);
        SetCardUseStep(resolutionId, ResolutionFrameStep.ResolvingEffect);
        source.HasAlcoholEffect = true;
        QueueGameEvent(new AlcoholAppliedEvent(resolutionId, source.Seat, DamageBonus: 1));
        AddLog("CardEffect", $"{source.Name} 使用【酒】，本回合下一张直接杀造成的伤害 +1。", source.Seat);
        MoveCard(
            alcohol,
            CardLocation.Processing,
            CardLocation.DiscardPile,
            CardMoveReasons.UseFinished);
        FinishCardUse(resolutionId, alcohol);
    }

    private void ResolveGroupCard(
        PlayerRuntime source,
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
            .Where(player => player.IsAlive)
            .Select(player => player.Seat)
            .ToArray();
        var resolutionId = BeginCardUse(groupCard, source.Seat, targets);
        MoveCard(
            groupCard,
            CardLocation.Hand(source.Seat),
            CardLocation.Processing,
            CardMoveReasons.Use);
        var group = new GroupCardResolution(
            resolutionId,
            source.Seat,
            groupCard,
            targets,
            GroupCardEffect.ResponseAttack,
            requiredCardKind);
        _pendingGroupCard = group;
        AddLog(
            "CardUsed",
            $"{source.Name} 使用【{CardCatalog.Get(groupCard.Kind).DisplayName}】，依次攻击 {targets.Length} 名角色。",
            source.Seat);
        QueueGameEvent(new GroupCardUsedEvent(
            resolutionId,
            groupCard.Id,
            groupCard.Kind,
            source.Seat,
            targets));
        NotifyAiOfGroupAttack(source, targets);
        BeginGroupAttackResponse(group);
    }

    private void ResolvePeachGarden(PlayerRuntime source, Card peachGarden)
    {
        var stillLegal = BuildLegalActions(source).Any(action =>
            action.Kind == LegalActionKind.PeachGarden &&
            action.CardId == peachGarden.Id &&
            action.TargetSeat is null);
        if (!stillLegal)
        {
            throw new InvalidOperationException("PeachGarden became illegal before resolution.");
        }

        var targets = Enumerable.Range(0, _playerCount)
            .Select(offset => _players[(source.Seat + offset) % _playerCount])
            .Where(player => player.IsAlive)
            .Select(player => player.Seat)
            .ToArray();
        var resolutionId = BeginCardUse(peachGarden, source.Seat, targets);
        MoveCard(
            peachGarden,
            CardLocation.Hand(source.Seat),
            CardLocation.Processing,
            CardMoveReasons.Use);
        SetCardUseStep(resolutionId, ResolutionFrameStep.ResolvingEffect);
        var group = new GroupCardResolution(
            resolutionId,
            source.Seat,
            peachGarden,
            targets,
            GroupCardEffect.Recovery,
            requiredCardKind: null);
        _pendingGroupCard = group;
        AddLog(
            "CardUsed",
            $"{source.Name} 使用【{CardCatalog.Get(peachGarden.Kind).DisplayName}】，使 {targets.Length} 名存活角色依次回复体力。",
            source.Seat);
        QueueGameEvent(new GroupCardUsedEvent(
            resolutionId,
            peachGarden.Id,
            peachGarden.Kind,
            source.Seat,
            targets));
    }

    private void ResolveFiveGrains(PlayerRuntime source, Card fiveGrains)
    {
        var stillLegal = BuildLegalActions(source).Any(action =>
            action.Kind == LegalActionKind.FiveGrains &&
            action.CardId == fiveGrains.Id &&
            action.TargetSeat is null);
        if (!stillLegal)
        {
            throw new InvalidOperationException("FiveGrains became illegal before resolution.");
        }

        var aliveSeats = Enumerable.Range(0, _playerCount)
            .Select(offset => _players[(source.Seat + offset) % _playerCount])
            .Where(player => player.IsAlive)
            .Select(player => player.Seat)
            .ToArray();
        var availableCards = _cardZones.Count(CardLocation.DrawPile) +
                             _cardZones.Count(CardLocation.DiscardPile);
        var targets = aliveSeats.Take(availableCards).ToArray();
        var resolutionId = BeginCardUse(fiveGrains, source.Seat, targets);
        MoveCard(
            fiveGrains,
            CardLocation.Hand(source.Seat),
            CardLocation.Processing,
            CardMoveReasons.Use);
        SetCardUseStep(resolutionId, ResolutionFrameStep.ResolvingEffect);
        var group = new GroupCardResolution(
            resolutionId,
            source.Seat,
            fiveGrains,
            targets,
            GroupCardEffect.PublicDraft,
            requiredCardKind: null);
        _pendingGroupCard = group;

        foreach (var _ in targets)
        {
            var revealed = DrawOneToProcessing(CardMoveReasons.Reveal) ??
                throw new InvalidOperationException("FiveGrains could not reveal the promised card count.");
            group.RevealedCardIds.Add(revealed.Id);
        }

        AddLog(
            "CardUsed",
            $"{source.Name} 使用【{CardCatalog.Get(fiveGrains.Kind).DisplayName}】，公开展示 {group.RevealedCardIds.Count} 张牌并依次选取。",
            source.Seat);
        QueueGameEvent(new GroupCardUsedEvent(
            resolutionId,
            fiveGrains.Id,
            fiveGrains.Kind,
            source.Seat,
            targets));
        QueueGameEvent(new CardsRevealedEvent(
            resolutionId,
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

    private void ResolveDismantlement(PlayerRuntime source, PlayerRuntime target, Card dismantlement) =>
        ResolveTargetCardEffect(
            source,
            target,
            dismantlement,
            LegalActionKind.Dismantlement,
            TargetCardEffect.Discard);

    private void ResolveSnatch(PlayerRuntime source, PlayerRuntime target, Card snatch) =>
        ResolveTargetCardEffect(
            source,
            target,
            snatch,
            LegalActionKind.Snatch,
            TargetCardEffect.Take);

    private void ResolveTargetCardEffect(
        PlayerRuntime source,
        PlayerRuntime target,
        Card effectCard,
        LegalActionKind actionKind,
        TargetCardEffect effect)
    {
        var stillLegal = BuildLegalActions(source).Any(action =>
            action.Kind == actionKind &&
            action.CardId == effectCard.Id &&
            action.TargetSeat == target.Seat);
        if (!stillLegal)
        {
            throw new InvalidOperationException(
                $"{effectCard.Kind} became illegal before resolution.");
        }

        var targetHand = GetHand(target);
        if (targetHand.Count == 0)
        {
            throw new InvalidOperationException(
                $"{effectCard.Kind} cannot resolve against an empty hand.");
        }

        var resolutionId = BeginCardUse(effectCard, source.Seat, [target.Seat]);
        MoveCard(
            effectCard,
            CardLocation.Hand(source.Seat),
            CardLocation.Processing,
            CardMoveReasons.Use);
        SetCardUseStep(resolutionId, ResolutionFrameStep.ResolvingEffect);

        // The chosen hand card remains hidden from the source and ordinary
        // observers. The deterministic engine RNG makes the blind choice
        // replayable without adding a private card-id prompt to the UI.
        var targetCard = targetHand[_random.Next(targetHand.Count)];
        var targetMoveReason = effect == TargetCardEffect.Discard
            ? CardMoveReasons.Dismantlement
            : CardMoveReasons.Snatch;
        MoveCard(
            targetCard,
            CardLocation.Hand(target.Seat),
            CardLocation.Processing,
            targetMoveReason);

        if (effect == TargetCardEffect.Discard)
        {
            QueueGameEvent(new TargetCardDiscardedEvent(
                resolutionId,
                source.Seat,
                target.Seat,
                CardZoneKind.Hand));
            MoveCard(
                targetCard,
                CardLocation.Processing,
                CardLocation.DiscardPile,
                CardMoveReasons.DismantlementFinished);
        }
        else
        {
            QueueGameEvent(new TargetCardTakenEvent(
                resolutionId,
                source.Seat,
                target.Seat,
                CardZoneKind.Hand));
            MoveCard(
                targetCard,
                CardLocation.Processing,
                CardLocation.Hand(source.Seat),
                CardMoveReasons.SnatchFinished);
        }

        MoveCard(
            effectCard,
            CardLocation.Processing,
            CardLocation.DiscardPile,
            CardMoveReasons.UseFinished);
        FinishCardUse(resolutionId, effectCard);

        var displayName = CardCatalog.Get(effectCard.Kind).DisplayName;
        var outcome = effect == TargetCardEffect.Discard
            ? $"弃置 {target.Name} 的一张手牌"
            : $"获得 {target.Name} 的一张手牌";
        AddLog(
            "CardEffect",
            $"{source.Name} 使用【{displayName}】，{outcome}（牌面不公开）。",
            source.Seat,
            target.Seat);
    }

    private void ResolveFireAttack(
        PlayerRuntime source,
        PlayerRuntime target,
        Card fireAttack)
    {
        var stillLegal = BuildLegalActions(source).Any(action =>
            action.Kind == LegalActionKind.FireAttack &&
            action.CardId == fireAttack.Id &&
            action.TargetSeat == target.Seat);
        if (!stillLegal)
        {
            throw new InvalidOperationException("FireAttack became illegal before resolution.");
        }

        var resolutionId = BeginCardUse(fireAttack, source.Seat, [target.Seat]);
        MoveCard(
            fireAttack,
            CardLocation.Hand(source.Seat),
            CardLocation.Processing,
            CardMoveReasons.Use);
        SetCardUseStep(resolutionId, ResolutionFrameStep.AwaitingResponse);
        var pending = new FireAttackResolution(
            resolutionId,
            source.Seat,
            target.Seat,
            fireAttack);
        _pendingFireAttack = pending;
        AddLog(
            "CardUsed",
            $"{source.Name} 对 {target.Name} 使用【火攻】，等待目标展示一张手牌。",
            source.Seat,
            target.Seat);
        QueueGameEvent(new CardUsedEvent(
            fireAttack.Id,
            fireAttack.Kind,
            source.Seat,
            target.Seat));
        BeginFireAttackReveal(pending);
    }

    private void BeginFireAttackReveal(FireAttackResolution pending)
    {
        if (!ReferenceEquals(_pendingFireAttack, pending) || pending.RevealedCardId is not null)
        {
            throw new InvalidOperationException("The FireAttack reveal is not the current resolution.");
        }

        var target = _players[pending.TargetSeat];
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
            pending.Card.Kind)
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
        FireAttackResolution pending,
        int cardId)
    {
        if (!ReferenceEquals(_pendingFireAttack, pending) || pending.RevealedCardId is not null)
        {
            throw new InvalidOperationException("The FireAttack reveal is not the current resolution.");
        }

        var target = _players[pending.TargetSeat];
        var revealed = GetHand(target).SingleOrDefault(card => card.Id == cardId) ??
            throw new InvalidOperationException("The selected FireAttack reveal card is not in the target hand.");
        MoveCard(
            revealed,
            CardLocation.Hand(target.Seat),
            CardLocation.Processing,
            CardMoveReasons.FireAttackReveal);
        pending.RevealedCardId = revealed.Id;
        SetCardUseStep(pending.ResolutionId, ResolutionFrameStep.AwaitingResponse);
        QueueGameEvent(new FireAttackCardRevealedEvent(
            pending.ResolutionId,
            pending.SourceSeat,
            pending.TargetSeat,
            revealed.Id,
            revealed.Kind,
            revealed.Suit));
        AddLog(
            "CardRevealed",
            $"{target.Name} 展示了【{revealed.DisplayName}】（{revealed.Suit}），等待攻击者弃置同花色手牌。",
            target.Seat,
            pending.SourceSeat);
        BeginFireAttackDiscard(pending, revealed);
    }

    private void BeginFireAttackDiscard(
        FireAttackResolution pending,
        Card revealed)
    {
        if (!ReferenceEquals(_pendingFireAttack, pending) || pending.RevealedCardId != revealed.Id)
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
            pending.Card.Kind)
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
            TargetSeat = pending.TargetSeat
        };
        _status = source.IsHuman
            ? EngineStatus.AwaitingHumanCardSelection
            : EngineStatus.Running;
    }

    private void ResolveFireAttackDiscard(
        FireAttackResolution pending,
        int? selectedCardId)
    {
        if (!ReferenceEquals(_pendingFireAttack, pending) || pending.RevealedCardId is not { } revealedCardId)
        {
            throw new InvalidOperationException("The FireAttack discard is not the current resolution.");
        }

        var source = _players[pending.SourceSeat];
        var target = _players[pending.TargetSeat];
        var revealed = _cardZones.CardsAt(CardLocation.Processing)
            .Single(card => card.Id == revealedCardId);
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
            MoveCard(
                revealed,
                CardLocation.Processing,
                CardLocation.DiscardPile,
                CardMoveReasons.FireAttackFinished);
            QueueGameEvent(new FireAttackResolvedEvent(
                pending.ResolutionId,
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
            MoveCard(
                pending.Card,
                CardLocation.Processing,
                CardLocation.DiscardPile,
                CardMoveReasons.UseFinished);
            FinishCardUse(pending.ResolutionId, pending.Card);
            _pendingFireAttack = null;
            return;
        }

        MoveCard(
            matchingDiscard,
            CardLocation.Hand(source.Seat),
            CardLocation.Processing,
            CardMoveReasons.FireAttackDiscard);
        MoveCard(
            revealed,
            CardLocation.Processing,
            CardLocation.DiscardPile,
            CardMoveReasons.FireAttackFinished);
        MoveCard(
            matchingDiscard,
            CardLocation.Processing,
            CardLocation.DiscardPile,
            CardMoveReasons.FireAttackDiscardFinished);
        QueueGameEvent(new FireAttackResolvedEvent(
            pending.ResolutionId,
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

        _pendingFireAttack = null;
        var attack = new AttackResolution(
            pending.ResolutionId,
            source.Seat,
            target.Seat,
            pending.Card,
            damageAmount: 1,
            playedCardKind: CardKind.FireAttack);
        _pendingAttack = attack;
        if (!ApplyAttackDamage(attack))
        {
            CompleteAttack(attack);
        }
    }

    private void BeginHarvestSelection(GroupCardResolution group)
    {
        if (!ReferenceEquals(_pendingGroupCard, group) || group.Effect != GroupCardEffect.PublicDraft)
        {
            throw new InvalidOperationException("The FiveGrains draft is not the current card resolution.");
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
        GroupCardResolution group,
        PlayerRuntime picker,
        int cardId)
    {
        if (!ReferenceEquals(_pendingGroupCard, group) || group.Effect != GroupCardEffect.PublicDraft)
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
        group.RevealedCardIds.Remove(cardId);
        AddLog(
            "CardSelected",
            $"{picker.Name} 从五谷丰登的公开牌中选择了【{card.DisplayName}】。",
            picker.Seat);
        QueueGameEvent(new HarvestCardSelectedEvent(group.ResolutionId, picker.Seat, cardId));

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

    private void FinishFiveGrains(GroupCardResolution group)
    {
        if (!ReferenceEquals(_pendingGroupCard, group) || group.Effect != GroupCardEffect.PublicDraft)
        {
            throw new InvalidOperationException("The FiveGrains draft is not the current card resolution.");
        }

        if (_pendingDying is not null || _pendingAttack is not null ||
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
        group.RevealedCardIds.Clear();

        var cardLocation = _cardZones.GetLocation(group.Card.Id);
        if (cardLocation == CardLocation.Processing)
        {
            MoveCard(
                group.Card,
                CardLocation.Processing,
                CardLocation.DiscardPile,
                CardMoveReasons.UseFinished);
        }
        else if (cardLocation != CardLocation.DiscardPile)
        {
            throw new InvalidOperationException(
                $"A resolved {group.Card.Kind} left Processing through an unsupported destination: {cardLocation}.");
        }

        SetCardUseTargetIndex(group.ResolutionId, group.TargetSeats.Count);
        FinishCardUse(group.ResolutionId, group.Card);
        _pendingGroupCard = null;
        _pendingAttack = null;
        _pendingDuel = null;
        _pendingDecision = null;

        if (_winner != Winner.None && _status != EngineStatus.Completed)
        {
            CompleteGame();
        }
    }

    private void RunOneGroupRecoveryStep()
    {
        var group = _pendingGroupCard ??
            throw new InvalidOperationException("A group recovery step has no pending card.");
        if (group.Effect != GroupCardEffect.Recovery)
        {
            throw new InvalidOperationException("The pending group card is not a recovery effect.");
        }

        if (group.TargetIndex >= group.TargetSeats.Count)
        {
            FinishGroupRecovery(group);
            PublishState();
            return;
        }

        var target = _players[group.TargetSeats[group.TargetIndex]];
        if (target.IsAlive && target.Hp < target.MaxHp)
        {
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
                QueueGameEvent(new RecoveryAppliedEvent(
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

        group.TargetIndex++;
        SetCardUseTargetIndex(group.ResolutionId, group.TargetIndex);
        if (group.TargetIndex >= group.TargetSeats.Count)
        {
            FinishGroupRecovery(group);
        }

        PublishState();
    }

    private void BeginGroupAttackResponse(GroupCardResolution group)
    {
        if (!ReferenceEquals(_pendingGroupCard, group) || group.Effect != GroupCardEffect.ResponseAttack)
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
        if (!target.IsAlive)
        {
            group.TargetIndex++;
            SetCardUseTargetIndex(group.ResolutionId, group.TargetIndex);
            BeginGroupAttackResponse(group);
            return;
        }

        var attack = new AttackResolution(group.ResolutionId, group.SourceSeat, target.Seat, group.Card);
        group.CurrentAttack = attack;
        _pendingAttack = attack;
        var requiredCardKind = group.RequiredCardKind ??
            throw new InvalidOperationException("A group response attack must declare a required card kind.");
        var responseCards = GetResponseCards(target, requiredCardKind);
        if (responseCards.Count == 0)
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
            responseCards.Select(card => card.Id).ToArray(),
            [],
            group.SourceSeat,
            group.Card.Kind)
        {
            PromptId = CreatePromptId(),
            Choices = CreateResponseChoices(
                responseCards,
                requiredCardKind,
                group.Card.Kind,
                card => GetEffectiveResponseKind(target, card, requiredCardKind)),
            RequiredCardKind = requiredCardKind
        };
        QueueGameEvent(new ResponseRequestedEvent(
            group.SourceSeat,
            target.Seat,
            group.Card.Kind,
            requiredCardKind));
        _status = target.IsHuman
            ? EngineStatus.AwaitingHumanResponse
            : EngineStatus.Running;
    }

    private void ResolveSlash(
        PlayerRuntime source,
        PlayerRuntime target,
        Card slash,
        CardKind playedCardKind)
    {
        var stillLegal = BuildLegalActions(source).Any(action =>
            action.Kind == LegalActionKind.Slash &&
            action.CardId == slash.Id &&
            action.TargetSeat == target.Seat &&
            (action.PlayedCardKind ?? slash.Kind) == playedCardKind);
        if (!stillLegal)
        {
            throw new InvalidOperationException("Slash became illegal before resolution.");
        }

        var resolutionId = BeginCardUse(slash, source.Seat, [target.Seat], playedCardKind);
        MoveCard(
            slash,
            CardLocation.Hand(source.Seat),
            CardLocation.Processing,
            CardMoveReasons.Use);
        _slashCountThisTurn++;
        var damageAmount = source.HasAlcoholEffect ? 2 : 1;
        source.HasAlcoholEffect = false;
        var attack = new AttackResolution(
            resolutionId,
            source.Seat,
            target.Seat,
            slash,
            damageAmount,
            playedCardKind);
        _pendingAttack = attack;
        var slashName = CardCatalog.Get(playedCardKind).DisplayName;
        var useDescription = playedCardKind == slash.Kind
            ? $"使用【{slashName}】"
            : $"将【{CardCatalog.Get(slash.Kind).DisplayName}】当作【{slashName}】使用";
        AddLog("CardUsed", $"{source.Name} 对 {target.Name}{useDescription}。", source.Seat, target.Seat);
        QueueGameEvent(new CardUsedEvent(slash.Id, playedCardKind, source.Seat, target.Seat));
        NotifyAiOfSlash(source, target);

        var dodges = GetResponseCards(target, CardKind.Dodge);
        var dodge = dodges.FirstOrDefault();
        if (dodges.Count > 0 && target.IsHuman)
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
                $"{source.Name} 对你使用了【{slashName}】，是否打出【闪】？",
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
                    card => GetEffectiveResponseKind(target, card, CardKind.Dodge)),
                RequiredCardKind = CardKind.Dodge
            };
            _status = EngineStatus.AwaitingHumanResponse;
            QueueGameEvent(new ResponseRequestedEvent(
                source.Seat,
                target.Seat,
                playedCardKind,
                CardKind.Dodge));
            PublishState();
            return;
        }

        if (dodge is not null)
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
                $"{source.Name} 对 {target.Name} 使用了【{slashName}】，AI 将选择是否打出【闪】。",
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
                    card => GetEffectiveResponseKind(target, card, CardKind.Dodge)),
                RequiredCardKind = CardKind.Dodge
            };
            QueueGameEvent(new ResponseRequestedEvent(
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

    private void ResolveDuel(PlayerRuntime source, PlayerRuntime target, Card duel)
    {
        var stillLegal = BuildLegalActions(source).Any(action =>
            action.Kind == LegalActionKind.Duel &&
            action.CardId == duel.Id &&
            action.TargetSeat == target.Seat);
        if (!stillLegal)
        {
            throw new InvalidOperationException("Duel became illegal before resolution.");
        }

        var resolutionId = BeginCardUse(duel, source.Seat, [target.Seat]);
        MoveCard(
            duel,
            CardLocation.Hand(source.Seat),
            CardLocation.Processing,
            CardMoveReasons.Use);
        var attack = new AttackResolution(resolutionId, source.Seat, target.Seat, duel);
        _pendingAttack = attack;
        var duelResolution = new DuelResolution(attack);
        _pendingDuel = duelResolution;
        AddLog("CardUsed", $"{source.Name} 对 {target.Name} 使用【决斗】。", source.Seat, target.Seat);
        QueueGameEvent(new CardUsedEvent(duel.Id, duel.Kind, source.Seat, target.Seat));
        NotifyAiOfDuel(source, target);
        BeginDuelResponse(duelResolution);
    }

    private void BeginDuelResponse(DuelResolution duel)
    {
        if (!ReferenceEquals(_pendingDuel, duel))
        {
            throw new InvalidOperationException("The Duel response is not the current card resolution.");
        }

        var responder = _players[duel.ResponderSeat];
        var opponent = _players[duel.OpponentSeat];
        var slashes = GetResponseCards(responder, CardKind.Slash);
        if (slashes.Count == 0)
        {
            if (_pendingAttack is null)
            {
                throw new InvalidOperationException("A Duel response has no active damage source.");
            }
            SetCardUseStep(duel.ResolutionId, ResolutionFrameStep.ResolvingEffect);
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
            $"{opponent.Name} 对你使用了【决斗】，是否打出【杀】？",
            slashes.Select(card => card.Id).ToArray(),
            [],
            duel.OpponentSeat,
            CardKind.Duel)
        {
            PromptId = CreatePromptId(),
            Choices = CreateResponseChoices(
                slashes,
                CardKind.Slash,
                effectiveCardKindSelector: card =>
                    GetEffectiveResponseKind(responder, card, CardKind.Slash)),
            RequiredCardKind = CardKind.Slash
        };
        QueueGameEvent(new ResponseRequestedEvent(
            duel.OpponentSeat,
            responder.Seat,
            CardKind.Duel,
            CardKind.Slash));
        _status = responder.IsHuman
            ? EngineStatus.AwaitingHumanResponse
            : EngineStatus.Running;
    }

    private void ResolveDuelResponse(
        DuelResolution duel,
        PlayerRuntime responder,
        Card? selectedSlash)
    {
        if (!ReferenceEquals(_pendingDuel, duel) || responder.Seat != duel.ResponderSeat)
        {
            throw new InvalidOperationException("The Duel response does not belong to the current responder.");
        }

        if (selectedSlash is { } slash)
        {
            var responseCardKind = GetEffectiveResponseKind(
                responder,
                slash,
                CardKind.Slash);
            MoveCard(
                slash,
                CardLocation.Hand(responder.Seat),
                CardLocation.Processing,
                CardMoveReasons.Respond);
            var responseName = CardCatalog.Get(responseCardKind).DisplayName;
            var responseDescription = IsNativeResponseCard(slash, CardKind.Slash)
                ? $"打出【{responseName}】"
                : $"将【{slash.DisplayName}】当作【{responseName}】";
            AddLog(
                "CardResponded",
                $"{responder.Name} {responseDescription}应战【决斗】。",
                responder.Seat,
                duel.OpponentSeat);
            QueueGameEvent(new CardRespondedEvent(
                slash.Id,
                responder.Seat,
                duel.OpponentSeat,
                responseCardKind));
            QueueGameEvent(new DuelResponseEvent(
                duel.ResolutionId,
                responder.Seat,
                UsedSlash: true,
                SlashCardId: slash.Id,
                ResponseCardKind: responseCardKind));
            MoveCard(
                slash,
                CardLocation.Processing,
                CardLocation.DiscardPile,
                CardMoveReasons.ResponseFinished);
            duel.ResponderSeat = duel.OpponentSeat;
            BeginDuelResponse(duel);
            return;
        }

        QueueGameEvent(new DuelResponseEvent(
            duel.ResolutionId,
            responder.Seat,
            UsedSlash: false,
            SlashCardId: null));
        var attack = _pendingAttack ??
            throw new InvalidOperationException("A failed Duel response has no active damage source.");
        if (!ApplyAttackDamage(attack))
        {
            CompleteAttack(attack);
        }
    }

    private void ResolveGroupResponse(
        GroupCardResolution group,
        PlayerRuntime responder,
        Card? selectedResponse)
    {
        if (!ReferenceEquals(_pendingGroupCard, group) ||
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

            MoveCard(
                responseCard,
                CardLocation.Hand(responder.Seat),
                CardLocation.Processing,
                CardMoveReasons.Respond);
            var incomingName = CardCatalog.Get(group.Card.Kind).DisplayName;
            var responseName = CardCatalog.Get(responseCardKind).DisplayName;
            var responseDescription = IsNativeResponseCard(responseCard, requiredCardKind)
                ? $"打出【{responseName}】"
                : $"将【{responseCard.DisplayName}】当作【{responseName}】";
            AddLog(
                "CardResponded",
                $"{responder.Name} {responseDescription}响应【{incomingName}】。",
                responder.Seat,
                group.SourceSeat);
            QueueGameEvent(new CardRespondedEvent(
                responseCard.Id,
                responder.Seat,
                group.SourceSeat,
                responseCardKind));
            QueueGameEvent(new GroupResponseEvent(
                group.ResolutionId,
                group.Card.Kind,
                requiredCardKind,
                responder.Seat,
                UsedResponse: true,
                ResponseCardId: responseCard.Id,
                ResponseCardKind: responseCardKind));
            MoveCard(
                responseCard,
                CardLocation.Processing,
                CardLocation.DiscardPile,
                CardMoveReasons.ResponseFinished);
            CompleteAttack(attack);
            return;
        }

        QueueGameEvent(new GroupResponseEvent(
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

    private void ResolvePendingAiResponse()
    {
        if (_pendingDuel is not null)
        {
            ResolvePendingAiDuel();
            return;
        }

        if (_pendingGroupCard is { Effect: GroupCardEffect.ResponseAttack })
        {
            ResolvePendingAiGroupAttack();
            return;
        }

        ResolvePendingAiDodge();
    }

    private void ResolvePendingAiDamageSkill()
    {
        var pending = _pendingDamageSkill ??
            throw new InvalidOperationException("AI damage-skill continuation is missing.");
        if (_pendingDecision is not { Kind: DecisionKind.Feedback or DecisionKind.Yiji or DecisionKind.Jieming or DecisionKind.Yuanhu } decision ||
            decision.PlayerSeat != pending.OwnerSeat)
        {
            throw new InvalidOperationException("The pending AI damage-skill prompt is inconsistent.");
        }

        var owner = _players[pending.OwnerSeat];
        if (owner.IsHuman)
        {
            throw new InvalidOperationException("A human damage-skill owner cannot be resolved as AI.");
        }

        var view = CreateSnapshot(owner.Seat);
        if (pending.Skill == SkillKind.Yuanhu)
        {
            var (cardId, thought) = _aiBrains[owner.Seat].ChooseYuanhuCard(
                view,
                decision.ValidCardIds,
                pending.Attack.TargetSeat,
                ++_thoughtSequence);
            AddThought(thought);
            ClearPendingDecision();
            ResolveDamageSkillChoice(
                pending,
                useSkill: cardId.HasValue,
                selectedCardId: cardId,
                selectedTargetSeat: cardId.HasValue ? pending.Attack.TargetSeat : null);
        }
        else if (pending.Skill == SkillKind.Jieming)
        {
            var (targetSeat, thought) = _aiBrains[owner.Seat].ChooseJiemingTarget(
                view,
                decision.ValidTargetSeats,
                ++_thoughtSequence);
            AddThought(thought);
            ClearPendingDecision();
            ResolveDamageSkillChoice(
                pending,
                useSkill: targetSeat.HasValue,
                selectedTargetSeat: targetSeat);
        }
        else if (pending.Skill == SkillKind.Yiji)
        {
            var (cardId, targetSeat, thought) = _aiBrains[owner.Seat].ChooseYijiGift(
                view,
                pending.EffectCardIds,
                decision.ValidTargetSeats,
                ++_thoughtSequence);
            AddThought(thought);
            ClearPendingDecision();
            ResolveDamageSkillChoice(
                pending,
                useSkill: cardId.HasValue,
                selectedCardId: cardId,
                selectedTargetSeat: targetSeat);
        }
        else
        {
            var (useFeedback, thought) = _aiBrains[owner.Seat].ChooseFeedback(
                view,
                pending.SourceSeat,
                pending.EffectiveCardKind,
                ++_thoughtSequence);
            AddThought(thought);
            ClearPendingDecision();
            ResolveDamageSkillChoice(pending, useFeedback);
        }

        PublishState();
    }

    private void ResolvePendingAiHarvest()
    {
        var group = _pendingGroupCard ??
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
        PublishState();
    }

    private void ResolvePendingAiFireAttack()
    {
        var pending = _pendingFireAttack ??
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
            var revealed = _cardZones.CardsAt(CardLocation.Processing)
                .Single(card => card.Id == (pending.RevealedCardId ??
                    throw new InvalidOperationException("FireAttack discard selection has no revealed card.")));
            var (cardId, thought) = _aiBrains[actor.Seat].ChooseFireAttackDiscard(
                view,
                pending.TargetSeat,
                revealed.Suit,
                decision.ValidCardIds,
                ++_thoughtSequence);
            AddThought(thought);
            ClearPendingDecision();
            ResolveFireAttackDiscard(pending, cardId);
        }

        PublishState();
    }

    private void ResolvePendingAiDuel()
    {
        var duel = _pendingDuel ??
            throw new InvalidOperationException("AI Duel continuation has no pending Duel.");
        var responder = _players[duel.ResponderSeat];
        var slashes = GetResponseCards(responder, CardKind.Slash);
        PopResponseWindow(duel.ResolutionId);
        SetCardUseStep(duel.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        _pendingDecision = null;

        if (slashes.Count == 0)
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
            ResolveDuelResponse(duel, responder, useSlash ? slashes[0] : null);
        }

        PublishState();
    }

    private void ResolvePendingAiGroupAttack()
    {
        var group = _pendingGroupCard ??
            throw new InvalidOperationException("AI group response has no pending group attack.");
        var attack = group.CurrentAttack ??
            throw new InvalidOperationException("AI group response has no current target.");
        var responder = _players[attack.TargetSeat];
        var requiredCardKind = group.RequiredCardKind ??
            throw new InvalidOperationException("A group response attack must declare a required card kind.");
        var responseCards = GetResponseCards(responder, requiredCardKind);
        PopResponseWindow(group.ResolutionId);
        SetCardUseStep(group.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        _pendingDecision = null;

        if (responseCards.Count == 0)
        {
            ResolveGroupResponse(group, responder, selectedResponse: null);
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
            ResolveGroupResponse(group, responder, useResponse ? responseCards[0] : null);
        }

        PublishState();
    }

    private void ResolvePendingAiDodge()
    {
        var attack = _pendingAttack ??
            throw new InvalidOperationException("AI Dodge continuation has no pending Slash.");
        var source = _players[attack.SourceSeat];
        var target = _players[attack.TargetSeat];
        PopResponseWindow(attack.ResolutionId);
        SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        _pendingDecision = null;
        var dodges = GetResponseCards(target, CardKind.Dodge);
        var dodge = dodges.FirstOrDefault();

        if (!target.IsAlive || dodge is null)
        {
            if (!ApplyAttackDamage(attack))
            {
                CompleteAttack(attack);
            }
        }
        else
        {
            var view = CreateSnapshot(target.Seat);
            var (useDodge, thought) = _aiBrains[target.Seat].ChooseDodge(view, source.Seat, ++_thoughtSequence);
            AddThought(thought);
            if (useDodge)
            {
                ResolveDodgeResponse(attack, target, dodge!);
            }
            else
            {
                if (!ApplyAttackDamage(attack))
                {
                    CompleteAttack(attack);
                }
            }
        }

        PublishState();
    }

    private void ResolveDodgeResponse(
        AttackResolution attack,
        PlayerRuntime defender,
        Card responseCard)
    {
        var responseCardKind = GetEffectiveResponseKind(
            defender,
            responseCard,
            CardKind.Dodge);
        MoveCard(
            responseCard,
            CardLocation.Hand(defender.Seat),
            CardLocation.Processing,
            CardMoveReasons.Respond);
        var incomingName = CardCatalog.Get(attack.EffectiveCardKind).DisplayName;
        var responseName = CardCatalog.Get(responseCardKind).DisplayName;
        var responseDescription = IsNativeResponseCard(responseCard, CardKind.Dodge)
            ? $"打出【{responseName}】"
            : $"将【{responseCard.DisplayName}】当作【{responseName}】";
        AddLog(
            "CardResponded",
            $"{defender.Name} {responseDescription}，抵消了【{incomingName}】。",
            defender.Seat,
            attack.SourceSeat);
        QueueGameEvent(new CardRespondedEvent(
            responseCard.Id,
            defender.Seat,
            attack.SourceSeat,
            responseCardKind));
        MoveCard(
            responseCard,
            CardLocation.Processing,
            CardLocation.DiscardPile,
            CardMoveReasons.ResponseFinished);
        CompleteAttack(attack);
    }

    private bool ApplyAttackDamage(AttackResolution attack)
    {
        var source = _players[attack.SourceSeat];
        var target = _players[attack.TargetSeat];
        if (!target.IsAlive)
        {
            return false;
        }

        var nature = GetDamageNature(attack.EffectiveCardKind);
        var amount = attack.DamageAmount;
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
            QueueGameEvent(new DamageRequestedEvent(
                damageFrameId,
                source.Seat,
                target.Seat,
                amount,
                attack.EffectiveCardKind,
                nature));
            target.Hp = Math.Max(0, target.Hp - amount);
            var natureLabel = GetDamageNatureLabel(nature);
            AddLog("Damage", $"{target.Name} 受到 {source.Name} 造成的 {amount} 点{natureLabel}伤害，剩余 {Math.Max(0, target.Hp)} 点体力。", source.Seat, target.Seat);
            QueueGameEvent(new DamageAppliedEvent(
                source.Seat,
                target.Seat,
                amount,
                Math.Max(0, target.Hp),
                nature));

            if (target.Hp > 0)
            {
                var damageContext = new DamageSkillContext(
                    CreateSkillContext(target),
                    source.Seat,
                    attack.EffectiveCardKind,
                    SourceCardIsInProcessing:
                        _cardZones.GetLocation(attack.Card.Id) == CardLocation.Processing,
                    Nature: nature,
                    Amount: amount,
                    SourceCardId: attack.Card.Id,
                    TargetSeat: target.Seat,
                    TargetHp: target.Hp,
                    TargetMaxHp: target.MaxHp);
                var triggerCandidates = DamageTriggerOrdering.Order(
                    CollectDamageTriggerCandidates(damageContext),
                    source.Seat,
                    _playerCount);
                if (triggerCandidates.Count > 0)
                {
                    BeginDamageTriggerWindow(attack, damageFrameId, triggerCandidates);
                    awaitingDamageTrigger = true;
                    // Keep the established public boundary for an optional
                    // trigger: a human should receive its prompt as part of
                    // the card command that caused the damage. Automatic
                    // candidates remain one deterministic engine step each.
                    if (triggerCandidates[0].IsOptional)
                    {
                        RunOneDamageTriggerStep();
                    }
                }
            }

            if (target.Hp <= 0)
            {
                BeginDying(attack, damageFrameId, target, source);
                awaitingDying = _pendingDying is not null;
            }

            if (!awaitingDying && !awaitingDamageTrigger)
            {
                QueueGameEvent(new AfterDamageEvent(
                    damageFrameId,
                    source.Seat,
                    target.Seat,
                    amount,
                    Math.Max(0, target.Hp),
                    nature));
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

    private void BeginDamageTriggerWindow(
        AttackResolution attack,
        long damageFrameId,
        IReadOnlyList<DamageTriggerCandidate> candidates)
    {
        if (_pendingDamageTrigger is not null)
        {
            throw new InvalidOperationException("The engine cannot resolve two damage trigger windows at once.");
        }

        var frozenCandidates = Array.AsReadOnly(candidates.ToArray());
        var frameId = ++_resolutionSequence;
        _resolutionStack.Add(new DamageTriggerWindowFrame(
            frameId,
            damageFrameId,
            attack.SourceSeat,
            attack.TargetSeat,
            attack.Card.Id,
            attack.EffectiveCardKind,
            frozenCandidates));
        QueueGameEvent(new DamageTriggerWindowOpenedEvent(
            frameId,
            damageFrameId,
            attack.SourceSeat,
            attack.TargetSeat,
            attack.Card.Id,
            attack.EffectiveCardKind,
            frozenCandidates));
        _pendingDamageTrigger = new DamageTriggerResolution(
            frameId,
            damageFrameId,
            attack,
            frozenCandidates);
    }

    private void RunOneDamageTriggerStep()
    {
        var window = _pendingDamageTrigger ??
            throw new InvalidOperationException("A damage trigger step has no pending window.");
        if (_pendingDamageSkill is not null || _pendingDecision is not null)
        {
            return;
        }

        if (window.CandidateIndex >= window.Candidates.Count)
        {
            CompleteDamageTriggerWindow(window);
            return;
        }

        var candidate = window.Candidates[window.CandidateIndex];
        var owner = _players[candidate.OwnerSeat];
        var context = CreateDamageSkillContext(window.Attack, owner);
        var skill = SkillRegistry.Get(candidate.Skill);
        if (!owner.IsAlive ||
            !skill.CanTriggerAfterDamage(context) ||
            (!skill.OffersDamageCardChoice(context) && !skill.ClaimsDamageCard(context)))
        {
            AdvanceDamageTriggerCandidate(window);
            return;
        }

        if (candidate.IsOptional)
        {
            BeginDamageSkillChoice(window, candidate, owner, skill);
            return;
        }

        if (skill.ClaimsDamageCard(context))
        {
            ClaimDamageCard(window.Attack, window.DamageFrameId, owner, skill, context);
        }

        AdvanceDamageTriggerCandidate(window);
    }

    private void AdvanceDamageTriggerCandidate(DamageTriggerResolution window)
    {
        if (!ReferenceEquals(_pendingDamageTrigger, window))
        {
            throw new InvalidOperationException("The completed damage trigger window is not current.");
        }

        window.CandidateIndex++;
        SetDamageTriggerWindowCursor(window.FrameId, window.CandidateIndex);
        QueueGameEvent(new DamageTriggerWindowAdvancedEvent(
            window.FrameId,
            window.CandidateIndex,
            window.CandidateIndex >= window.Candidates.Count));
        if (window.CandidateIndex >= window.Candidates.Count)
        {
            CompleteDamageTriggerWindow(window);
        }
    }

    private void CompleteDamageTriggerWindow(DamageTriggerResolution window)
    {
        if (!ReferenceEquals(_pendingDamageTrigger, window) ||
            _pendingDamageSkill is not null)
        {
            throw new InvalidOperationException("The damage trigger window is not ready to complete.");
        }

        SetDamageTriggerWindowStep(window.FrameId, ResolutionFrameStep.Completed);
        var target = _players[window.Attack.TargetSeat];
        QueueGameEvent(new AfterDamageEvent(
            window.DamageFrameId,
            window.Attack.SourceSeat,
            window.Attack.TargetSeat,
            window.Attack.DamageAmount,
            Math.Max(0, target.Hp),
            GetDamageNature(window.Attack.EffectiveCardKind)));
        PopResolutionFrame(window.FrameId, ResolutionFrameKind.DamageTriggerWindow);
        _pendingDamageTrigger = null;
        PopResolutionFrame(window.DamageFrameId, ResolutionFrameKind.Damage);
        CompleteAttack(window.Attack);
    }

    private void BeginDamageSkillChoice(
        DamageTriggerResolution window,
        DamageTriggerCandidate candidate,
        PlayerRuntime owner,
        IPassiveSkill skill)
    {
        var attack = window.Attack;
        var damageFrameId = window.DamageFrameId;
        SetDamageTriggerWindowStep(window.FrameId, ResolutionFrameStep.AwaitingResponse);
        if (_pendingDamageSkill is not null)
        {
            throw new InvalidOperationException("The engine cannot resolve two damage skills at once.");
        }

        var context = CreateDamageSkillContext(attack, owner);
        var effect = candidate.Effect == DamageSkillEffectKind.None
            ? skill.GetDamageSkillEffect(context)
            : candidate.Effect;
        if (effect == DamageSkillEffectKind.None)
        {
            throw new InvalidOperationException(
                $"Optional damage skill {skill.Kind} did not declare a resolvable effect.");
        }

        var effectCardIds = effect == DamageSkillEffectKind.GiftDrawnCard
            ? DrawYijiCards(owner, damageFrameId)
            : Array.Empty<int>();
        var frameId = ++_resolutionSequence;
        _resolutionStack.Add(new DamageSkillFrame(
            frameId,
            window.FrameId,
            owner.Seat,
            attack.SourceSeat,
            attack.Card.Id,
            attack.EffectiveCardKind,
            skill.Kind,
            CandidateId: candidate.CandidateId,
            Priority: candidate.Priority,
            Effect: effect,
            EffectCardIds: effectCardIds));
        _pendingDamageSkill = new DamageSkillResolution(
            frameId,
            window.FrameId,
            damageFrameId,
            attack,
            owner.Seat,
            skill.Kind,
            attack.EffectiveCardKind,
            candidate.CandidateId,
            candidate.Priority,
            effect,
            effectCardIds);
        QueueGameEvent(new DamageSkillRequestedEvent(
            damageFrameId,
            owner.Seat,
            attack.SourceSeat,
            attack.Card.Id,
            attack.EffectiveCardKind,
            skill.Kind,
            candidate.CandidateId,
            candidate.Priority));

        _pendingDecision = effect switch
        {
            DamageSkillEffectKind.ClaimDamageCard => CreateFeedbackDecision(
                owner,
                skill,
                attack),
            DamageSkillEffectKind.GiftDrawnCard => CreateYijiDecision(
                owner,
                skill,
                attack,
                effectCardIds),
            DamageSkillEffectKind.DrawToMaxHand => CreateJiemingDecision(
                owner,
                skill,
                attack),
            DamageSkillEffectKind.RecoverDamageTarget => CreateYuanhuDecision(
                owner,
                skill,
                attack),
            _ => throw new InvalidOperationException($"Unsupported damage skill effect {effect}.")
        };
        _status = owner.IsHuman
            ? EngineStatus.AwaitingHumanResponse
            : EngineStatus.Running;
    }

    private PendingDecision CreateFeedbackDecision(
        PlayerRuntime owner,
        IPassiveSkill skill,
        AttackResolution attack)
    {
        var cardName = CardCatalog.Get(attack.Card.Kind).DisplayName;
        return new PendingDecision(
            DecisionKind.Feedback,
            owner.Seat,
            $"{owner.Name} 受到伤害，是否发动【{skill.Name}】获得造成伤害的【{cardName}】？",
            [attack.Card.Id],
            [],
            attack.SourceSeat,
            attack.EffectiveCardKind)
        {
            PromptId = CreatePromptId(),
            Choices =
            [
                new PromptChoice(
                    new ChoiceId($"feedback.use.card-{attack.Card.Id}"),
                    $"发动【{skill.Name}】，获得造成伤害的【{cardName}】。",
                    [attack.Card.Id],
                    [],
                    new Dictionary<string, string>
                    {
                        ["response"] = "feedback",
                        ["action"] = "claim-damage-card"
                    }),
                new PromptChoice(
                    new ChoiceId("feedback.skip"),
                    $"不发动【{skill.Name}】，将伤害牌置入弃牌堆。",
                    [],
                    [],
                    new Dictionary<string, string>
                    {
                        ["response"] = "take-damage",
                        ["action"] = "skip-damage-skill"
                    })
            ],
            RequiredCardKind = null,
            TargetSeat = owner.Seat
        };
    }

    private PendingDecision CreateYijiDecision(
        PlayerRuntime owner,
        IPassiveSkill skill,
        AttackResolution attack,
        IReadOnlyList<int> effectCardIds)
    {
        var targetSeats = GetYijiTargetSeats(owner.Seat);
        var cards = effectCardIds
            .Select(cardId => GetHand(owner).Single(card => card.Id == cardId))
            .ToArray();
        var giftChoices =
            from card in cards
            from targetSeat in targetSeats
            let target = _players[targetSeat]
            select new PromptChoice(
                new ChoiceId($"yiji.gift.card-{card.Id}.target-{targetSeat}"),
                $"将【{card.DisplayName}】交给 {target.Name}（座位 {targetSeat + 1}）。",
                [card.Id],
                [targetSeat],
                new Dictionary<string, string>
                {
                    ["response"] = "yiji-gift",
                    ["action"] = "gift-drawn-card"
                });

        return new PendingDecision(
            DecisionKind.Yiji,
            owner.Seat,
            $"{owner.Name} 发动【{skill.Name}】：选择一张摸到的牌交给一名其他存活角色，或保留这些牌。",
            effectCardIds,
            targetSeats,
            attack.SourceSeat,
            attack.EffectiveCardKind)
        {
            PromptId = CreatePromptId(),
            Choices = giftChoices
                .Append(new PromptChoice(
                    new ChoiceId("yiji.skip"),
                    "不分配，保留摸到的牌。",
                    [],
                    [],
                    new Dictionary<string, string>
                    {
                        ["response"] = "yiji-skip",
                        ["action"] = "skip-yiji"
                    }))
                .ToArray(),
            RequiredCardKind = null,
            TargetSeat = owner.Seat
        };
    }

    private IReadOnlyList<int> DrawYijiCards(PlayerRuntime owner, long damageFrameId)
    {
        var drawnCardIds = new List<int>();
        for (var index = 0; index < 2; index++)
        {
            if (DrawOne(owner, CardMoveReasons.YijiDraw) is { } card)
            {
                drawnCardIds.Add(card.Id);
            }
        }

        if (drawnCardIds.Count > 0)
        {
            QueueGameEvent(new DamageSkillCardsDrawnEvent(
                damageFrameId,
                owner.Seat,
                SkillKind.Yiji,
                drawnCardIds));
            AddLog("SkillDraw", $"{owner.Name} 因【遗计】摸了 {drawnCardIds.Count} 张牌。", owner.Seat);
        }

        return Array.AsReadOnly(drawnCardIds.ToArray());
    }

    private IReadOnlyList<int> GetYijiTargetSeats(int ownerSeat) =>
        _players
            .Where(player => player.IsAlive && player.Seat != ownerSeat)
            .Select(player => player.Seat)
            .ToArray();

    private PendingDecision CreateJiemingDecision(
        PlayerRuntime owner,
        IPassiveSkill skill,
        AttackResolution attack)
    {
        var targetSeats = GetJiemingTargetSeats();
        var choices = targetSeats
            .Select(targetSeat =>
            {
                var target = _players[targetSeat];
                var drawCount = target.MaxHp - GetHand(target).Count;
                return new PromptChoice(
                    new ChoiceId($"jieming.draw.target-{targetSeat}"),
                    $"令 {target.Name} 摸 {drawCount} 张牌至体力上限。",
                    [],
                    [targetSeat],
                    new Dictionary<string, string>
                    {
                        ["response"] = "jieming-draw",
                        ["action"] = "draw-to-max-hand"
                    });
            })
            .Append(new PromptChoice(
                new ChoiceId("jieming.skip"),
                $"不发动【{skill.Name}】。",
                [],
                [],
                new Dictionary<string, string>
                {
                    ["response"] = "jieming-skip",
                    ["action"] = "skip-damage-skill"
                }))
            .ToArray();

        return new PendingDecision(
            DecisionKind.Jieming,
            owner.Seat,
            $"{owner.Name} 受到伤害，是否发动【{skill.Name}】令一名角色补牌至体力上限？",
            [],
            targetSeats,
            attack.SourceSeat,
            attack.EffectiveCardKind)
        {
            PromptId = CreatePromptId(),
            Choices = choices,
            RequiredCardKind = null,
            TargetSeat = owner.Seat
        };
    }

    private IReadOnlyList<int> GetJiemingTargetSeats() =>
        _players
            .Where(player => player.IsAlive && GetHand(player).Count < player.MaxHp)
            .Select(player => player.Seat)
            .ToArray();

    private PendingDecision CreateYuanhuDecision(
        PlayerRuntime owner,
        IPassiveSkill skill,
        AttackResolution attack)
    {
        var target = _players[attack.TargetSeat];
        var choices = GetHand(owner)
            .Select(card => new PromptChoice(
                new ChoiceId($"yuanhu.discard.card-{card.Id}"),
                $"弃置【{card.DisplayName}】，令 {target.Name} 回复 1 点体力。",
                [card.Id],
                [target.Seat],
                new Dictionary<string, string>
                {
                    ["response"] = "yuanhu",
                    ["action"] = "recover-damage-target"
                }))
            .Append(new PromptChoice(
                new ChoiceId("yuanhu.skip"),
                $"不发动【{skill.Name}】。",
                [],
                [],
                new Dictionary<string, string>
                {
                    ["response"] = "yuanhu-skip",
                    ["action"] = "skip-damage-skill"
                }))
            .ToArray();

        return new PendingDecision(
            DecisionKind.Yuanhu,
            owner.Seat,
            $"{owner.Name} 可弃置一张手牌，令受伤的 {target.Name} 回复 1 点体力。",
            GetHand(owner).Select(card => card.Id).ToArray(),
            [target.Seat],
            attack.SourceSeat,
            attack.EffectiveCardKind)
        {
            PromptId = CreatePromptId(),
            Choices = choices,
            RequiredCardKind = null,
            TargetSeat = target.Seat
        };
    }

    private void ResolveDamageSkillChoice(
        DamageSkillResolution pending,
        bool useSkill,
        int? selectedCardId = null,
        int? selectedTargetSeat = null)
    {
        if (!ReferenceEquals(_pendingDamageSkill, pending))
        {
            throw new InvalidOperationException("The completed damage skill is not current.");
        }

        var window = _pendingDamageTrigger ??
            throw new InvalidOperationException("A damage skill must belong to a damage trigger window.");
        if (window.FrameId != pending.TriggerFrameId || window.DamageFrameId != pending.DamageFrameId)
        {
            throw new InvalidOperationException("The damage skill does not belong to the current trigger window.");
        }

        var owner = _players[pending.OwnerSeat];
        var source = _players[pending.SourceSeat];
        var skill = SkillRegistry.Get(pending.Skill);
        var damageContext = CreateDamageSkillContext(pending.Attack, owner);
        Card? giftedCard = null;
        PlayerRuntime? giftedTarget = null;
        PlayerRuntime? jiemingTarget = null;
        var jiemingDrawCount = 0;
        Card? yuanhuDiscardedCard = null;
        PlayerRuntime? yuanhuRecoveredTarget = null;
        var yuanhuRecoveryAmount = 0;
        if (useSkill)
        {
            switch (pending.Effect)
            {
                case DamageSkillEffectKind.ClaimDamageCard:
                    if (!skill.ClaimsDamageCard(damageContext))
                    {
                        throw new InvalidOperationException(
                            $"Skill {pending.Skill} cannot claim the current damage card.");
                    }

                    ClaimDamageCard(pending.Attack, pending.DamageFrameId, owner, skill, damageContext);
                    break;
                case DamageSkillEffectKind.GiftDrawnCard:
                    if (selectedCardId is not { } cardId ||
                        selectedTargetSeat is not { } targetSeat ||
                        !pending.EffectCardIds.Contains(cardId) ||
                        targetSeat == owner.Seat ||
                        !GetYijiTargetSeats(owner.Seat).Contains(targetSeat) ||
                        !IsValidPlayerSeat(targetSeat) ||
                        !_players[targetSeat].IsAlive)
                    {
                        throw new InvalidOperationException("The selected Yiji card or target is not legal.");
                    }

                    giftedCard = GetHand(owner).SingleOrDefault(card => card.Id == cardId) ??
                        throw new InvalidOperationException("The selected Yiji card is not in the skill owner's hand.");
                    giftedTarget = _players[targetSeat];
                    MoveCard(
                        giftedCard,
                        CardLocation.Hand(owner.Seat),
                        CardLocation.Hand(giftedTarget.Seat),
                        CardMoveReasons.YijiGive);
                    QueueGameEvent(new DamageSkillCardGivenEvent(
                        pending.DamageFrameId,
                        owner.Seat,
                        giftedTarget.Seat,
                        giftedCard.Id,
                        giftedCard.Kind,
                        pending.Skill));
                    break;
                case DamageSkillEffectKind.DrawToMaxHand:
                    if (selectedTargetSeat is not { } jiemingSeat ||
                        !GetJiemingTargetSeats().Contains(jiemingSeat) ||
                        !IsValidPlayerSeat(jiemingSeat))
                    {
                        throw new InvalidOperationException("The selected Jieming target is not legal.");
                    }

                    jiemingTarget = _players[jiemingSeat];
                    jiemingDrawCount = Math.Max(0, jiemingTarget.MaxHp - GetHand(jiemingTarget).Count);
                    var drawnCardIds = new List<int>(jiemingDrawCount);
                    for (var index = 0; index < jiemingDrawCount; index++)
                    {
                        if (DrawOne(jiemingTarget, CardMoveReasons.JiemingDraw) is { } drawnCard)
                        {
                            drawnCardIds.Add(drawnCard.Id);
                        }
                    }

                    jiemingDrawCount = drawnCardIds.Count;
                    if (drawnCardIds.Count > 0)
                    {
                        QueueGameEvent(new DamageSkillCardsDrawnEvent(
                            pending.DamageFrameId,
                            owner.Seat,
                            pending.Skill,
                            drawnCardIds,
                            jiemingTarget.Seat));
                    }

                    break;
                case DamageSkillEffectKind.RecoverDamageTarget:
                    if (selectedCardId is not { } yuanhuCardId ||
                        selectedTargetSeat is not { } yuanhuTargetSeat ||
                        yuanhuTargetSeat != pending.Attack.TargetSeat ||
                        !IsValidPlayerSeat(yuanhuTargetSeat) ||
                        !GetHand(owner).Any(card => card.Id == yuanhuCardId))
                    {
                        throw new InvalidOperationException("The selected Yuanhu card or target is not legal.");
                    }

                    yuanhuRecoveredTarget = _players[yuanhuTargetSeat];
                    if (!yuanhuRecoveredTarget.IsAlive ||
                        yuanhuRecoveredTarget.Hp <= 0 ||
                        yuanhuRecoveredTarget.Hp >= yuanhuRecoveredTarget.MaxHp ||
                        !skill.CanTriggerAfterDamage(damageContext) ||
                        !skill.OffersDamageCardChoice(damageContext))
                    {
                        throw new InvalidOperationException("The Yuanhu target is no longer recoverable.");
                    }

                    yuanhuDiscardedCard = GetHand(owner).Single(card => card.Id == yuanhuCardId);
                    MoveCard(
                        yuanhuDiscardedCard,
                        CardLocation.Hand(owner.Seat),
                        CardLocation.DiscardPile,
                        CardMoveReasons.YuanhuDiscard);
                    QueueGameEvent(new DamageSkillCardDiscardedEvent(
                        pending.DamageFrameId,
                        owner.Seat,
                        yuanhuDiscardedCard.Id,
                        yuanhuDiscardedCard.Kind,
                        pending.Skill));

                    var recoveryFrameId = BeginRecovery(
                        pending.DamageFrameId,
                        owner.Seat,
                        yuanhuRecoveredTarget.Seat,
                        1);
                    try
                    {
                        yuanhuRecoveredTarget.Hp = Math.Min(
                            yuanhuRecoveredTarget.MaxHp,
                            yuanhuRecoveredTarget.Hp + 1);
                        yuanhuRecoveryAmount = 1;
                        AddLog(
                            "Recovered",
                            $"{owner.Name} 因【{skill.Name}】使 {yuanhuRecoveredTarget.Name} 回复至 {yuanhuRecoveredTarget.Hp}/{yuanhuRecoveredTarget.MaxHp} 点体力。",
                            owner.Seat,
                            yuanhuRecoveredTarget.Seat);
                        QueueGameEvent(new RecoveryAppliedEvent(
                            owner.Seat,
                            yuanhuRecoveredTarget.Seat,
                            yuanhuRecoveryAmount,
                            yuanhuRecoveredTarget.Hp));
                    }
                    finally
                    {
                        PopResolutionFrame(recoveryFrameId, ResolutionFrameKind.Recovery);
                    }

                    break;
                default:
                    throw new InvalidOperationException(
                        $"Skill {pending.Skill} has no supported damage effect.");
            }
        }

        SetDamageSkillFrameStep(pending.FrameId, ResolutionFrameStep.ResolvingEffect);
        QueueGameEvent(new DamageSkillResolvedEvent(
            pending.DamageFrameId,
            pending.OwnerSeat,
            pending.SourceSeat,
            pending.Card.Id,
            pending.EffectiveCardKind,
            pending.Skill,
            useSkill,
            pending.CandidateId,
            pending.Priority,
            (yuanhuRecoveredTarget ?? jiemingTarget)?.Seat));
        AddLog(
            "SkillTriggered",
            useSkill
                ? pending.Effect switch
                {
                    DamageSkillEffectKind.GiftDrawnCard =>
                        $"{owner.Name} 触发【{skill.Name}】，将【{giftedCard!.DisplayName}】交给了 {giftedTarget!.Name}。",
                    DamageSkillEffectKind.DrawToMaxHand =>
                        $"{owner.Name} 触发【{skill.Name}】，令 {jiemingTarget!.Name} 摸了 {jiemingDrawCount} 张牌。",
                    DamageSkillEffectKind.RecoverDamageTarget =>
                        $"{owner.Name} 触发【{skill.Name}】，弃置【{yuanhuDiscardedCard!.DisplayName}】令 {yuanhuRecoveredTarget!.Name} 回复 1 点体力。",
                    _ => $"{owner.Name} 触发【{skill.Name}】。"
                }
                : pending.Effect switch
                {
                    DamageSkillEffectKind.GiftDrawnCard =>
                        $"{owner.Name} 选择不发动【{skill.Name}】分配，保留摸到的牌。",
                    DamageSkillEffectKind.DrawToMaxHand =>
                        $"{owner.Name} 选择不发动【{skill.Name}】。",
                    DamageSkillEffectKind.RecoverDamageTarget =>
                        $"{owner.Name} 选择不发动【{skill.Name}】。",
                    _ => $"{owner.Name} 选择不发动【{skill.Name}】，伤害牌将进入弃牌堆。"
                },
            owner.Seat,
            source.Seat);
        PopResolutionFrame(pending.FrameId, ResolutionFrameKind.DamageSkill);
        _pendingDamageSkill = null;
        SetDamageTriggerWindowStep(window.FrameId, ResolutionFrameStep.ResolvingEffect);
        AdvanceDamageTriggerCandidate(window);
    }

    private IReadOnlyList<DamageTriggerCandidate> CollectDamageTriggerCandidates(
        DamageSkillContext context)
    {
        var candidates = new List<DamageTriggerCandidate>();
        foreach (var owner in _players.Where(player => player.IsAlive))
        {
            var ownerContext = context with
            {
                Owner = CreateSkillContext(owner)
            };
            var skill = SkillRegistry.Get(owner.General.Skill);
            if (!skill.CanTriggerAfterDamage(ownerContext))
            {
                continue;
            }

            var offersChoice = skill.OffersDamageCardChoice(ownerContext);
            var claimsCard = skill.ClaimsDamageCard(ownerContext);
            if (!offersChoice && !claimsCard)
            {
                continue;
            }

            var effect = skill.GetDamageSkillEffect(ownerContext);
            if (effect == DamageSkillEffectKind.None)
            {
                throw new InvalidOperationException(
                    $"Skill {skill.Kind} exposes a damage trigger without an effect.");
            }

            candidates.Add(new DamageTriggerCandidate(
                owner.Seat,
                skill.Kind,
                skill.DamageTriggerId,
                skill.DamageTriggerPriority,
                IsOptional: offersChoice,
                Effect: effect));
        }

        return candidates;
    }

    private DamageSkillContext CreateDamageSkillContext(
        AttackResolution attack,
        PlayerRuntime owner)
    {
        var target = _players[attack.TargetSeat];
        return new(
            CreateSkillContext(owner),
            attack.SourceSeat,
            attack.EffectiveCardKind,
            SourceCardIsInProcessing:
                _cardZones.GetLocation(attack.Card.Id) == CardLocation.Processing,
            Nature: GetDamageNature(attack.EffectiveCardKind),
            Amount: attack.DamageAmount,
            SourceCardId: attack.Card.Id,
            TargetSeat: attack.TargetSeat,
            TargetHp: target.Hp,
            TargetMaxHp: target.MaxHp);
    }

    private void ClaimDamageCard(
        AttackResolution attack,
        long damageFrameId,
        PlayerRuntime owner,
        IPassiveSkill skill,
        DamageSkillContext context)
    {
        var claimReason = skill.Kind switch
        {
            SkillKind.Jianxiong => CardMoveReasons.JianxiongClaim,
            SkillKind.Feedback => CardMoveReasons.FeedbackClaim,
            _ => throw new InvalidOperationException(
                $"Skill {skill.Kind} cannot claim a damage card without a movement reason.")
        };
        if (!context.SourceCardIsInProcessing)
        {
            throw new InvalidOperationException("A damage skill can only claim a card from Processing.");
        }

        MoveCard(
            attack.Card,
            CardLocation.Processing,
            CardLocation.Hand(owner.Seat),
            claimReason);
        QueueGameEvent(new DamageCardClaimedEvent(
            damageFrameId,
            owner.Seat,
            attack.SourceSeat,
            attack.Card.Id,
            attack.Card.Kind,
            skill.Kind));
    }

    private void BeginDying(
        AttackResolution attack,
        long damageFrameId,
        PlayerRuntime victim,
        PlayerRuntime killer)
    {
        if (_pendingDying is not null)
        {
            throw new InvalidOperationException("The engine cannot resolve two dying players at once.");
        }

        var responderSeats = Array.AsReadOnly(BuildDyingResponderSeats(victim.Seat).ToArray());
        var frameId = ++_resolutionSequence;
        _resolutionStack.Add(new DyingFrame(
            frameId,
            damageFrameId,
            victim.Seat,
            killer.Seat,
            responderSeats,
            ResponderIndex: 0));
        _pendingDying = new DyingResolution(
            frameId,
            damageFrameId,
            attack,
            victim.Seat,
            killer.Seat,
            responderSeats);
        QueueGameEvent(new PlayerDyingEvent(frameId, victim.Seat, killer.Seat));
        _status = EngineStatus.Running;
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

    private void RunOneDyingStep()
    {
        var dying = _pendingDying ??
            throw new InvalidOperationException("A dying step requires a pending dying resolution.");
        if (_pendingDecision is not null)
        {
            return;
        }

        if (dying.ResponderIndex >= dying.ResponderSeats.Count)
        {
            CompleteDying(dying, survived: false);
            PublishState();
            return;
        }

        var responder = _players[dying.ResponderSeat];
        var peaches = GetHand(responder)
            .Where(card => card.Kind == CardKind.Peach)
            .ToArray();
        var alcohols = responder.Seat == dying.VictimSeat
            ? GetHand(responder)
                .Where(card => card.Kind == CardKind.Alcohol)
                .ToArray()
            : [];
        if (responder.IsHuman)
        {
            if (peaches.Length > 0 || alcohols.Length > 0)
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
            PublishState();
            return;
        }

        var view = CreateSnapshot(responder.Seat);
        var (usePeach, peachCardId, useAlcohol, alcoholCardId, thought) = _aiBrains[responder.Seat].ChooseDyingResponseWithAlcohol(
            view,
            dying.VictimSeat,
            peaches,
            alcohols,
            ++_thoughtSequence);
        AddThought(thought);
        ApplyDyingResponse(responder, usePeach, peachCardId, useAlcohol, alcoholCardId);
        PublishState();
    }

    private void ExposeHumanDyingPrompt()
    {
        var dying = _pendingDying;
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

        var peaches = GetHand(responder)
            .Where(card => card.Kind == CardKind.Peach)
            .ToArray();
        var alcohols = responder.Seat == dying.VictimSeat
            ? GetHand(responder)
                .Where(card => card.Kind == CardKind.Alcohol)
                .ToArray()
            : [];
        if (peaches.Length > 0 || alcohols.Length > 0)
        {
            RequestHumanDyingResponse(responder, peaches, alcohols);
        }
    }

    private void RequestHumanDyingResponse(
        PlayerRuntime responder,
        IReadOnlyList<Card> peaches,
        IReadOnlyList<Card> alcohols)
    {
        var dying = _pendingDying ??
            throw new InvalidOperationException("There is no dying resolution for the prompt.");
        var victim = _players[dying.VictimSeat];
        var choices = peaches.Select(peach => new PromptChoice(
            new ChoiceId($"dying.peach.card-{peach.Id}"),
            $"使用【桃】救援 {victim.Name}。",
            [peach.Id],
            [],
            new Dictionary<string, string>
            {
                ["response"] = "peach",
                ["target-seat"] = victim.Seat.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })).ToList();
        choices.AddRange(alcohols.Select(alcohol => new PromptChoice(
            new ChoiceId($"dying.alcohol.card-{alcohol.Id}"),
            $"使用【酒】自救，使 {victim.Name} 回复 1 点体力。",
            [alcohol.Id],
            [],
            new Dictionary<string, string>
            {
                ["response"] = "alcohol",
                ["target-seat"] = victim.Seat.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })));
        var rescueCardIds = peaches
            .Select(card => card.Id)
            .Concat(alcohols.Select(card => card.Id))
            .ToArray();
        var rescueNames = peaches.Count > 0
            ? alcohols.Count > 0 ? "桃】或【酒" : "桃"
            : "酒";
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
        PublishState();
    }

    private void ApplyDyingResponse(
        PlayerRuntime responder,
        bool usePeach,
        int? peachCardId,
        bool useAlcohol,
        int? alcoholCardId)
    {
        var dying = _pendingDying ??
            throw new InvalidOperationException("There is no dying response to apply.");
        if (responder.Seat != dying.ResponderSeat)
        {
            throw new InvalidOperationException("The response does not belong to the current dying responder.");
        }

        var victim = _players[dying.VictimSeat];
        SetDyingFrameStep(dying.FrameId, ResolutionFrameStep.ResolvingEffect);
        int? usedPeachCardId = null;
        int? usedAlcoholCardId = null;
        if (usePeach && useAlcohol)
        {
            throw new InvalidOperationException("A dying response can use either Peach or Alcohol, not both.");
        }
        if (usePeach)
        {
            var peach = GetHand(responder).FirstOrDefault(card =>
                card.Kind == CardKind.Peach &&
                (!peachCardId.HasValue || card.Id == peachCardId.Value));
            if (peach is null)
            {
                throw new InvalidOperationException("The requested Peach is not in the responder's hand.");
            }

            usedPeachCardId = peach.Id;
            ResolvePeach(responder, victim, peach, allowDying: true);
        }
        if (useAlcohol)
        {
            if (responder.Seat != victim.Seat)
            {
                throw new InvalidOperationException("Alcohol can only rescue its dying holder.");
            }
            var alcohol = GetHand(responder).FirstOrDefault(card =>
                card.Kind == CardKind.Alcohol &&
                (!alcoholCardId.HasValue || card.Id == alcoholCardId.Value));
            if (alcohol is null)
            {
                throw new InvalidOperationException("The requested Alcohol is not in the responder's hand.");
            }
            usedAlcoholCardId = alcohol.Id;
            ResolveDyingAlcohol(responder, victim, alcohol);
        }

        QueueGameEvent(new DyingResponseEvent(
            dying.FrameId,
            responder.Seat,
            usePeach,
            usedPeachCardId,
            useAlcohol,
            usedAlcoholCardId));
        dying.ResponderIndex++;

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

    private void CompleteDying(DyingResolution dying, bool survived)
    {
        if (!ReferenceEquals(_pendingDying, dying))
        {
            throw new InvalidOperationException("The completed dying resolution is not current.");
        }

        SetDyingFrameStep(dying.FrameId, ResolutionFrameStep.ResolvingEffect);
        if (!survived)
        {
            FinalizePlayerDeath(dying);
        }

        QueueGameEvent(new DyingResolvedEvent(dying.FrameId, dying.VictimSeat, survived));
        PopResolutionFrame(dying.FrameId, ResolutionFrameKind.Dying);
        _pendingDying = null;
        CompleteDamageAfterDying(dying);
    }

    private void CompleteDamageAfterDying(DyingResolution dying)
    {
        var victim = _players[dying.VictimSeat];
        QueueGameEvent(new AfterDamageEvent(
            dying.DamageFrameId,
            dying.Attack.SourceSeat,
            dying.VictimSeat,
            dying.Attack.DamageAmount,
            Math.Max(0, victim.Hp),
            GetDamageNature(dying.Attack.EffectiveCardKind)));
        PopResolutionFrame(dying.DamageFrameId, ResolutionFrameKind.Damage);
        CompleteAttack(dying.Attack);
    }

    private void FinishAttack(AttackResolution attack)
    {
        var cardLocation = _cardZones.GetLocation(attack.Card.Id);
        if (cardLocation == CardLocation.Processing)
        {
            MoveCard(
                attack.Card,
                CardLocation.Processing,
                CardLocation.DiscardPile,
                CardMoveReasons.UseFinished);
        }
        else if (cardLocation.Zone != CardZoneKind.Hand)
        {
            throw new InvalidOperationException(
                $"A resolved {attack.EffectiveCardKind} left Processing through an unsupported destination: {cardLocation}.");
        }

        FinishCardUse(attack.ResolutionId, attack.Card, attack.EffectiveCardKind);

        if (_winner != Winner.None && _status != EngineStatus.Completed)
        {
            CompleteGame();
        }
    }

    private void CompleteAttack(AttackResolution attack)
    {
        if (_pendingDying is not null)
        {
            throw new InvalidOperationException("A card cannot finish while its dying resolution is pending.");
        }

        if (_pendingGroupCard is { Effect: GroupCardEffect.ResponseAttack } group)
        {
            if (!ReferenceEquals(group.CurrentAttack, attack))
            {
                throw new InvalidOperationException(
                    "The completed attack does not belong to the current group target.");
            }

            group.CurrentAttack = null;
            _pendingAttack = null;
            _pendingDuel = null;
            _pendingDecision = null;
            group.TargetIndex++;
            SetCardUseTargetIndex(group.ResolutionId, group.TargetIndex);
            BeginGroupAttackResponse(group);
            return;
        }

        FinishAttack(attack);
        _pendingAttack = null;
        _pendingDuel = null;
        _pendingDecision = null;
    }

    private void FinishGroupAttack(GroupCardResolution group)
    {
        if (!ReferenceEquals(_pendingGroupCard, group) || group.Effect != GroupCardEffect.ResponseAttack)
        {
            throw new InvalidOperationException("The group attack is not the current card resolution.");
        }

        if (_pendingDying is not null || _pendingAttack is not null)
        {
            throw new InvalidOperationException(
                "A group card cannot finish while a target continuation is pending.");
        }

        var cardLocation = _cardZones.GetLocation(group.Card.Id);
        if (cardLocation == CardLocation.Processing)
        {
            MoveCard(
                group.Card,
                CardLocation.Processing,
                CardLocation.DiscardPile,
                CardMoveReasons.UseFinished);
        }
        else if (cardLocation != CardLocation.DiscardPile &&
                 (cardLocation.Zone != CardZoneKind.Hand ||
                  cardLocation.OwnerSeat is not { } ownerSeat ||
                  !group.TargetSeats.Contains(ownerSeat)))
        {
            throw new InvalidOperationException(
                $"A resolved {group.Card.Kind} left Processing through an unsupported destination: {cardLocation}.");
        }

        SetCardUseTargetIndex(group.ResolutionId, group.TargetSeats.Count);
        FinishCardUse(group.ResolutionId, group.Card);
        _pendingGroupCard = null;
        _pendingAttack = null;
        _pendingDuel = null;
        _pendingDecision = null;

        if (_winner != Winner.None && _status != EngineStatus.Completed)
        {
            CompleteGame();
        }
    }

    private void FinishGroupRecovery(GroupCardResolution group)
    {
        if (!ReferenceEquals(_pendingGroupCard, group) || group.Effect != GroupCardEffect.Recovery)
        {
            throw new InvalidOperationException("The group recovery is not the current card resolution.");
        }

        if (_pendingDying is not null || _pendingAttack is not null)
        {
            throw new InvalidOperationException(
                "A group recovery cannot finish while another continuation is pending.");
        }

        var cardLocation = _cardZones.GetLocation(group.Card.Id);
        if (cardLocation == CardLocation.Processing)
        {
            MoveCard(
                group.Card,
                CardLocation.Processing,
                CardLocation.DiscardPile,
                CardMoveReasons.UseFinished);
        }
        else if (cardLocation != CardLocation.DiscardPile)
        {
            throw new InvalidOperationException(
                $"A resolved {group.Card.Kind} left Processing through an unsupported destination: {cardLocation}.");
        }

        SetCardUseTargetIndex(group.ResolutionId, group.TargetSeats.Count);
        FinishCardUse(group.ResolutionId, group.Card);
        _pendingGroupCard = null;
        _pendingAttack = null;
        _pendingDuel = null;
        _pendingDecision = null;

        if (_winner != Winner.None && _status != EngineStatus.Completed)
        {
            CompleteGame();
        }
    }

    private void ResolvePeach(PlayerRuntime player, Card peach)
    {
        ResolvePeach(player, player, peach, allowDying: false);
    }

    private void ResolvePeach(
        PlayerRuntime source,
        PlayerRuntime target,
        Card peach,
        bool allowDying)
    {
        if ((!allowDying && source.Hp >= source.MaxHp) ||
            (allowDying && target.Hp > 0) ||
            !target.IsAlive)
        {
            throw new InvalidOperationException("This Peach target cannot currently be recovered.");
        }


        ResolveRecoveryCard(source, target, peach, "桃");
    }

    private void ResolveDyingAlcohol(
        PlayerRuntime source,
        PlayerRuntime target,
        Card alcohol)
    {
        if (source.Seat != target.Seat || target.Hp > 0 || !target.IsAlive)
        {
            throw new InvalidOperationException("Alcohol can only rescue its dying holder.");
        }

        ResolveRecoveryCard(source, target, alcohol, "酒");
    }

    private void ResolveRecoveryCard(
        PlayerRuntime source,
        PlayerRuntime target,
        Card card,
        string cardName)
    {
        var resolutionId = BeginCardUse(card, source.Seat, [target.Seat]);
        MoveCard(
            card,
            CardLocation.Hand(source.Seat),
            CardLocation.Processing,
            CardMoveReasons.Use);
        SetCardUseStep(resolutionId, ResolutionFrameStep.ResolvingEffect);
        var recoveryFrameId = BeginRecovery(resolutionId, source.Seat, target.Seat, 1);
        try
        {
            target.Hp = Math.Min(target.MaxHp, target.Hp + 1);
            AddLog("Recovered", $"{source.Name} 使用【{cardName}】使 {target.Name} 回复至 {target.Hp}/{target.MaxHp} 点体力。", source.Seat, target.Seat);
            QueueGameEvent(new RecoveryAppliedEvent(source.Seat, target.Seat, 1, target.Hp));
        }
        finally
        {
            PopResolutionFrame(recoveryFrameId, ResolutionFrameKind.Recovery);
        }

        MoveCard(
            card,
            CardLocation.Processing,
            CardLocation.DiscardPile,
            CardMoveReasons.UseFinished);
        FinishCardUse(resolutionId, card);
    }
    private long BeginCardUse(
        Card card,
        int sourceSeat,
        IReadOnlyList<int> targetSeats,
        CardKind? playedCardKind = null)
    {
        var resolutionId = ++_resolutionSequence;
        var targets = Array.AsReadOnly(targetSeats.ToArray());
        var effectiveCardKind = playedCardKind ?? card.Kind;
        _resolutionStack.Add(new CardUseFrame(
            resolutionId,
            sourceSeat,
            card.Id,
            effectiveCardKind,
            targets));
        QueueGameEvent(new CardUseDeclaredEvent(resolutionId, card.Id, effectiveCardKind, sourceSeat));
        QueueGameEvent(new TargetsConfirmedEvent(resolutionId, targets));
        return resolutionId;
    }

    private void PushResponseWindow(
        long parentFrameId,
        int sourceSeat,
        int responderSeat,
        CardKind incomingCard,
        CardKind requiredCardKind)
    {
        SetCardUseStep(parentFrameId, ResolutionFrameStep.AwaitingResponse);
        _resolutionStack.Add(new ResponseWindowFrame(
            ++_resolutionSequence,
            parentFrameId,
            sourceSeat,
            responderSeat,
            incomingCard,
            RequiredCardKind: requiredCardKind));
    }

    private long BeginDamage(
        long parentFrameId,
        int sourceSeat,
        int targetSeat,
        int amount,
        DamageNature nature = DamageNature.Normal)
    {
        var frameId = ++_resolutionSequence;
        _resolutionStack.Add(new DamageFrame(
            frameId,
            parentFrameId,
            sourceSeat,
            targetSeat,
            amount,
            Nature: nature));
        return frameId;
    }

    private long BeginRecovery(
        long parentFrameId,
        int sourceSeat,
        int targetSeat,
        int amount)
    {
        var frameId = ++_resolutionSequence;
        _resolutionStack.Add(new RecoveryFrame(
            frameId,
            parentFrameId,
            sourceSeat,
            targetSeat,
            amount));
        return frameId;
    }

    private long BeginDeath(long parentFrameId, int victimSeat, int? killerSeat)
    {
        var frameId = ++_resolutionSequence;
        _resolutionStack.Add(new DeathFrame(frameId, parentFrameId, victimSeat, killerSeat));
        return frameId;
    }

    private void SetDyingFrameStep(long frameId, ResolutionFrameStep step)
    {
        var index = _resolutionStack.FindLastIndex(frame => frame.Id == frameId);
        if (index < 0 || _resolutionStack[index] is not DyingFrame dying)
        {
            throw new InvalidOperationException($"Resolution frame {frameId} is not a Dying frame.");
        }

        var pendingDying = _pendingDying;
        var responderIndex = pendingDying is { FrameId: var pendingFrameId } && pendingFrameId == frameId
            ? pendingDying.ResponderIndex
            : dying.ResponderIndex;
        _resolutionStack[index] = dying with
        {
            ResponderIndex = responderIndex,
            Step = step
        };
    }

    private void SetDamageSkillFrameStep(long frameId, ResolutionFrameStep step)
    {
        var index = _resolutionStack.FindLastIndex(frame => frame.Id == frameId);
        if (index < 0 || _resolutionStack[index] is not DamageSkillFrame damageSkill)
        {
            throw new InvalidOperationException($"Resolution frame {frameId} is not a DamageSkill frame.");
        }

        _resolutionStack[index] = damageSkill with { Step = step };
    }

    private void SetDamageTriggerWindowStep(long frameId, ResolutionFrameStep step)
    {
        var index = _resolutionStack.FindLastIndex(frame => frame.Id == frameId);
        if (index < 0 || _resolutionStack[index] is not DamageTriggerWindowFrame triggerWindow)
        {
            throw new InvalidOperationException($"Resolution frame {frameId} is not a damage trigger window.");
        }

        _resolutionStack[index] = triggerWindow with { Step = step };
    }

    private void SetDamageTriggerWindowCursor(long frameId, int candidateIndex)
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

        _resolutionStack[index] = triggerWindow with { CandidateIndex = candidateIndex };
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

        _resolutionStack[index] = cardUse with { Step = step };
    }

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

        _resolutionStack[index] = cardUse with { TargetIndex = targetIndex };
    }

    private void FinishCardUse(
        long frameId,
        Card card,
        CardKind? playedCardKind = null)
    {
        SetCardUseStep(frameId, ResolutionFrameStep.Completed);
        if (_resolutionStack.Count == 0 ||
            _resolutionStack[^1] is not CardUseFrame cardUse ||
            cardUse.Id != frameId)
        {
            throw new InvalidOperationException(
                $"Resolution frame {frameId} has unfinished child frames.");
        }

        QueueGameEvent(new CardUseFinishedEvent(
            frameId,
            card.Id,
            playedCardKind ?? card.Kind));
        PopResolutionFrame(frameId, ResolutionFrameKind.CardUse);
    }

    private void PopResolutionFrame(long frameId, ResolutionFrameKind expectedKind)
    {
        if (_resolutionStack.Count == 0)
        {
            throw new InvalidOperationException(
                $"Resolution frame {frameId} is not the top {expectedKind} frame.");
        }

        var top = _resolutionStack[^1];
        if (top.Id != frameId || top.Kind != expectedKind)
        {
            throw new InvalidOperationException(
                $"Resolution frame {frameId} is not the top {expectedKind} frame.");
        }

        _resolutionStack.RemoveAt(_resolutionStack.Count - 1);
    }

    private int GetSlashLimit(
        PlayerRuntime actor,
        IPassiveSkill skill,
        PlayerSkillContext skillContext)
    {
        var limit = skill.ModifySlashLimit(skillContext, 1);
        foreach (var equipment in GetEquipment(actor))
        {
            var bonus = EquipmentCatalog.Get(equipment.Kind).SlashLimitBonus;
            if (bonus == int.MaxValue)
            {
                return int.MaxValue;
            }

            limit = Math.Min(int.MaxValue, limit + bonus);
        }

        return limit;
    }

    private bool CanUseSlashTarget(PlayerRuntime source, PlayerRuntime target) =>
        target.IsAlive &&
        target.Seat != source.Seat &&
        GetCombatDistance(source.Seat, target.Seat) <= GetAttackRange(source.Seat) &&
        !IsSlashProhibited(target);

    private IReadOnlyList<LegalAction> BuildLegalActions(PlayerRuntime actor)
    {
        var actions = new List<LegalAction>();
        if (!actor.IsAlive || _phase != TurnPhase.Play || actor.Seat != _currentSeat)
        {
            return actions;
        }

        var skill = SkillRegistry.Get(actor.General.Skill);
        var skillContext = CreateSkillContext(actor);
        var slashLimit = GetSlashLimit(actor, skill, skillContext);
        if (_slashCountThisTurn < slashLimit)
        {
            foreach (var slash in GetHand(actor).Where(card => IsSlashCard(card.Kind)))
            {
                var slashName = CardCatalog.Get(slash.Kind).DisplayName;
                foreach (var target in _players.Where(player =>
                             CanUseSlashTarget(actor, player)))
                {
                    actions.Add(new LegalAction(
                        LegalActionKind.Slash,
                        slash.Id,
                        target.Seat,
                        $"对 {target.Name} 使用【{slashName}】"));
                }
            }

            foreach (var converted in GetHand(actor).Where(card =>
                         skill.CanUseAsSlash(skillContext, card)))
            {
                var physicalName = CardCatalog.Get(converted.Kind).DisplayName;
                foreach (var target in _players.Where(player =>
                             CanUseSlashTarget(actor, player)))
                {
                    actions.Add(new LegalAction(
                        LegalActionKind.Slash,
                        converted.Id,
                        target.Seat,
                        $"将【{physicalName}】当作【杀】对 {target.Name} 使用",
                        PlayedCardKind: CardKind.Slash));
                }
            }
        }

        if (actor.Hp < actor.MaxHp)
        {
            foreach (var peach in GetHand(actor).Where(card => card.Kind == CardKind.Peach))
            {
                actions.Add(new LegalAction(
                    LegalActionKind.Peach,
                    peach.Id,
                    actor.Seat,
                    "对自己使用【桃】"));
            }
        }

        foreach (var duel in GetHand(actor).Where(card => card.Kind == CardKind.Duel))
        {
            foreach (var target in _players.Where(player =>
                         player.IsAlive &&
                         player.Seat != actor.Seat))
            {
                actions.Add(new LegalAction(
                    LegalActionKind.Duel,
                    duel.Id,
                    target.Seat,
                    $"对 {target.Name} 使用【决斗】"));
            }
        }

        foreach (var drawTwo in GetHand(actor).Where(card => card.Kind == CardKind.DrawTwo))
        {
            actions.Add(new LegalAction(
                LegalActionKind.DrawTwo,
                drawTwo.Id,
                null,
                "使用【无中生有】摸两张牌"));
        }

        foreach (var assault in GetHand(actor).Where(card => card.Kind == CardKind.BarbarianAssault))
        {
            actions.Add(new LegalAction(
                LegalActionKind.BarbarianAssault,
                assault.Id,
                null,
                "使用【南蛮入侵】"));
        }

        foreach (var arrowBarrage in GetHand(actor).Where(card => card.Kind == CardKind.ArrowBarrage))
        {
            actions.Add(new LegalAction(
                LegalActionKind.ArrowBarrage,
                arrowBarrage.Id,
                null,
                "使用【万箭齐发】"));
        }

        foreach (var peachGarden in GetHand(actor).Where(card => card.Kind == CardKind.PeachGarden))
        {
            actions.Add(new LegalAction(
                LegalActionKind.PeachGarden,
                peachGarden.Id,
                null,
                "使用【桃园结义】"));
        }

        foreach (var fiveGrains in GetHand(actor).Where(card => card.Kind == CardKind.FiveGrains))
        {
            actions.Add(new LegalAction(
                LegalActionKind.FiveGrains,
                fiveGrains.Id,
                null,
                "使用【五谷丰登】"));
        }

        if (!actor.HasAlcoholEffect)
        {
            foreach (var alcohol in GetHand(actor).Where(card => card.Kind == CardKind.Alcohol))
            {
                actions.Add(new LegalAction(
                    LegalActionKind.Alcohol,
                    alcohol.Id,
                    null,
                    "使用【酒】，本回合下一张杀伤害+1"));
            }
        }

        foreach (var equipment in GetHand(actor).Where(card => EquipmentCatalog.IsEquipment(card.Kind)))
        {
            var definition = EquipmentCatalog.Get(equipment.Kind);
            actions.Add(new LegalAction(
                LegalActionKind.Equip,
                equipment.Id,
                null,
                $"装备【{definition.DisplayName}】至{EquipmentCatalog.GetSlotName(definition.Slot)}槽"));
        }

        foreach (var dismantlement in GetHand(actor).Where(card => card.Kind == CardKind.Dismantlement))
        {
            foreach (var target in _players.Where(player =>
                         player.IsAlive &&
                         player.Seat != actor.Seat &&
                         GetHand(player).Count > 0))
            {
                actions.Add(new LegalAction(
                    LegalActionKind.Dismantlement,
                    dismantlement.Id,
                    target.Seat,
                    $"对 {target.Name} 使用【过河拆桥】"));
            }
        }

        foreach (var snatch in GetHand(actor).Where(card => card.Kind == CardKind.Snatch))
        {
            foreach (var target in _players.Where(player =>
                         player.IsAlive &&
                         player.Seat != actor.Seat &&
                         GetCombatDistance(actor.Seat, player.Seat) == 1 &&
                         GetHand(player).Count > 0))
            {
                actions.Add(new LegalAction(
                    LegalActionKind.Snatch,
                    snatch.Id,
                    target.Seat,
                    $"对 {target.Name} 使用【顺手牵羊】"));
            }
        }

        foreach (var fireAttack in GetHand(actor).Where(card => card.Kind == CardKind.FireAttack))
        {
            foreach (var target in _players.Where(player =>
                         player.IsAlive &&
                         player.Seat != actor.Seat &&
                         GetHand(player).Count > 0))
            {
                actions.Add(new LegalAction(
                    LegalActionKind.FireAttack,
                    fireAttack.Id,
                    target.Seat,
                    $"对 {target.Name} 使用【火攻】"));
            }
        }

        actions.Add(new LegalAction(LegalActionKind.EndPlay, null, null, "结束出牌"));
        return actions;
    }

    private static LegalAction? SelectPlayAction(
        IReadOnlyList<LegalAction> legalActions,
        Card card,
        IReadOnlyList<int> targets,
        CardKind? playedCardKind)
    {
        var matching = legalActions
            .Where(candidate =>
                candidate.TargetSeat.HasValue
                    ? targets.Count == 1 && targets[0] == candidate.TargetSeat.Value
                    : targets.Count == 0)
            .ToArray();
        if (playedCardKind is { } requestedKind)
        {
            return matching.FirstOrDefault(candidate =>
                (candidate.PlayedCardKind ?? card.Kind) == requestedKind);
        }

        // Compatibility callers do not carry an action discriminator. Prefer a
        // native use of the physical card when one exists, then fall back to a
        // conversion such as Wusheng's red-card Slash.
        return matching.FirstOrDefault(candidate => candidate.PlayedCardKind is null) ??
               matching.FirstOrDefault();
    }

    private static bool IsSlashCard(CardKind kind) =>
        kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash;

    private IReadOnlyList<Card> GetResponseCards(
        PlayerRuntime responder,
        CardKind requiredCardKind)
    {
        var skill = SkillRegistry.Get(responder.General.Skill);
        var context = CreateSkillContext(responder);
        return GetHand(responder)
            .Where(card =>
                MatchesRequiredCard(card.Kind, requiredCardKind) ||
                skill.CanUseAsResponse(context, card, requiredCardKind))
            .ToArray();
    }

    private CardKind GetEffectiveResponseKind(
        PlayerRuntime responder,
        Card responseCard,
        CardKind requiredCardKind)
    {
        if (MatchesRequiredCard(responseCard.Kind, requiredCardKind))
        {
            return requiredCardKind;
        }

        var skill = SkillRegistry.Get(responder.General.Skill);
        return skill.CanUseAsResponse(
            CreateSkillContext(responder),
            responseCard,
            requiredCardKind)
            ? requiredCardKind
            : throw new InvalidOperationException(
                $"The response card {responseCard.Id} cannot be used as {requiredCardKind}.");
    }

    private static bool IsNativeResponseCard(Card responseCard, CardKind requiredCardKind) =>
        MatchesRequiredCard(responseCard.Kind, requiredCardKind);

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
        CardKind.ThunderSlash => DamageNature.Thunder,
        _ => DamageNature.Normal
    };

    private static string GetDamageNatureLabel(DamageNature nature) => nature switch
    {
        DamageNature.Fire => "火焰",
        DamageNature.Thunder => "雷电",
        _ => string.Empty
    };

    private bool IsSlashProhibited(PlayerRuntime target)
    {
        var skill = SkillRegistry.Get(target.General.Skill);
        return skill.ProhibitsSlashTarget(CreateSkillContext(target));
    }

    private void RequestHumanPlay()
    {
        var legal = BuildLegalActions(_players[_currentSeat]);
        _pendingDecision = new PendingDecision(
            DecisionKind.PlayCard,
            _currentSeat,
            "请选择一张牌和目标，或结束出牌阶段。",
            legal.Where(action => action.CardId.HasValue).Select(action => action.CardId!.Value).Distinct().ToArray(),
            legal.Where(action => action.TargetSeat.HasValue).Select(action => action.TargetSeat!.Value).Distinct().ToArray())
        {
            PromptId = CreatePromptId(),
            Choices = CreatePlayChoices(legal)
        };
        _status = EngineStatus.AwaitingHumanPlay;
        PublishState();
    }

    private PromptId CreatePromptId() => new(++_nextPromptId);

    private static IReadOnlyList<PromptChoice> CreatePlayChoices(
        IReadOnlyList<LegalAction> legalActions)
    {
        var choices = new List<PromptChoice>(legalActions.Count);
        foreach (var action in legalActions)
        {
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
            var target = action.TargetSeat;
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
                _ => throw new InvalidOperationException($"Unsupported prompt action {action.Kind}.")
            };
            var choiceId = target is { } targetSeat
                ? $"play.{actionName}.card-{cardId}.target-{targetSeat}"
                : $"play.{actionName}.card-{cardId}";

            var parameters = new Dictionary<string, string>
            {
                ["action"] = actionName
            };
            if (action.PlayedCardKind is { } playedCardKind)
            {
                parameters["played-card-kind"] = playedCardKind.ToString();
            }

            choices.Add(new PromptChoice(
                new ChoiceId(choiceId),
                action.Description,
                [cardId],
                target is { } ? [target.Value] : [],
                parameters));
        }

        return choices;
    }

    private static IReadOnlyList<PromptChoice> CreateResponseChoices(
        IReadOnlyList<Card> responseCards,
        CardKind requiredCardKind,
        CardKind? incomingCard = null,
        Func<Card, CardKind>? effectiveCardKindSelector = null)
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
            .Select(card =>
            {
                var effectiveCardKind = effectiveCardKindSelector?.Invoke(card) ?? requiredCardKind;
                var cardDescription = IsNativeResponseCard(card, requiredCardKind)
                    ? $"打出【{responseName}】"
                    : $"将【{card.DisplayName}】当作【{responseName}】";
                var description = isDodge
                    ? isGroupAttack
                        ? $"{cardDescription}，响应【{incomingName}】。"
                        : $"{cardDescription}，抵消这次{incomingAttackName}。"
                    : isGroupAttack
                        ? $"{cardDescription}，响应【{incomingName}】。"
                        : $"{cardDescription}，继续应战【决斗】。";
                return new PromptChoice(
                    new ChoiceId($"respond.{responseAction}.card-{card.Id}"),
                    description,
                    [card.Id],
                    [],
                    new Dictionary<string, string>
                    {
                        ["response"] = responseAction,
                        ["required-card"] = responseName,
                        ["response-card-kind"] = effectiveCardKind.ToString()
                    });
            })
            .ToList();
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

    private void BeginDiscardPhase()
    {
        _phase = TurnPhase.Discard;
        _status = EngineStatus.Running;
        AddLog("PhaseChanged", $"{_players[_currentSeat].Name} 进入弃牌阶段。", _currentSeat);
        QueueGameEvent(new PhaseChangedEvent(_phase, _currentSeat));
    }

    private void AutoDiscard(PlayerRuntime player)
    {
        var hand = GetHand(player);
        var handLimit = Math.Max(0, player.Hp);
        var count = Math.Max(0, hand.Count - handLimit);
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

    private static int GetKeepValue(Card card, PlayerRuntime owner)
    {
        var value = CardCatalog.Get(card.Kind).HandKeepValue;
        return card.Kind == CardKind.Peach && owner.Hp < owner.MaxHp
            ? value + 50
            : value;
    }

    private void EndTurn()
    {
        var previous = _players[_currentSeat];
        if (previous.HasAlcoholEffect)
        {
            previous.HasAlcoholEffect = false;
            AddLog("EffectExpired", $"{previous.Name} 的酒效在回合结束时失效。", previous.Seat);
            QueueGameEvent(new AlcoholExpiredEvent(previous.Seat));
        }

        _phase = TurnPhase.Finished;
        AddLog("TurnEnded", $"{previous.Name} 的回合结束。", previous.Seat);
        QueueGameEvent(new TurnEndedEvent(_turnNumber, previous.Seat));
        _currentSeat = FindNextAliveSeat(_currentSeat);
        _phase = TurnPhase.NotStarted;
        PublishState();
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

    private void FinalizePlayerDeath(DyingResolution dying)
    {
        var victim = _players[dying.VictimSeat];
        var killer = dying.KillerSeat is { } killerSeat
            ? _players[killerSeat]
            : null;
        if (!victim.IsAlive)
        {
            return;
        }

        var deathFrameId = BeginDeath(dying.FrameId, victim.Seat, killer?.Seat);
        try
        {
            victim.Hp = 0;
            victim.IsAlive = false;
            victim.RoleRevealed = true;
            QueueGameEvent(new RoleRevealedEvent(victim.Seat, victim.Role));
            MoveCards(
                GetHand(victim).ToArray(),
                CardLocation.Hand(victim.Seat),
                CardLocation.DiscardPile,
                CardMoveReasons.DeathDiscard);
            MoveCards(
                GetEquipment(victim).ToArray(),
                CardLocation.Equipment(victim.Seat),
                CardLocation.DiscardPile,
                CardMoveReasons.DeathEquipmentDiscard);

            AddLog("PlayerDied", $"{victim.Name} 阵亡，身份是【{GetRoleName(victim.Role)}】。", killer?.Seat, victim.Seat);
            QueueGameEvent(new PlayerDiedEvent(victim.Seat, killer?.Seat));
            NotifyAiOfDeath(killer, victim);

            if (killer is { IsAlive: true })
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
            _winner = GameRules.EvaluateWinner(_players.Select(player =>
                new PlayerLifeState(player.Role, player.IsAlive)));
            if (_winner != previousWinner && _winner != Winner.None)
            {
                QueueGameEvent(new WinnerDeterminedEvent(_winner));
            }
        }
        finally
        {
            PopResolutionFrame(deathFrameId, ResolutionFrameKind.Death);
        }
    }

    private void CompleteGame()
    {
        foreach (var player in _players)
        {
            player.RoleRevealed = true;
        }

        _phase = TurnPhase.Finished;
        _status = EngineStatus.Completed;
        _pendingDecision = null;
        AddLog("GameEnded", $"游戏结束：{GetWinnerName(_winner)}获胜。 ");
        QueueGameEvent(new GameEndedEvent(_winner));
    }

    private void EndAsDraw(string reason)
    {
        if (_pendingAttack is not null ||
            _pendingGroupCard is not null ||
            _pendingFireAttack is not null ||
            _cardZones.Count(CardLocation.Processing) != 0)
        {
            throw new InvalidOperationException("A game cannot end as a draw during an active card resolution.");
        }

        _winner = Winner.Draw;
        foreach (var player in _players)
        {
            player.RoleRevealed = true;
        }

        _phase = TurnPhase.Finished;
        _status = EngineStatus.Completed;
        _pendingDecision = null;
        AddLog("GameEnded", $"{reason}，本局记为平局。");
        QueueGameEvent(new WinnerDeterminedEvent(_winner));
        QueueGameEvent(new GameEndedEvent(_winner));
        PublishState();
    }

    private void DrawCards(
        PlayerRuntime player,
        int count,
        bool log,
        CardMoveReason? reason = null)
    {
        var drawn = 0;
        for (var i = 0; i < count; i++)
        {
            var card = DrawOne(player, reason ?? CardMoveReasons.Draw);
            if (card is null)
            {
                break;
            }

            drawn++;
        }

        if (log && drawn > 0)
        {
            AddLog("CardsDrawn", $"{player.Name} 摸了 {drawn} 张牌。", player.Seat);
        }
    }

    private Card? DrawOne(PlayerRuntime player, CardMoveReason reason)
    {
        if (!EnsureDrawPile())
        {
            return null;
        }

        var drawPile = _cardZones.CardsAt(CardLocation.DrawPile);
        var card = drawPile[^1];
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

    private void NotifyAiOfSlash(PlayerRuntime source, PlayerRuntime target)
    {
        var lordSeat = _players.Single(player => player.Role == Role.Lord).Seat;
        var visibleTargetRole = target.RoleRevealed || target.Role == Role.Lord
            ? target.Role
            : (Role?)null;
        foreach (var brain in _aiBrains.Values)
        {
            brain.ObserveSlash(source.Seat, target.Seat, lordSeat, visibleTargetRole);
        }
    }

    private void NotifyAiOfDuel(PlayerRuntime source, PlayerRuntime target)
    {
        var lordSeat = _players.Single(player => player.Role == Role.Lord).Seat;
        var visibleTargetRole = target.RoleRevealed || target.Role == Role.Lord
            ? target.Role
            : (Role?)null;
        foreach (var brain in _aiBrains.Values)
        {
            brain.ObserveDuel(source.Seat, target.Seat, lordSeat, visibleTargetRole);
        }
    }

    private void NotifyAiOfGroupAttack(PlayerRuntime source, IReadOnlyList<int> targets)
    {
        var lordSeat = _players.Single(player => player.Role == Role.Lord).Seat;
        foreach (var targetSeat in targets)
        {
            var target = _players[targetSeat];
            var visibleTargetRole = target.RoleRevealed || target.Role == Role.Lord
                ? target.Role
                : (Role?)null;
            foreach (var brain in _aiBrains.Values)
            {
                brain.ObserveGroupAttack(source.Seat, target.Seat, lordSeat, visibleTargetRole);
            }
        }
    }

    private void NotifyAiOfDeath(PlayerRuntime? killer, PlayerRuntime victim)
    {
        foreach (var brain in _aiBrains.Values)
        {
            brain.ObserveDeath(killer?.Seat, victim.Role);
        }
    }

    private IReadOnlyList<Card> GetHand(PlayerRuntime player) =>
        _cardZones.CardsAt(CardLocation.Hand(player.Seat));

    private IReadOnlyList<Card> GetEquipment(PlayerRuntime player) =>
        GetEquipment(player.Seat);

    private IReadOnlyList<Card> GetEquipment(int seat) =>
        _cardZones.CardsAt(CardLocation.Equipment(seat));

    private PlayerSkillContext CreateSkillContext(PlayerRuntime player) =>
        new(player.Seat, player.Hp, player.MaxHp, GetHand(player).Count, _phase);

    private void MoveCard(
        Card card,
        CardLocation from,
        CardLocation to,
        CardMoveReason reason)
    {
        _cardZones.Move(card.Id, from, to);
        RecordMovement(card, from, to, reason);
    }

    private void MoveCards(
        IReadOnlyList<Card> cards,
        CardLocation from,
        CardLocation to,
        CardMoveReason reason)
    {
        var moved = _cardZones.MoveMany(cards.Select(card => card.Id), from, to);
        foreach (var card in moved)
        {
            RecordMovement(card, from, to, reason);
        }
    }

    private void MoveAllCards(
        CardLocation from,
        CardLocation to,
        CardMoveReason reason)
    {
        var cards = _cardZones.MoveAll(from, to);
        foreach (var card in cards)
        {
            RecordMovement(card, from, to, reason);
        }
    }

    private void RecordMovement(
        Card card,
        CardLocation from,
        CardLocation to,
        CardMoveReason reason)
    {
        var movement = new CardMovementRecord(
            ++_movementSequence,
            _turnNumber,
            card.Id,
            card.Kind,
            from,
            to,
            reason);
        _cardMovements.Add(movement);
        if (_started)
        {
            _pendingNotifications.Enqueue(new CardMovedNotification(movement));
            QueueGameEvent(new CardMovedEvent(card.Id, card.Kind, from, to, reason));
        }
    }

    private void AssertCoreInvariants()
    {
        _cardZones.AssertInvariants(_initialCardCount);

        foreach (var player in _players)
        {
            var equipment = GetEquipment(player);
            if (equipment.Any(card => !EquipmentCatalog.IsEquipment(card.Kind)))
            {
                throw new InvalidOperationException(
                    $"Player {player.Seat} equipment zone contains a non-equipment card.");
            }

            if (equipment
                .Select(card => EquipmentCatalog.Get(card.Kind).Slot)
                .Distinct()
                .Count() != equipment.Count)
            {
                throw new InvalidOperationException(
                    $"Player {player.Seat} has more than one card in an equipment slot.");
            }
        }

        var processing = _cardZones.CardsAt(CardLocation.Processing);
        var hasActiveCardResolution = _pendingAttack is not null ||
            _pendingGroupCard is not null ||
            _pendingFireAttack is not null;
        if (!hasActiveCardResolution && processing.Count != 0)
        {
            throw new InvalidOperationException("Processing contains cards without an active resolution.");
        }

        if (_pendingAttack is { } attack &&
            !IsActiveAttackCardConsistent(attack, processing))
        {
            throw new InvalidOperationException("The active card resolution and Processing zone are inconsistent.");
        }

        if (_pendingGroupCard is { Effect: GroupCardEffect.Recovery } recoveryGroup &&
            (processing.Count != 1 || processing[0].Id != recoveryGroup.Card.Id))
        {
            throw new InvalidOperationException(
                "The active group recovery and Processing zone are inconsistent.");
        }

        if (_pendingGroupCard is { Effect: GroupCardEffect.PublicDraft } publicDraft &&
            (processing.Count != 1 + publicDraft.RevealedCardIds.Count ||
             processing.All(card => card.Id != publicDraft.Card.Id) ||
             publicDraft.RevealedCardIds.Any(cardId => processing.All(card => card.Id != cardId))))
        {
            throw new InvalidOperationException(
                "The active FiveGrains draft and Processing zone are inconsistent.");
        }

        if (_pendingFireAttack is { } fireAttack)
        {
            var expectedProcessingCount = fireAttack.RevealedCardId is null ? 1 : 2;
            if (processing.Count != expectedProcessingCount ||
                processing.All(card => card.Id != fireAttack.Card.Id) ||
                (fireAttack.RevealedCardId is { } revealedId &&
                 processing.All(card => card.Id != revealedId)))
            {
                throw new InvalidOperationException(
                    "The active FireAttack and Processing zone are inconsistent.");
            }

            if (_pendingAttack is not null ||
                _pendingDuel is not null ||
                _pendingGroupCard is not null ||
                _pendingDying is not null ||
                _pendingDamageTrigger is not null ||
                _pendingDamageSkill is not null)
            {
                throw new InvalidOperationException(
                    "A FireAttack selection cannot coexist with another card continuation.");
            }

            if (_resolutionStack.LastOrDefault() is not CardUseFrame cardUse ||
                cardUse.Id != fireAttack.ResolutionId ||
                cardUse.CardKind != fireAttack.Card.Kind)
            {
                throw new InvalidOperationException(
                    "A FireAttack selection must retain its CardUse frame as the stack top.");
            }

            var expectedKind = fireAttack.RevealedCardId is null
                ? DecisionKind.FireAttackReveal
                : DecisionKind.FireAttackDiscard;
            if (_pendingDecision is not { } fireDecision ||
                fireDecision.Kind != expectedKind ||
                fireDecision.PlayerSeat != (expectedKind == DecisionKind.FireAttackReveal
                    ? fireAttack.TargetSeat
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

        if (!_setupComplete && _resolutionStack.Count != 0)
        {
            throw new InvalidOperationException("Setup cannot retain an in-flight card resolution.");
        }

        if (!hasActiveCardResolution && _resolutionStack.Count != 0)
        {
            throw new InvalidOperationException("A completed card resolution left frames on the stack.");
        }

        if (_pendingAttack is { } pendingAttack)
        {
            if (!_resolutionStack.Any(frame =>
                    frame is CardUseFrame cardUse && cardUse.Id == pendingAttack.ResolutionId))
            {
                throw new InvalidOperationException(
                    "An active Slash continuation has no parent CardUse frame.");
            }

            if (_pendingDying is null &&
                _pendingDamageTrigger is null &&
                _pendingDamageSkill is null)
            {
                if (_resolutionStack.LastOrDefault() is not ResponseWindowFrame response ||
                    response.ParentFrameId != pendingAttack.ResolutionId)
                {
                    throw new InvalidOperationException(
                        "An active Slash must retain its response window as the stack top.");
                }
            }
            else if (_pendingDamageSkill is { } damageSkill)
            {
                if (_pendingDamageTrigger is not { } triggerWindow)
                {
                    throw new InvalidOperationException(
                        "A damage-skill continuation must retain its trigger window.");
                }

                if (!ReferenceEquals(damageSkill.Attack, pendingAttack) ||
                    _resolutionStack.LastOrDefault() is not DamageSkillFrame frame ||
                    frame.Id != damageSkill.FrameId ||
                    frame.ParentFrameId != triggerWindow.FrameId)
                {
                    throw new InvalidOperationException(
                        "A damage-skill continuation must retain its DamageSkill frame as the stack top.");
                }
            }
            else if (_pendingDamageTrigger is { } triggerContinuation)
            {
                if (!ReferenceEquals(triggerContinuation.Attack, pendingAttack) ||
                    _resolutionStack.LastOrDefault() is not DamageTriggerWindowFrame frame ||
                    frame.Id != triggerContinuation.FrameId ||
                    frame.ParentFrameId != triggerContinuation.DamageFrameId ||
                    frame.CandidateIndex != triggerContinuation.CandidateIndex)
                {
                    throw new InvalidOperationException(
                        "An active damage trigger window must retain its ordered cursor as the stack top.");
                }
            }
            else if (_pendingDying is { } dyingContinuation)
            {
                if (!ReferenceEquals(dyingContinuation.Attack, pendingAttack))
                {
                    throw new InvalidOperationException(
                        "The dying continuation does not belong to the active Slash.");
                }

                if (_resolutionStack.LastOrDefault() is not DyingFrame dying ||
                    dying.Id != dyingContinuation.FrameId ||
                    dying.ParentFrameId != dyingContinuation.DamageFrameId)
                {
                    throw new InvalidOperationException(
                        "An active dying continuation must retain its Dying frame as the stack top.");
                }
            }
            else
            {
                throw new InvalidOperationException(
                    "An active Slash continuation has an unsupported pending state.");
            }
        }

        if (_pendingDying is not null && _pendingAttack is null)
        {
            throw new InvalidOperationException("A dying continuation must retain its active card resolution.");
        }

        if (_pendingDamageSkill is not null && _pendingAttack is null)
        {
            throw new InvalidOperationException(
                "A damage-skill continuation must retain its active card resolution.");
        }

        if (_pendingDamageTrigger is not null && _pendingAttack is null)
        {
            throw new InvalidOperationException(
                "A damage trigger window must retain its active card resolution.");
        }

        if (_pendingDying is not null && _pendingDamageSkill is not null)
        {
            throw new InvalidOperationException(
                "A damage-skill continuation cannot coexist with a dying continuation.");
        }

        if (_pendingDying is not null && _pendingDamageTrigger is not null)
        {
            throw new InvalidOperationException(
                "A damage trigger window cannot coexist with a dying continuation.");
        }

        if (_pendingDamageTrigger is { } pendingDamageTrigger)
        {
            if (_resolutionStack.All(frame => frame.Id != pendingDamageTrigger.FrameId) ||
                pendingDamageTrigger.CandidateIndex < 0 ||
                pendingDamageTrigger.CandidateIndex > pendingDamageTrigger.Candidates.Count)
            {
                throw new InvalidOperationException(
                    "A damage trigger continuation must retain a valid trigger window frame.");
            }

            var expectedTop = _pendingDamageSkill is null
                ? _resolutionStack.LastOrDefault() is DamageTriggerWindowFrame triggerFrame &&
                  triggerFrame.Id == pendingDamageTrigger.FrameId &&
                  triggerFrame.CandidateIndex == pendingDamageTrigger.CandidateIndex
                : _resolutionStack.LastOrDefault() is DamageSkillFrame damageSkillFrame &&
                  damageSkillFrame.ParentFrameId == pendingDamageTrigger.FrameId;
            if (!expectedTop)
            {
                throw new InvalidOperationException(
                    "A damage trigger continuation must retain its window or skill frame at the stack top.");
            }

            if (_pendingDamageSkill is null && _status != EngineStatus.Running)
            {
                throw new InvalidOperationException(
                    "An automatic damage trigger cursor must remain in the running state.");
            }
        }

        if (_pendingDamageSkill is { } pendingDamageSkill)
        {
            if (_pendingDamageTrigger is not { } damageTrigger ||
                damageTrigger.FrameId != _resolutionStack.OfType<DamageSkillFrame>().LastOrDefault()?.ParentFrameId)
            {
                throw new InvalidOperationException(
                    "A damage-skill continuation must retain its parent trigger window.");
            }

            if (_pendingDecision is not { Kind: DecisionKind.Feedback or DecisionKind.Yiji or DecisionKind.Jieming or DecisionKind.Yuanhu } damageSkillDecision ||
                damageSkillDecision.PlayerSeat != pendingDamageSkill.OwnerSeat)
            {
                throw new InvalidOperationException(
                    "A damage-skill continuation must retain its private skill prompt.");
            }

            var expectedStatus = _players[pendingDamageSkill.OwnerSeat].IsHuman
                ? EngineStatus.AwaitingHumanResponse
                : EngineStatus.Running;
            if (_status != expectedStatus)
            {
                throw new InvalidOperationException(
                    "A damage-skill prompt status does not match its owner.");
            }
        }

        if (_pendingDuel is not null &&
            (_pendingAttack is null || !ReferenceEquals(_pendingDuel.Attack, _pendingAttack)))
        {
            throw new InvalidOperationException("A Duel continuation must retain its active card resolution.");
        }

        if (_pendingGroupCard is { Effect: GroupCardEffect.ResponseAttack } group &&
            (_pendingAttack is null ||
             !ReferenceEquals(group.CurrentAttack, _pendingAttack)))
        {
            throw new InvalidOperationException(
                "A group continuation must retain its current target attack.");
        }

        if (_pendingGroupCard is { Effect: GroupCardEffect.Recovery } recovery &&
            (_pendingAttack is not null || recovery.CurrentAttack is not null))
        {
            throw new InvalidOperationException(
                "A group recovery cannot retain an attack continuation.");
        }

        if (_pendingGroupCard is { Effect: GroupCardEffect.PublicDraft } draft)
        {
            if (_pendingAttack is not null || draft.CurrentAttack is not null)
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
            if (_pendingDecision is not { Kind: DecisionKind.SelectHarvestCard } harvestDecision ||
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

        if (_pendingDecision?.Kind == DecisionKind.SelectHarvestCard &&
            _pendingGroupCard is not { Effect: GroupCardEffect.PublicDraft })
        {
            throw new InvalidOperationException(
                "A harvest selection cannot exist without a public draft.");
        }

        if (_pendingDecision?.Kind is DecisionKind.FireAttackReveal or DecisionKind.FireAttackDiscard &&
            _pendingFireAttack is null)
        {
            throw new InvalidOperationException(
                "A FireAttack selection cannot exist without a FireAttack resolution.");
        }

        if (_pendingDecision?.Kind is DecisionKind.Feedback or DecisionKind.Yiji or DecisionKind.Jieming or DecisionKind.Yuanhu &&
            _pendingDamageSkill is null)
        {
            throw new InvalidOperationException(
                "A damage-skill prompt cannot exist without a damage-skill continuation.");
        }

        var awaitingHumanResponse =
            (_pendingDecision?.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Feedback or DecisionKind.Yiji or DecisionKind.Jieming or DecisionKind.Yuanhu) &&
            _status == EngineStatus.AwaitingHumanResponse;
        var awaitingHumanDying =
            _pendingDecision?.Kind == DecisionKind.RescueDying &&
            _status == EngineStatus.AwaitingHumanDying;
        var awaitingAiResponse = IsAiResponsePending();
        var awaitingAiDamageSkill = IsAiDamageSkillPending();
        if (_pendingAttack is null &&
            (awaitingHumanResponse ||
             awaitingHumanDying ||
             awaitingAiResponse ||
             awaitingAiDamageSkill))
        {
            throw new InvalidOperationException("A response continuation exists without an active Slash.");
        }

        if (_pendingDying is null &&
            _pendingDamageTrigger is null &&
            _pendingDamageSkill is null &&
            _pendingAttack is not null &&
            awaitingHumanResponse == awaitingAiResponse)
        {
            throw new InvalidOperationException("An active card resolution must have exactly one response continuation.");
        }

        if (_status == EngineStatus.Completed &&
            (_pendingDecision is not null ||
             _pendingAttack is not null ||
             _pendingDuel is not null ||
             _pendingGroupCard is not null ||
             _pendingFireAttack is not null ||
             _pendingDying is not null ||
             _pendingDamageTrigger is not null ||
             _pendingDamageSkill is not null ||
             processing.Count != 0))
        {
            throw new InvalidOperationException("A completed game cannot retain pending resolution state.");
        }
    }

    private bool IsActiveAttackCardConsistent(
        AttackResolution attack,
        IReadOnlyList<Card> processing)
    {
        if (processing.Count == 1 && processing[0].Id == attack.Card.Id)
        {
            return true;
        }

        if (_pendingDamageTrigger is not null)
        {
            var triggerLocation = _cardZones.GetLocation(attack.Card.Id);
            return triggerLocation.Zone == CardZoneKind.Hand;
        }

        if (_pendingGroupCard is not { Effect: GroupCardEffect.ResponseAttack } group ||
            processing.Count != 0)
        {
            return false;
        }

        var location = _cardZones.GetLocation(attack.Card.Id);
        return location.Zone == CardZoneKind.Hand &&
               location.OwnerSeat is { } ownerSeat &&
               group.TargetSeats.Contains(ownerSeat);
    }

    private bool IsHumanDecisionPending() =>
        _pendingDecision is { } decision && decision.PlayerSeat == _options.HumanSeat;

    private bool IsAiResponsePending() =>
        (_pendingDecision?.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash) &&
        _pendingDecision.PlayerSeat != _options.HumanSeat;

    private bool IsAiDamageSkillPending() =>
        _pendingDamageSkill is { } damageSkill &&
        _pendingDecision is { Kind: DecisionKind.Feedback or DecisionKind.Yiji or DecisionKind.Jieming or DecisionKind.Yuanhu } damageSkillDecision &&
        damageSkillDecision.PlayerSeat == damageSkill.OwnerSeat &&
        !_players[damageSkill.OwnerSeat].IsHuman;

    private bool IsAiHarvestPending() =>
        _pendingGroupCard is { Effect: GroupCardEffect.PublicDraft } &&
        _pendingDecision?.Kind == DecisionKind.SelectHarvestCard &&
        _pendingDecision.PlayerSeat != _options.HumanSeat;

    private bool IsAiFireAttackPending() =>
        _pendingFireAttack is not null &&
        _pendingDecision is
        {
            Kind: DecisionKind.FireAttackReveal or DecisionKind.FireAttackDiscard,
            PlayerSeat: var playerSeat
        } &&
        playerSeat != _options.HumanSeat;

    private bool IsAiDyingResponsePending() =>
        _pendingDying is { } dying &&
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

    private EngineRunResult ExecuteExclusive(Func<EngineRunResult> operation)
    {
        if (_isExecutingPublicOperation)
        {
            throw new InvalidOperationException(
                "GameEngine cannot be advanced reentrantly from a synchronous event handler.");
        }

        _isExecutingPublicOperation = true;
        try
        {
            operation();
            AssertCoreInvariants();
            _revision++;
            RefreshPendingDecisionRevision();
            if (_pendingStateSnapshot is not null)
            {
                _pendingStateSnapshot = State;
            }

            CommitPendingEvents();
            FlushNotifications();
            return BuildResult();
        }
        catch
        {
            _pendingNotifications.Clear();
            _pendingEvents.Clear();
            _pendingStateSnapshot = null;
            throw;
        }
        finally
        {
            _isExecutingPublicOperation = false;
        }
    }

    private EngineRunResult BuildResult()
    {
        var state = State;
        return new EngineRunResult(_status, _winner, state, state.PendingDecision, _revision);
    }

    private void RefreshPendingDecisionRevision()
    {
        if (_pendingDecision is { } pending)
        {
            _pendingDecision = pending with { Revision = _revision };
        }
    }

    private void PublishState() => _pendingStateSnapshot = State;

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

    private void CommitPendingEvents()
    {
        foreach (var payload in _pendingEvents)
        {
            var envelope = new EventEnvelope(
                new EventId(++_eventSequence),
                ParentId: null,
                _eventSequence,
                _revision,
                $"revision-{_revision}",
                payload);
            _events.Add(envelope);
            _pendingNotifications.Enqueue(new EventNotification(envelope));
        }

        _pendingEvents.Clear();
    }

    private void FlushNotifications()
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

        if (_pendingStateSnapshot is { } snapshot)
        {
            _pendingStateSnapshot = null;
            InvokeObservers(StateChanged, snapshot, nameof(StateChanged));
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

    private static PendingDecision CloneDecision(PendingDecision decision) =>
        decision with
        {
            ValidCardIds = Array.AsReadOnly(decision.ValidCardIds.ToArray()),
            ValidTargetSeats = Array.AsReadOnly(decision.ValidTargetSeats.ToArray()),
            Choices = Array.AsReadOnly(decision.Choices.Select(CloneChoice).ToArray()),
            ValidContentIds = Array.AsReadOnly(decision.ValidContentIds.ToArray())
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
                TargetSeats = play.TargetSeats.ToArray()
            },
            _ => command
        };

    private static GeneralDefinition CreateHiddenGeneral() =>
        new(
            "",
            "未知武将",
            "",
            SkillKind.None,
            "未知",
            "武将尚未公开。");

    private static string GetRoleName(Role role) => role switch
    {
        Role.Lord => "主公",
        Role.Loyalist => "忠臣",
        Role.Rebel => "反贼",
        Role.Renegade => "内奸",
        _ => role.ToString()
    };

    private static string GetWinnerName(Winner winner) => winner switch
    {
        Winner.LordAndLoyalists => "主公与忠臣",
        Winner.Rebels => "反贼",
        Winner.Renegade => "内奸",
        Winner.Draw => "无人",
        _ => "尚未决出"
    };

    private sealed class PlayerRuntime
    {
        public required int Seat { get; init; }
        public required string Name { get; init; }
        public required bool IsHuman { get; init; }
        public required Role Role { get; init; }
        public required bool RoleRevealed { get; set; }
        public required GeneralDefinition General { get; set; }
        public required bool GeneralSelected { get; set; }
        public required bool GeneralRevealed { get; set; }
        public required int MaxHp { get; init; }
        public required int Hp { get; set; }
        public bool IsAlive { get; set; } = true;
        public bool HasAlcoholEffect { get; set; }
    }

    private sealed class AttackResolution(
        long resolutionId,
        int sourceSeat,
        int targetSeat,
        Card card,
        int damageAmount = 1,
        CardKind? playedCardKind = null)
    {
        public long ResolutionId { get; } = resolutionId;
        public int SourceSeat { get; } = sourceSeat;
        public int TargetSeat { get; } = targetSeat;
        public Card Card { get; } = card;
        public int DamageAmount { get; } = damageAmount;
        public CardKind EffectiveCardKind { get; } = playedCardKind ?? card.Kind;
    }

    private sealed class FireAttackResolution(
        long resolutionId,
        int sourceSeat,
        int targetSeat,
        Card card)
    {
        public long ResolutionId { get; } = resolutionId;
        public int SourceSeat { get; } = sourceSeat;
        public int TargetSeat { get; } = targetSeat;
        public Card Card { get; } = card;
        public int? RevealedCardId { get; set; }
    }

    private sealed class DuelResolution(AttackResolution attack)
    {
        public AttackResolution Attack { get; } = attack;
        public long ResolutionId => Attack.ResolutionId;
        public int SourceSeat => Attack.SourceSeat;
        public int TargetSeat => Attack.TargetSeat;
        public Card Card => Attack.Card;
        public int ResponderSeat { get; set; } = attack.TargetSeat;
        public int OpponentSeat => ResponderSeat == SourceSeat ? TargetSeat : SourceSeat;
    }

    private enum GroupCardEffect
    {
        ResponseAttack,
        Recovery,
        PublicDraft
    }

    private enum TargetCardEffect
    {
        Discard,
        Take
    }

    private sealed class GroupCardResolution(
        long resolutionId,
        int sourceSeat,
        Card card,
        IReadOnlyList<int> targetSeats,
        GroupCardEffect effect,
        CardKind? requiredCardKind)
    {
        public long ResolutionId { get; } = resolutionId;
        public int SourceSeat { get; } = sourceSeat;
        public Card Card { get; } = card;
        public IReadOnlyList<int> TargetSeats { get; } = targetSeats;
        public GroupCardEffect Effect { get; } = effect;
        public CardKind? RequiredCardKind { get; } = requiredCardKind;
        public int TargetIndex { get; set; }
        public AttackResolution? CurrentAttack { get; set; }
        public List<int> RevealedCardIds { get; } = [];
    }

    private sealed class DyingResolution(
        long frameId,
        long damageFrameId,
        AttackResolution attack,
        int victimSeat,
        int? killerSeat,
        IReadOnlyList<int> responderSeats)
    {
        public long FrameId { get; } = frameId;
        public long DamageFrameId { get; } = damageFrameId;
        public AttackResolution Attack { get; } = attack;
        public int VictimSeat { get; } = victimSeat;
        public int? KillerSeat { get; } = killerSeat;
        public IReadOnlyList<int> ResponderSeats { get; } = responderSeats;
        public int ResponderIndex { get; set; }
        public int ResponderSeat => ResponderSeats[ResponderIndex];
    }

    private sealed class DamageSkillResolution(
        long frameId,
        long triggerFrameId,
        long damageFrameId,
        AttackResolution attack,
        int ownerSeat,
        SkillKind skill,
        CardKind effectiveCardKind,
        string candidateId,
        int priority,
        DamageSkillEffectKind effect,
        IReadOnlyList<int> effectCardIds)
    {
        public long FrameId { get; } = frameId;
        public long TriggerFrameId { get; } = triggerFrameId;
        public long DamageFrameId { get; } = damageFrameId;
        public AttackResolution Attack { get; } = attack;
        public int OwnerSeat { get; } = ownerSeat;
        public int SourceSeat => Attack.SourceSeat;
        public Card Card => Attack.Card;
        public SkillKind Skill { get; } = skill;
        public CardKind EffectiveCardKind { get; } = effectiveCardKind;
        public string CandidateId { get; } = candidateId;
        public int Priority { get; } = priority;
        public DamageSkillEffectKind Effect { get; } = effect;
        public IReadOnlyList<int> EffectCardIds { get; } = effectCardIds;
    }

    private sealed class DamageTriggerResolution(
        long frameId,
        long damageFrameId,
        AttackResolution attack,
        IReadOnlyList<DamageTriggerCandidate> candidates)
    {
        public long FrameId { get; } = frameId;
        public long DamageFrameId { get; } = damageFrameId;
        public AttackResolution Attack { get; } = attack;
        public IReadOnlyList<DamageTriggerCandidate> Candidates { get; } = candidates;
        public int CandidateIndex { get; set; }
    }

    private abstract record EngineNotification;

    private sealed record LogNotification(GameLogEntry Entry) : EngineNotification;

    private sealed record AiThoughtNotification(AiThoughtRecord Thought) : EngineNotification;

    private sealed record AiGeneralThoughtNotification(AiGeneralThought Thought) : EngineNotification;

    private sealed record CardMovedNotification(CardMovementRecord Movement) : EngineNotification;

    private sealed record EventNotification(EventEnvelope Event) : EngineNotification;
}
