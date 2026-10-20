namespace CardGame.Core;

// Selection/payment facts are read on demand. No player or zone store crosses
// this boundary, and modules never retain a suspended payment outside its frame.
internal interface IProgramPaidCardFacts : IProgramSelectionQuery
{
    int PlayerCount { get; }
    IReadOnlyList<int> Seats { get; }
    int Seat(int seat);
    bool IsValidSeat(int seat);
    bool IsAlive(int seat);
    bool IsHuman(int seat);
    string Name(int seat);
    bool EquipmentAreaAbolished(int seat);
    bool JudgmentAreaAbolished(int seat);
    int EquipmentSlotCapacity(int seat, EquipmentSlot slot);
    bool HasSkillInstance(int seat, string skillId, string instanceId);
    ContentSkillDefinition Skill(string skillId);
    PlayerSkillContext SkillContext(int seat);
    IReadOnlyList<Card> Hand(int seat);
    IReadOnlyList<Card> Equipment(int seat);
    IReadOnlyList<Card> Judgment(int seat);
    IReadOnlyList<Card> CardsAt(CardLocation location);
    CardLocation Location(int cardId);
    CardSnapshot Snapshot(Card card);
    int ProviderSeat(ProgramSkillFrame frame);
    SkillProgramEffect OwnedSelectionEffect(ProgramSkillFrame frame);
    bool ForeignDiscardPrevented(int chooser, Card card, CardLocation source, OwnedCardMoveIntent intent);
    bool SelfDiscardForbidden(int chooser, Card card, CardLocation source, OwnedCardMoveIntent intent);
    bool IsActiveSourceEquipment(int owner, string skillId, string instanceId, Card card);
    bool IsZixiJudgment(int cardId);
    bool CanTransferZixiJudgment(Card card, int destinationSeat);
    bool HasCardPolicy(int seat, SkillProgramCardPolicyKind policy);
    CardKind JudgmentKind(Card card);
    bool IsDelayedCard(CardKind kind);
    bool HasJudgmentEffectiveCard(int seat, CardKind kind);
    Suit EffectiveSuit(int seat, Card card);
    int CountLivingInAttackRange(int seat);
    IReadOnlyList<int> TargetSeats(int owner, SkillProgramTargetKind kind, ProgramSkillWindowContext? context);
    bool CanSelectPindianOpponent(ProgramSkillFrame frame, int seat);
    Card[] ClaimableDamageCards(ProgramSkillFrame frame);
    bool FrozenSuitMatchesChoice(ProgramSkillFrame frame, string cardBind, string choiceBind);
    bool BoundCardsMatchCategories(ProgramSkillFrame frame, string bind, IReadOnlyList<SkillProgramCardCategory> categories);
}

internal interface IProgramPaidCardOperations : IProgramRuntimeOperations
{
    void Cancel(ProgramSkillFrame frame, string reason);
    void SetCardSet(long frameId, string bind, IReadOnlyList<int> ids, SkillProgramCardSetVisibility visibility,
        IReadOnlyList<CardLocation>? locations = null, Suit? frozenSuit = null);
    NativeLogicalDiscardInvocationDraft? CaptureLogicalDiscard(int initiator, int executor, IReadOnlyList<int> ids,
        CardMoveReason reason, long? parentFrameId = null);
    void CompleteLogicalDiscard(NativeLogicalDiscardInvocationDraft? invocation);
    void Move(Card card, CardLocation source, CardLocation destination, CardMoveReason reason,
        CardMovementDisclosure disclosure = CardMovementDisclosure.Unspecified);
    bool BeginMovementWindow();
    void ReturnMovement(long frameId);
    void Log(string type, string message, int? actor = null);
}

[ProgramRuntimeModule]
internal sealed partial class ProgramPaidCardRuntime
{
    private readonly IProgramPaidCardFacts _facts;
    private readonly IProgramPaidCardOperations _operations;
    private readonly IProgramRuntimeFrames _frames;
    private readonly IProgramRuntimePrompts _prompts;
    private readonly IProgramRuntimeEvents _events;

    internal ProgramPaidCardRuntime(IProgramPaidCardFacts facts, IProgramPaidCardOperations operations,
        IProgramRuntimeFrames frames, IProgramRuntimePrompts prompts, IProgramRuntimeEvents events)
    {
        _facts = facts;
        _operations = operations;
        _frames = frames;
        _prompts = prompts;
        _events = events;
    }

