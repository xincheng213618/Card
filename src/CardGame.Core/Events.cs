using System.Text.Json.Serialization;

namespace CardGame.Core;

public readonly record struct EventId(long Value)
{
    public bool IsValid => Value > 0;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Typed event payloads emitted by the trusted host event projection.</summary>
public interface IGameEvent;

public sealed record GameStartedEvent(int PlayerCount, string ModeId = "") : IGameEvent;

public sealed record SetupStartedEvent(int PlayerCount, string ModeId) : IGameEvent;

public sealed record GeneralSelectionRequestedEvent(
    int ActorSeat,
    IReadOnlyList<string> CandidateIds) : IGameEvent;

public sealed record GeneralSelectedEvent(int ActorSeat, string GeneralId) : IGameEvent;

public sealed record GodFactionSelectionRequestedEvent(
    int ActorSeat,
    IReadOnlyList<string> FactionIds) : IGameEvent;

public sealed record GodFactionSelectedEvent(int ActorSeat, string FactionId) : IGameEvent;

/// <summary>
/// 左慈化身：一次公开的化身牌亮出与技能声明。仅亮出的化身牌与技能进入事件流，
/// 其余化身牌与牌堆内容保持私密。
/// </summary>
public sealed record HuaShenAvatarRevealedEvent(
    long? ProgramFrameId,
    string SkillId,
    int OwnerSeat,
    string GeneralId,
    string GeneralName,
    string DeclaredSkillId,
    string DeclaredSkillName) : IGameEvent;

/// <summary>
/// 左慈新生：一张游戏外武将牌进入了化身牌堆。牌堆内容对其他角色私密，事件只
/// 发布拥有者与新牌堆规模。
/// </summary>
public sealed record HuaShenAvatarGainedEvent(
    long FrameId,
    string SkillId,
    string BindingId,
    int OwnerSeat,
    int PileCount) : IGameEvent;

/// <summary>Trusted-host setup event for the second general in national war.</summary>
public sealed record SecondaryGeneralSelectedEvent(int ActorSeat, string GeneralId) : IGameEvent;

public sealed record NationalGeneralRevealedEvent(
    int Seat,
    GeneralSelectionSlot Slot,
    string GeneralId) : IGameEvent;

public sealed record NationalFactionRevealedEvent(int Seat, string FactionId) : IGameEvent;

public sealed record SetupCompletedEvent(int PlayerCount, string ModeId) : IGameEvent;

/// <summary>Public team assignment emitted during setup for team modes.</summary>
public sealed record TeamAssignedEvent(int Seat, string TeamId) : IGameEvent;

public sealed record TurnStartedEvent(int TurnNumber, int ActorSeat) : IGameEvent;

public sealed record RoundStartedEvent(int RoundNumber, int ActorSeat) : IGameEvent;

public sealed record TurnEndedEvent(int TurnNumber, int ActorSeat) : IGameEvent;

public sealed record PhaseChangedEvent(TurnPhase Phase, int ActorSeat) : IGameEvent;

public sealed record MaximumHpChangedEvent(
    int PlayerSeat,
    int Delta,
    int MaximumHp,
    string SkillId) : IGameEvent;

public sealed record SkillsAcquiredEvent(
    int PlayerSeat,
    string SourceSkillId,
    IReadOnlyList<string> SkillIds) : IGameEvent;

public sealed record SkillAwakenedEvent(
    int PlayerSeat,
    string SkillId,
    int MaximumHp,
    IReadOnlyList<string> AcquiredSkillIds) : IGameEvent;

public sealed record NuzhanAppliedEvent(
    long FrameId,
    int SourceSeat,
    int PhysicalCardId,
    bool IgnoredSlashLimit,
    int DamageBonus) : IGameEvent;

/// <summary>Trusted-host record of an accepted hand-limit selection.</summary>
public sealed record HandLimitDiscardedEvent(int ActorSeat, IReadOnlyList<int> CardIds) : IGameEvent;

public sealed record CardUsedEvent(
    int CardId,
    CardKind CardKind,
    int SourceSeat,
    int TargetSeat,
    bool IgnoresArmor = false) : IGameEvent;

public sealed record GroupCardUsedEvent(
    long ResolutionId,
    int CardId,
    CardKind CardKind,
    int SourceSeat,
    IReadOnlyList<int> TargetSeats) : IGameEvent;

public sealed record CardsRevealedEvent(
    long ResolutionId,
    IReadOnlyList<CardSnapshot> Cards) : IGameEvent;

public sealed record HarvestCardSelectedEvent(
    long ResolutionId,
    int PlayerSeat,
    int CardId) : IGameEvent;

/// <summary>
/// Public effect notification for a target-card discard. Hand selections remain
/// redacted; public equipment and judgment selections carry their already-public
/// identity.
/// </summary>
public sealed record TargetCardDiscardedEvent(
    long ResolutionId,
    int SourceSeat,
    int TargetSeat,
    CardZoneKind FromZone,
    int? PublicCardId = null,
    CardKind? PublicCardKind = null) : IGameEvent;

/// <summary>
/// Public effect notification for a target-card transfer. Hand selections remain
/// redacted; public equipment and judgment selections carry their already-public
/// identity.
/// </summary>
public sealed record TargetCardTakenEvent(
    long ResolutionId,
    int SourceSeat,
    int TargetSeat,
    CardZoneKind FromZone,
    int? PublicCardId = null,
    CardKind? PublicCardKind = null) : IGameEvent;

/// <summary>
/// Publicly announces the card intentionally revealed by FireAttack. The
/// preceding card-id choice remains private to the target; the card becomes
/// public only after this event is committed.
/// </summary>
public sealed record FireAttackCardRevealedEvent(
    long ResolutionId,
    int SourceSeat,
    int TargetSeat,
    int CardId,
    CardKind CardKind,
    Suit Suit) : IGameEvent;

/// <summary>Trusted-host outcome for the bounded FireAttack resolution.</summary>
public sealed record FireAttackResolvedEvent(
    long ResolutionId,
    int SourceSeat,
    int TargetSeat,
    int RevealedCardId,
    Suit RevealedSuit,
    int? MatchingDiscardCardId,
    bool CausedDamage) : IGameEvent;

public sealed record CardUseDeclaredEvent(
    long ResolutionId,
    int CardId,
    CardKind CardKind,
    int SourceSeat,
    bool IgnoresArmor = false) : IGameEvent;

public sealed record TargetsConfirmedEvent(
    long ResolutionId,
    IReadOnlyList<int> TargetSeats) : IGameEvent;

public sealed record LiuliRedirectedEvent(
    long ResolutionId,
    int SourceSeat,
    int OriginalTargetSeat,
    int NewTargetSeat,
    int DiscardedCardId) : IGameEvent;

public sealed record CardUseFinishedEvent(
    long ResolutionId,
    int CardId,
    CardKind CardKind) : IGameEvent;

public sealed record LihuoSlashUsedEvent(
    long ResolutionId,
    int OwnerSeat,
    IReadOnlyList<int> PhysicalCardIds,
    IReadOnlyList<int> TargetSeats,
    bool ConvertedFromOrdinarySlash,
    bool AddedTarget) : IGameEvent;

public sealed record ProgramCardTargetCountAppliedEvent(
    long ResolutionId,
    int OwnerSeat,
    CardKind EffectiveCardKind,
    IReadOnlyList<int> TargetSeats,
    IReadOnlyList<string> ContributionSourceIds) : IGameEvent;

/// <summary>Public result of an optional draw-phase skill decision.</summary>

/// <summary>Public result of an optional skill caused by one equipment leaving its owner's area.</summary>
/// <summary>Public result of an optional phase-skip skill decision.</summary>

/// <summary>Result of one enabled, data-defined card policy choice.</summary>
public sealed record ProgramCardPolicyResolvedEvent(
    int OwnerSeat,
    string SkillId,
    string PolicyId,
    bool Applied) : IGameEvent;

/// <summary>
/// Public result of a draw-phase hand gain. Hidden card identities remain in
/// the private hand snapshots and redacted movement stream.
/// </summary>

/// <summary>
/// Public result of Guanxing. Card identities and order remain private to the
/// skill owner; observers receive only whether it was used and the partition
/// sizes.
/// </summary>
public sealed record GuanxingResolvedEvent(
    int SourceSeat,
    bool Used,
    int ViewedCount,
    int TopCount,
    int BottomCount) : IGameEvent;

/// <summary>Public result of one initial or repeated Luoshen choice.</summary>
public sealed record LuoshenChoiceResolvedEvent(
    int SourceSeat,
    bool Used,
    bool IsRepeat) : IGameEvent;

/// <summary>Public result of the attacker's optional Tieqi trigger.</summary>
public sealed record TieqiChoiceResolvedEvent(
    int SourceSeat,
    int TargetSeat,
    bool Used) : IGameEvent;

/// <summary>Public result of the attacker's eligible optional Liegong trigger.</summary>
public sealed record LiegongChoiceResolvedEvent(
    int SourceSeat,
    int TargetSeat,
    bool Used) : IGameEvent;

/// <summary>Public transition of a tagged conversion skill after it actually resolves.</summary>
public sealed record SkillConversionStateChangedEvent(
    int PlayerSeat,
    string SkillId,
    SkillPolarity PreviousState,
    SkillPolarity CurrentState) : IGameEvent;

/// <summary>Public turn-scoped rule that prevents one card user from targeting one player.</summary>
public sealed record CardTargetProhibitionAddedEvent(
    int SkillOwnerSeat,
    string SkillId,
    int SourceSeat,
    int TargetSeat,
    SkillUsageScope Scope) : IGameEvent;

/// <summary>Public audit record for one named skill usage window being consumed.</summary>
public sealed record SkillUsageConsumedEvent(
    int SkillOwnerSeat,
    string SkillId,
    string UsageId,
    SkillUsageScope Scope,
    int Count) : IGameEvent;

/// <summary>Public result of Han Dang establishing unlimited range for the turn.</summary>
public sealed record GongqiResolvedEvent(
    long ResolutionId,
    int OwnerSeat,
    int CostCardId,
    bool EquipmentCost,
    int? TargetSeat,
    int? DiscardedCardId) : IGameEvent;

/// <summary>Public frozen responder order for one limited Jiefan activation.</summary>
public sealed record JiefanStartedEvent(
    long ResolutionId,
    int OwnerSeat,
    int TargetSeat,
    IReadOnlyList<int> ResponderSeats) : IGameEvent;

/// <summary>Public result of one responder's mandatory Jiefan choice.</summary>
public sealed record JiefanChoiceResolvedEvent(
    long ResolutionId,
    int ResponderSeat,
    int TargetSeat,
    int? DiscardedWeaponCardId,
    IReadOnlyList<int> DrawnCardIds) : IGameEvent;

/// <summary>Public audit record for a skill being restored to its game-start state.</summary>
public sealed record SkillResetEvent(
    int SkillOwnerSeat,
    string SkillId,
    int PreviousUsageCount) : IGameEvent;

/// <summary>Public growth state of Mou Lu Meng's locked Hengye skill.</summary>
public sealed record HengyeGrowthChangedEvent(
    long DamageFrameId,
    int SourceSeat,
    int PreviousGrowth,
    int CurrentGrowth) : IGameEvent;

/// <summary>Public Yingbo branch selected from this round's same-name damage-card ledger.</summary>
public sealed record YingboCardModeEvent(
    long ResolutionId,
    int SourceSeat,
    CardKind CardKind,
    bool WasUsedEarlierThisRound,
    bool CannotBeRespondedTo,
    bool ConvertsDamageToFire,
    int DamageBonus) : IGameEvent;

public sealed record YingboDamageIncreasedEvent(
    long ResolutionId,
    int SourceSeat,
    int TargetSeat,
    CardKind CardKind,
    int OriginalAmount,
    int ModifiedAmount) : IGameEvent;

/// <summary>Public result of Cao Zhang's draw-phase Jiangchi branch.</summary>
public sealed record JiangchiResolvedEvent(
    int PlayerSeat,
    JiangchiMode Mode,
    int DrawCount) : IGameEvent;

/// <summary>Public resolution of Yingbo's optional post-resolution card transfer.</summary>
public sealed record YingboGiftResolvedEvent(
    long ResolutionId,
    int SourceSeat,
    int CardId,
    CardKind CardKind,
    int? TargetSeat) : IGameEvent;

/// <summary>Public result of one optional Juzhan side; hidden hand identities remain omitted.</summary>
public sealed record JuzhanResolvedEvent(
    long ResolutionId,
    int OwnerSeat,
    int SourceSeat,
    int? TargetSeat,
    SkillPolarity State,
    bool Used,
    CardZoneKind? ObtainedFromZone,
    int? PublicObtainedCardId) : IGameEvent;

public enum CardEffectSkipReason { TargetHandEmpty, PublicTargetMissing, SkillNullified }

/// <summary>A declared target no longer receives this card's effect.</summary>
public sealed record CardEffectSkippedEvent(
    long ResolutionId, int SourceSeat, int TargetSeat, CardKind CardKind,
    CardEffectSkipReason Reason) : IGameEvent;

/// <summary>Public placement of a delayed card in a target's judgment zone.</summary>
public sealed record DelayedCardPlacedEvent(
    long ResolutionId,
    int CardId,
    CardKind CardKind,
    int SourceSeat,
    int TargetSeat) : IGameEvent;

/// <summary>
/// Public result after a delayed card's judgment and phase transition.
/// From rules 11 onward, <see cref="JudgmentSucceeded"/> means the revealed
/// suit is the card's safe suit (Heart for Indulgence, Club for Supply
/// Shortage); skipped phase flags report whether the delayed effect applied.
/// Rules 1-10 retain their historical red/black event semantics.
/// </summary>
public sealed record DelayedCardResolvedEvent(
    long ResolutionId,
    int CardId,
    CardKind CardKind,
    int TargetSeat,
    int? JudgmentCardId,
    bool JudgmentSucceeded,
    bool SkippedPlayPhase) : IGameEvent
{
    /// <summary>Whether this delayed card skipped the target's Draw phase.</summary>
    public bool SkippedDrawPhase { get; init; }
}

/// <summary>
/// Public terminal result for Lightning. A miss transfers the public delayed
/// card to the next alive seat; a hit starts a typed three-point Thunder damage
/// continuation. The event never exposes hidden cards or deck order.
/// </summary>
public sealed record LightningResolvedEvent(
    long ResolutionId,
    int CardId,
    int JudgmentTargetSeat,
    int? JudgmentCardId,
    bool Hit,
    int? NextTargetSeat,
    int DamageAmount) : IGameEvent;

/// <summary>
/// Public equipment lifecycle result. Equipment cards are visible to every
/// player, so the physical ids are safe here; hidden hands remain redacted.
/// </summary>
public sealed record EquipmentChangedEvent(
    long ResolutionId,
    int PlayerSeat,
    EquipmentSlot Slot,
    int CardId,
    CardKind CardKind,
    int? ReplacedCardId = null) : IGameEvent;

/// <summary>
/// Public resolution record for an armor effect that makes an already declared
/// incoming card ineffective. The physical armor remains visible in equipment.
/// </summary>
public sealed record ArmorEffectAppliedEvent(
    long ResolutionId,
    CardKind ArmorCard,
    int SourceSeat,
    int TargetSeat,
    CardKind IncomingCard) : IGameEvent;

/// <summary>Public result for Yu Jin's locked Yizhong skill nullifying a black Slash.</summary>
public sealed record YizhongNullifiedEvent(
    long ResolutionId,
    int SourceSeat,
    int TargetSeat,
    CardKind IncomingCard) : IGameEvent;

/// <summary>Public result for a skill that prevents trick-card damage.</summary>
public sealed record WuyanDamagePreventedEvent(
    long ResolutionId,
    int SourceSeat,
    int TargetSeat,
    CardKind TrickCard,
    int PreventedAmount,
    int SkillOwnerSeat,
    string SkillId) : IGameEvent;

public sealed record ResponseRequestedEvent(
    int SourceSeat,
    int TargetSeat,
    CardKind IncomingCard,
    CardKind? RequiredCardKind = null) : IGameEvent;

/// <summary>
/// Public progress for a locked skill that requires consecutive responses.
/// Every counted response has already paid its own physical or equipment cost.
/// </summary>
public sealed record RequiredResponseProgressEvent(
    long ResolutionId,
    int SkillOwnerSeat,
    int ResponderSeat,
    CardKind IncomingCard,
    CardKind RequiredCardKind,
    int ResponseCount,
    int RequiredResponseCount) : IGameEvent;

/// <summary>
/// Publicly announces a trick-effect nullification opportunity. The responder's
/// private hand is exposed only through that responder's filtered prompt.
/// </summary>
public sealed record NullificationRequestedEvent(
    long ResolutionId,
    int EffectCardId,
    CardKind EffectCardKind,
    int SourceSeat,
    int ResponderSeat,
    bool EffectCurrentlyNullified,
    int ChainDepth) : IGameEvent;

/// <summary>Public result of one played Nullification card.</summary>
public sealed record NullificationRespondedEvent(
    long ResolutionId,
    int EffectCardId,
    CardKind EffectCardKind,
    int ResponderSeat,
    int NullificationCardId,
    bool EffectNullified,
    int ChainDepth) : IGameEvent;

/// <summary>Public terminal result of the layered trick-effect response window.</summary>
public sealed record NullificationResolvedEvent(
    long ResolutionId,
    int EffectCardId,
    CardKind EffectCardKind,
    bool EffectNullified,
    int ChainDepth) : IGameEvent;

/// <summary>
/// Public cursor notification for the source-side hidden-hand choice. It
/// carries the number of opaque slots only; the selected physical card stays
/// redacted until the normal target-card outcome event is projected.
/// </summary>
public sealed record TargetCardSelectionRequestedEvent(
    long ResolutionId,
    int SourceSeat,
    int TargetSeat,
    LegalActionKind ActionKind,
    int CandidateCount) : IGameEvent;

/// <summary>Public state change for one character's elemental-link marker.</summary>
public sealed record IronChainStateChangedEvent(
    long ResolutionId,
    int SourceSeat,
    int TargetSeat,
    bool IsChained) : IGameEvent;

/// <summary>Public completion event for the exact one/two-target IronChain choice.</summary>
public sealed record IronChainResolvedEvent(
    long ResolutionId,
    int SourceSeat,
    IReadOnlyList<int> TargetSeats) : IGameEvent;

/// <summary>The recast card is public; the newly drawn card identities remain private.</summary>
public sealed record CardRecastEvent(int ActorSeat, int CardId, CardKind CardKind, int DrawCount) : IGameEvent;

/// <summary>Publicly explains one propagated elemental damage step.</summary>
public sealed record ChainedDamagePropagatedEvent(
    long ResolutionId,
    int SourceSeat,
    int FromSeat,
    int TargetSeat,
    int Amount,
    DamageNature Nature) : IGameEvent;

/// <summary>Public lifecycle notification for a deterministic judgment.</summary>
public sealed record JudgmentRequestedEvent(
    long ResolutionId,
    long ParentResolutionId,
    int TargetSeat,
    string Reason,
    CardKind? SourceCard = null) : IGameEvent;

/// <summary>
/// Public result of a judgment. The revealed card is public by rule; nullable
/// fields represent an exhausted deck with no card available to judge. For
/// delayed cards in rules 11 onward, Succeeded identifies the card's safe suit
/// rather than whether its phase-skipping effect applied.
/// </summary>
public sealed record JudgmentResolvedEvent(
    long ResolutionId,
    long ParentResolutionId,
    int TargetSeat,
    string Reason,
    int? CardId,
    CardKind? CardKind,
    Suit? Suit,
    int? Rank,
    bool Succeeded) : IGameEvent;

/// <summary>
/// Publicly announces a private Guicai opportunity. The owner hand remains
/// visible only through that owner's filtered PendingDecision.
/// </summary>
public sealed record JudgmentReplacementRequestedEvent(
    long ResolutionId,
    long JudgmentResolutionId,
    int TargetSeat,
    int OwnerSeat,
    string Reason,
    int JudgmentCardId,
    CardKind JudgmentCardKind,
    Suit JudgmentSuit) : IGameEvent;

/// <summary>Trusted-host result of one Guicai replacement opportunity.</summary>
public sealed record JudgmentReplacementResolvedEvent(
    long ResolutionId,
    long JudgmentResolutionId,
    int TargetSeat,
    int OwnerSeat,
    string Reason,
    bool Used,
    int OldCardId,
    int? NewCardId,
    CardKind? NewCardKind,
    Suit? NewSuit,
    int? NewRank) : IGameEvent;

public sealed record CardRespondedEvent(
    int CardId,
    int ResponderSeat,
    int SourceSeat,
    CardKind? EffectiveCardKind = null) : IGameEvent;

public sealed record DamageAppliedEvent(
    int SourceSeat,
    int TargetSeat,
    int Amount,
    int RemainingHp,
    DamageNature Nature = DamageNature.Normal) : IGameEvent;

/// <summary>
/// Public, typed marker mutation. SkillOwnerSeat identifies the character
/// whose rule caused the change; PlayerSeat identifies the marker holder.
/// </summary>
public sealed record PlayerMarkerChangedEvent(
    long ResolutionId,
    int PlayerSeat,
    PlayerMarkerKind Marker,
    int Delta,
    int Count,
    int? SkillOwnerSeat,
    string Reason) : IGameEvent;

public sealed record DamageRequestedEvent(
    long ResolutionId,
    int SourceSeat,
    int TargetSeat,
    int Amount,
    CardKind? SourceCard,
    DamageNature Nature = DamageNature.Normal) : IGameEvent;

public sealed record AfterDamageEvent(
    long ResolutionId,
    int SourceSeat,
    int TargetSeat,
    int Amount,
    int RemainingHp,
    DamageNature Nature = DamageNature.Normal) : IGameEvent;

/// <summary>
/// Trusted-host lifecycle event for a frozen after-damage trigger window. The
/// candidate list is intentionally not part of a player snapshot because it
/// can reveal private skill ownership and ordering information.
/// </summary>
public sealed record DamageTriggerWindowOpenedEvent(
    long ResolutionId,
    long DamageFrameId,
    int SourceSeat,
    int TargetSeat,
    int? CardId,
    CardKind? CardKind,
    IReadOnlyList<DamageTriggerCandidate> Candidates) : IGameEvent;

/// <summary>Trusted-host cursor movement for an after-damage trigger window.</summary>
public sealed record DamageTriggerWindowAdvancedEvent(
    long ResolutionId,
    int CandidateIndex,
    bool Completed) : IGameEvent;

public sealed record ZhenlieResolvedEvent(
    long ResolutionId,
    int OwnerSeat,
    int SourceSeat,
    CardKind CardKind,
    bool Used,
    int RemainingHp,
    int? DiscardedCardId = null,
    CardZoneKind? DiscardedFromZone = null) : IGameEvent;

public sealed record MijiResolvedEvent(
    int OwnerSeat,
    bool Used,
    int LostHp,
    IReadOnlyList<int> DrawnCardIds,
    IReadOnlyList<int> GivenCardIds,
    IReadOnlyList<int> TargetSeats) : IGameEvent;

/// <summary>Classic Guan Ping paid one exact card after a Play-phase Slash was declared.</summary>
public sealed record LongyinResolvedEvent(
    long ResolutionId,
    int OwnerSeat,
    int SlashSourceSeat,
    bool Used,
    int? DiscardedCardId,
    CardKind? DiscardedCardKind,
    CardKind SlashKind,
    bool SlashWasRed,
    bool SlashCountRemoved,
    IReadOnlyList<int> DrawnCardIds) : IGameEvent;

public sealed record AlcoholAppliedEvent(
    long ResolutionId,
    int SourceSeat,
    int DamageBonus) : IGameEvent;

public sealed record AlcoholExpiredEvent(int PlayerSeat) : IGameEvent;

public sealed record RecoveryAppliedEvent(
    int SourceSeat,
    int TargetSeat,
    int Amount,
    int RemainingHp) : IGameEvent;

public sealed record PlayerDiedEvent(int VictimSeat, int? KillerSeat) : IGameEvent;

public sealed record PlayerDyingEvent(
    long ResolutionId,
    int VictimSeat,
    int? KillerSeat) : IGameEvent;

public sealed record DyingResolvedEvent(
    long ResolutionId,
    int VictimSeat,
    bool Survived) : IGameEvent;

public sealed record DyingResponseEvent(
    long ResolutionId,
    int ResponderSeat,
    bool UsedPeach,
    int? PeachCardId,
    bool UsedAlcohol = false,
    int? AlcoholCardId = null) : IGameEvent
{
    /// <summary>
    /// Physical kind of a card that was converted to Peach by a rescue skill.
    /// Null preserves the legacy event shape for a native Peach response.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CardKind? UsedPeachPhysicalCardKind { get; init; }
}

/// <summary>Public result of placing physical Slash cards on Cheng Pu as "醇".</summary>
public sealed record ChunlaoStoredEvent(
    int OwnerSeat,
    IReadOnlyList<int> CardIds) : IGameEvent;

/// <summary>
/// Public result of one Chunlao trigger in a dying occurrence. The victim is
/// treated as the user of a virtual Alcohol; the owner only pays the public
/// "醇" card.
/// </summary>
public sealed record ChunlaoRescueEvent(
    long DyingFrameId,
    int OwnerSeat,
    int VictimSeat,
    int ChunCardId,
    int RecoveredHp,
    int VictimHp) : IGameEvent;

/// <summary>A configured owner-pile card paid to make the dying victim use virtual Alcohol.</summary>
public sealed record ProgramDyingRescueEvent(
    long DyingFrameId,
    string SkillId,
    int OwnerSeat,
    int VictimSeat,
    int CardId,
    int RecoveredHp,
    int VictimHp) : IGameEvent;

public sealed record ProgramRecoveryPolicyAppliedEvent(
    long ResolutionId,
    int OwnerSeat,
    int ProviderSeat,
    int PeachCardId,
    int RecoveryAmount,
    string SkillId,
    string PolicyId) : IGameEvent;

public sealed record DuelResponseEvent(
    long ResolutionId,
    int ResponderSeat,
    bool UsedSlash,
    int? SlashCardId,
    CardKind? ResponseCardKind = null) : IGameEvent;

public sealed record GroupResponseEvent(
    long ResolutionId,
    CardKind IncomingCard,
    CardKind RequiredCardKind,
    int ResponderSeat,
    bool UsedResponse,
    int? ResponseCardId,
    CardKind? ResponseCardKind = null) : IGameEvent;

/// <summary>
/// Public terminal result for Borrowed Sword. A successful branch records the
/// physical Slash that opened its own nested card-use frame; the fallback
/// records the already-public weapon transferred to the trick user.
/// </summary>
public sealed record BorrowedSwordResolvedEvent(
    long ResolutionId,
    int SourceSeat,
    int WeaponOwnerSeat,
    int SlashTargetSeat,
    bool UsedSlash,
    int? SlashCardId = null,
    CardKind? EffectiveSlashKind = null,
    int? TransferredWeaponCardId = null,
    CardKind? TransferredWeaponKind = null) : IGameEvent;

/// <summary>
/// Public terminal result for Stone Axe. The paid cards have already moved to
/// the public discard pile when this event is emitted.
/// </summary>
public sealed record StoneAxeResolvedEvent(
    long ResolutionId,
    int SourceSeat,
    int TargetSeat,
    bool Used,
    IReadOnlyList<int> DiscardedCardIds) : IGameEvent;

/// <summary>
/// Public declaration that two physical hand cards were converted into one
/// virtual Slash by Zhangba Serpent Spear.
/// </summary>
public sealed record ZhangbaSerpentSpearConvertedEvent(
    long ResolutionId,
    int UserSeat,
    IReadOnlyList<int> PhysicalCardIds,
    bool IsUse,
    int? TargetSeat = null) : IGameEvent;

public sealed record CixiongDoubleSwordsResolvedEvent(
    long ResolutionId,
    int SourceSeat,
    int TargetSeat,
    bool Activated,
    bool TargetDiscarded,
    int? DiscardedCardId = null,
    int SourceDrawCount = 0) : IGameEvent;

/// <summary>
/// Public terminal result for Qinglong Crescent Blade. A successful result
/// records the exact physical cards that immediately open a new Slash use
/// against the same target; an empty card list records a declined trigger.
/// </summary>
public sealed record QinglongCrescentBladeResolvedEvent(
    long ResolutionId,
    int SourceSeat,
    int TargetSeat,
    bool Used,
    IReadOnlyList<int> SlashCardIds,
    CardKind? EffectiveSlashKind = null) : IGameEvent;

/// <summary>
/// Public terminal result for Ice Sword. Used results prevent the pending
/// Slash damage and record the target cards discarded in their exact order.
/// </summary>
public sealed record IceSwordResolvedEvent(
    long ResolutionId,
    int SourceSeat,
    int TargetSeat,
    bool Used,
    int PreventedDamageAmount,
    IReadOnlyList<int> DiscardedCardIds) : IGameEvent;

public sealed record ProgramDamageTransferredEvent(
    long ResolutionId,
    string SkillId,
    int OwnerSeat,
    int SourceSeat,
    int TargetSeat,
    int DiscardedCardId,
    int DamageAmount,
    DamageNature Nature) : IGameEvent;

public sealed record ProgramDamageTransferCardsDrawnEvent(
    long ResolutionId,
    string SkillId,
    int OwnerSeat,
    int TargetSeat,
    int DrawCount) : IGameEvent;

public sealed record ProgramUniqueRankDyingResolvedEvent(
    long DyingFrameId,
    string SkillId,
    int OwnerSeat,
    CardZoneKind Zone,
    int CardId,
    int Rank,
    bool RankWasUnique,
    IReadOnlyList<int> WoundCardIds) : IGameEvent;

public sealed record LuanjiConvertedEvent(
    long ResolutionId,
    int SourceSeat,
    IReadOnlyList<int> PhysicalCardIds,
    Suit Suit) : IGameEvent;

/// <summary>Public terminal result for Qilin Bow at the Slash damage timing.</summary>
public sealed record QilinBowResolvedEvent(
    long ResolutionId,
    int SourceSeat,
    int TargetSeat,
    bool Used,
    int? DiscardedMountCardId) : IGameEvent;

/// <summary>Public terminal result for Pang De's Mengjin after a Slash is fully dodged.</summary>
public sealed record MengjinResolvedEvent(
    long ResolutionId,
    int SourceSeat,
    int TargetSeat,
    bool Used,
    int? DiscardedCardId) : IGameEvent;

/// <summary>Public result after both private Pindian cards have been committed and revealed.</summary>
/// <summary>Public turn-state result of Gao Shun's Xianzhen Pindian.</summary>
public sealed record XianzhenResolvedEvent(
    long ResolutionId,
    int SourceSeat,
    int TargetSeat,
    bool SourceWon) : IGameEvent;

/// <summary>Public draw-phase result and exact living-faction count for Liu Biao's Zishou.</summary>
public sealed record ZishouResolvedEvent(
    int PlayerSeat,
    bool Used,
    int LivingFactionCount,
    int DrawCount) : IGameEvent;

/// <summary>
/// Public declaration that one last-hand Slash used Fangtian Halberd's target
/// expansion. The ordered target list is exact and contains two or three seats.
/// </summary>
public sealed record FangtianHalberdUsedEvent(
    long ResolutionId,
    int SourceSeat,
    int SlashCardId,
    CardKind EffectiveSlashKind,
    IReadOnlyList<int> TargetSeats) : IGameEvent;

/// <summary>
/// Public audit record for Guding Blade's locked damage increase. The target
/// hand count is checked at the direct Slash damage timing, before damage is
/// requested and before any elemental chain propagation begins.
/// </summary>
public sealed record GudingBladeDamageIncreasedEvent(
    long ResolutionId,
    int SourceSeat,
    int TargetSeat,
    CardKind EffectiveSlashKind,
    int BaseAmount,
    int ModifiedAmount) : IGameEvent;

/// <summary>Public audit record for Tengjia increasing one fire-damage event.</summary>
public sealed record TengjiaFireDamageIncreasedEvent(
    long ResolutionId,
    int SourceSeat,
    int TargetSeat,
    CardKind? SourceCardKind,
    int BaseAmount,
    int ModifiedAmount) : IGameEvent;

public sealed record SilverLionDamageCappedEvent(
    long ResolutionId,
    int SourceSeat,
    int TargetSeat,
    CardKind? SourceCardKind,
    int BaseAmount,
    int ModifiedAmount) : IGameEvent;

public sealed record SilverLionRemovedRecoveryEvent(
    long ResolutionId,
    int PlayerSeat,
    CardMoveReason Reason,
    int RecoveredAmount,
    int RemainingHp) : IGameEvent;

/// <summary>
/// Public audit record for changing an ordinary or view-as ordinary Slash into
/// a Fire Slash when it is used through Zhuque Fan. The physical cards remain
/// unchanged and continue through the ordinary use pipeline.
/// </summary>
public sealed record ZhuqueFanConvertedEvent(
    long ResolutionId,
    int SourceSeat,
    IReadOnlyList<int> PhysicalCardIds,
    IReadOnlyList<int> TargetSeats) : IGameEvent;

public sealed record FactionDefenseRequestedEvent(
    long ResolutionId,
    int OwnerSeat,
    IReadOnlyList<int> CandidateSeats,
    string SkillId) : IGameEvent;

public sealed record FactionDefenseResolvedEvent(
    long ResolutionId,
    int OwnerSeat,
    bool Succeeded,
    int? ProviderSeat,
    int? ResponseCardId,
    bool UsedBagua = false) : IGameEvent;

public sealed record HuangtianCardGivenEvent(
    int ProviderSeat,
    int LordSeat,
    int CardId,
    CardKind CardKind) : IGameEvent;

public sealed record HuoshouAttributedEvent(long ResolutionId, int CardUserSeat, int DamageSourceSeat) : IGameEvent;

public sealed record ZaiqiResolvedEvent(
    int OwnerSeat,
    IReadOnlyList<int> RevealedCardIds,
    IReadOnlyList<int> HeartCardIds,
    IReadOnlyList<int> GainedCardIds,
    int RecoveredAmount) : IGameEvent;

public sealed record JuxiangCardClaimedEvent(long ResolutionId, int OwnerSeat, IReadOnlyList<int> CardIds) : IGameEvent;

public sealed record LierenResolvedEvent(
    long ResolutionId,
    int OwnerSeat,
    int TargetSeat,
    int? OwnerCardId,
    int? TargetCardId,
    bool Used,
    bool Won,
    int? GainedCardId) : IGameEvent;

public sealed record FactionSlashRequestedEvent(
    long ResolutionId,
    int OwnerSeat,
    IReadOnlyList<int> CandidateSeats,
    string SkillId,
    bool IsActiveUse,
    int? TargetSeat = null) : IGameEvent;

public sealed record FactionSlashResolvedEvent(
    long ResolutionId,
    int OwnerSeat,
    bool Succeeded,
    int? ProviderSeat,
    int? SlashCardId,
    CardKind? EffectiveSlashKind,
    bool IsActiveUse,
    int? TargetSeat = null) : IGameEvent;

public sealed record RoleRevealedEvent(int Seat, Role Role) : IGameEvent;

public sealed record WinnerDeterminedEvent(
    Winner Winner,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TeamId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FactionId = null) : IGameEvent;

public sealed record GameEndedEvent(
    Winner Winner,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TeamId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FactionId = null) : IGameEvent;

public sealed record CardMovedEvent(
    int CardId,
    CardKind CardKind,
    CardLocation From,
    CardLocation To,
    CardMoveReason Reason) : IGameEvent;

/// <summary>
/// Immutable event envelope. Events are a trusted-host projection; the ordinary
/// PlayerGameView/GameSnapshot never includes this stream or hidden card payloads.
/// </summary>
public sealed record EventEnvelope(
    EventId Id,
    EventId? ParentId,
    long Sequence,
    long Revision,
    string CorrelationId,
    IGameEvent Payload);