    private static ProgramSkillCardSetBinding CardSet(ProgramSkillFrame frame, string bind) =>
        frame.CardSetBindings.SingleOrDefault(binding => binding.Name == bind) ??
        throw new InvalidOperationException($"Program card-set binding '{bind}' is unavailable.");
}

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class ProgramPaidCardFacts(GameEngine engine) : IProgramPaidCardFacts
    {
        public int PlayerCount => engine._playerCount;
        public IReadOnlyList<int> Seats => Array.AsReadOnly(engine._players.Select(player => player.Seat).ToArray());
        public int Seat(int seat) => engine._players[seat].Seat;
        public bool IsValidSeat(int seat) => engine.IsValidPlayerSeat(seat);
        public bool IsAlive(int seat) => engine._players[seat].IsAlive;
        public bool IsHuman(int seat) => engine._players[seat].IsHuman;
        public string Name(int seat) => engine._players[seat].Name;
        public bool EquipmentAreaAbolished(int seat) => engine._players[seat].EquipmentAreaAbolished;
        public bool JudgmentAreaAbolished(int seat) => engine._players[seat].JudgmentAreaAbolished;
        public int EquipmentSlotCapacity(int seat, EquipmentSlot slot) => engine._players[seat].EquipmentSlotCapacity(slot);
        public bool HasSkillInstance(int seat, string skillId, string instanceId) =>
            engine.HasRuntimeSkillInstance(engine._players[seat], skillId, instanceId);
        public ContentSkillDefinition Skill(string skillId) => engine._contentRegistry!.GetSkill(skillId);
        public PlayerSkillContext SkillContext(int seat) => engine.CreateSkillContext(engine._players[seat]);
        public IReadOnlyList<Card> Hand(int seat) => Array.AsReadOnly(engine.GetHand(engine._players[seat]).ToArray());
        public IReadOnlyList<Card> Equipment(int seat) => Array.AsReadOnly(engine.GetEquipment(engine._players[seat]).ToArray());
        public IReadOnlyList<Card> Judgment(int seat) => Array.AsReadOnly(engine.GetJudgment(engine._players[seat]).ToArray());
        public IReadOnlyList<Card> CardsAt(CardLocation location) => Array.AsReadOnly(engine._cardZones.CardsAt(location).ToArray());
        public CardLocation Location(int cardId) => engine._cardZones.GetLocation(cardId);
        public CardSnapshot Snapshot(Card card) => engine.ToSnapshot(card);
        public int ProviderSeat(ProgramSkillFrame frame) => engine.GuanParentAction(frame).ProviderSeat;
        public SkillProgramEffect OwnedSelectionEffect(ProgramSkillFrame frame) => engine.GetOwnedSelectionEffect(frame);
        public bool ForeignDiscardPrevented(int chooser, Card card, CardLocation source, OwnedCardMoveIntent intent) =>
            engine.IsForeignEquipmentDiscardPrevented(chooser, card, source, intent);
        public bool SelfDiscardForbidden(int chooser, Card card, CardLocation source, OwnedCardMoveIntent intent) =>
            engine.IsSelfHandCategoryDiscardForbidden(chooser, card, source, intent);
        public bool IsActiveSourceEquipment(int owner, string skillId, string instanceId, Card card) =>
            engine.IsActiveProgramSourceEquipmentCard(owner, skillId, instanceId, card);
        public bool IsZixiJudgment(int cardId) => engine.IsZixiQinJudgmentCard(cardId);
        public bool CanTransferZixiJudgment(Card card, int destinationSeat) => engine.CanTransferZixiQinJudgmentCard(card, destinationSeat);
        public bool HasCardPolicy(int seat, SkillProgramCardPolicyKind policy) => engine.HasCardPolicy(engine._players[seat], policy);
        public CardKind JudgmentKind(Card card) => engine.GetJudgmentEffectiveCardKind(card);
        public bool IsDelayedCard(CardKind kind) => GameEngine.IsDelayedCard(kind);
        public bool HasJudgmentEffectiveCard(int seat, CardKind kind) => engine.HasJudgmentEffectiveCard(engine._players[seat], kind);
        public Suit EffectiveSuit(int seat, Card card) => engine.EffectiveSuit(engine._players[seat], card);
        public int CountLivingInAttackRange(int seat) => engine.CountLivingInAttackRange(seat);
        public IReadOnlyList<int> TargetSeats(int owner, SkillProgramTargetKind kind, ProgramSkillWindowContext? context) =>
            engine.GetProgramTargetSeats(owner, kind, context);
        public bool CanSelectPindianOpponent(ProgramSkillFrame frame, int seat) => engine.CanSelectProgramPindianOpponent(frame, seat);
        public Card[] ClaimableDamageCards(ProgramSkillFrame frame) => engine.GetClaimableProgramDamageCards(frame);
        public bool FrozenSuitMatchesChoice(ProgramSkillFrame frame, string cardBind, string choiceBind) =>
            engine.DoesProgramFrozenSuitMatchChoice(frame, cardBind, choiceBind);
        public bool BoundCardsMatchCategories(ProgramSkillFrame frame, string bind, IReadOnlyList<SkillProgramCardCategory> categories) =>
            engine.DoProgramBoundCardsMatchCategories(frame, bind, categories);
    }

    [ProgramCapability]
    private sealed class ProgramPaidCardOperations(GameEngine engine) : IProgramPaidCardOperations
    {
        public void Cancel(ProgramSkillFrame frame, string reason) => engine.CancelProgramBindingAndCleanup(frame, reason);
        public void SetCardSet(long frameId, string bind, IReadOnlyList<int> ids, SkillProgramCardSetVisibility visibility,
            IReadOnlyList<CardLocation>? locations = null, Suit? frozenSuit = null) =>
            engine.SetProgramCardSet(frameId, bind, ids, visibility, locations, frozenSuit);
        public NativeLogicalDiscardInvocationDraft? CaptureLogicalDiscard(int initiator, int executor, IReadOnlyList<int> ids,
            CardMoveReason reason, long? parentFrameId = null) =>
            engine.CaptureNativeLogicalDiscardInvocation(initiator, executor, ids, reason, parentFrameId);
        public void CompleteLogicalDiscard(NativeLogicalDiscardInvocationDraft? invocation) =>
            engine.CompleteNativeLogicalDiscardInvocation(invocation);
        public void Move(Card card, CardLocation source, CardLocation destination, CardMoveReason reason,
            CardMovementDisclosure disclosure = CardMovementDisclosure.Unspecified) =>
            engine.MoveCard(card, source, destination, reason, disclosure: disclosure);
        public bool BeginMovementWindow() => engine.TryBeginCardsMovedProgramWindow();
        public void ReturnMovement(long frameId) => engine.ReturnRuntimeProgramMovement(frameId);
        public void Log(string type, string message, int? actor = null) => engine.AddLog(type, message, actor);
    }
}
