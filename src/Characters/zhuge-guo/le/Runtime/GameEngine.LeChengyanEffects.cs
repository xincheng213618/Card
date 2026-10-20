using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record LeChengyanEffectPlan(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    long ActionId, int ShownCardId, CardKind EffectKind)
{
    public bool Started { get; init; }
    public long? TranslatedWindowId { get; init; }
    public int? TranslatedTargetIndex { get; init; }
    public int SlashCursor { get; init; }
    public bool Finished { get; init; }
    public int BorrowedCursor { get; init; }
    public bool AwaitingVictim { get; init; }
    public bool AwaitingBorrowedEffect { get; init; }
    public int? AuxiliaryVictimSeat { get; init; }
    private readonly IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public LeChengyanEffectPlan? LeChengyanEffect { get; init; } }
public sealed record LeChengyanEffectStartedEvent(long UseId, long ActionId, CardKind OriginalKind, CardKind EffectKind, int TargetCount) : IGameEvent;

public sealed partial class GameEngine
{
    // CardKind and Action remain the paid declaration. This query is used only
    // by native effect continuations, never by legality, quotas or Use ledgers.
    private static CardKind LeChengyanNativeEffectKind(CardUseFrame use) => use.LeChengyanEffect is { Started: true, Finished: false } p ? p.EffectKind : use.CardKind;
    private static IReadOnlyList<int> LeChengyanNativeTargets(CardUseFrame use) => use.LeChengyanEffect is { Started: true, Finished: false } p ? p.Targets : use.TargetSeats;
    private bool IsLeChengyanEffectUse(long id) => LifecycleCardUse(id) is { LeChengyanEffect: { Started: true, Finished: false } } u && ValidLeChengyanEffect(u);
    private bool ValidLeChengyanEffect(CardUseFrame u) => u.LeChengyanEffect is { } p && u.Action is { Type: CardActionType.Use } a &&
        a.ActionId == p.ActionId && a.ActorSeat == p.Source.OwnerSeat && u.SourceSeat == a.ActorSeat &&
        (IsSlashCard(a.EffectiveKind) || IsOrdinaryTrick(a.EffectiveKind)) &&
        p.EffectKind != CardKind.Nullification && (IsSlashCard(p.EffectKind) || IsOrdinaryTrick(p.EffectKind)) &&
        _contentRegistry.GetSkill(p.Source.SkillId).Program?.GameplayHash == p.GameplayHash &&
        ProgramEventHistory<LeChengyanEffectIssuedEvent>().Count(e => e == new LeChengyanEffectIssuedEvent(p.ProgramFrameId, u.Id, p.ActionId, p.Source, p.ShownCardId, p.EffectKind)) == 1;
    private void AssertLeChengyanEffects()
    {
        foreach (var u in _resolutionStack.OfType<CardUseFrame>().Where(u => u.LeChengyanEffect is not null))
            if (!ValidLeChengyanEffect(u) || u.LeChengyanEffect!.Targets.Any(s => !IsValidPlayerSeat(s)))
                throw new InvalidOperationException("Chengyan effect lost its original action or one issued shown-card receipt.");
    }
    private bool TryStartLeChengyanEffect(ProgramCardTriggerWindowFrame window)
    {
        if (GetCardActionWindow(window) != SkillProgramTriggerWindow.CardUseTargetsFinalized ||
            LifecycleCardUse(window.ParentFrameId) is not { LeChengyanEffect: { Started: false } p } u) return false;
        if (!ValidLeChengyanEffect(u) || _resolutionStack.LastOrDefault()?.Id != u.Id)
            throw new InvalidOperationException("Chengyan must return to its paid original native Use.");
        // Auxiliary Borrowed Sword victims are not designated card targets.
        var targets = (u.CardKind == CardKind.BorrowedSword ? u.TargetSeats.Where((_, i) => i % 2 == 0) : u.TargetSeats).ToArray();
        ReplaceRuntimeTop(u = u with { LeChengyanEffect = p with { Started = true, Targets = targets }, TargetIndex = 0,
            SequentialTrick = null, FireAttackSelection = null, CardAttack = null, PreparedTargetAttacks = null,
            Continuations = u.Continuations with { FangtianHalberd = null, GroupCard = null, BorrowedSword = null } });
        AdvanceEventRulesAndQueueFact(new LeChengyanEffectStartedEvent(u.Id, p.ActionId, u.CardKind, p.EffectKind, targets.Length));
        StartLeChengyanNativeEffect(u); return true;
    }
    private void StartLeChengyanNativeEffect(CardUseFrame u)
    {
        var p = u.LeChengyanEffect!;
        if (p.Targets.Count == 0 || _winner != Winner.None)
        { FinishCardUse(u.Id, LeChengyanOriginalCard(u), u.CardKind); return; }
        var kind = p.EffectKind;
        if (kind == CardKind.BorrowedSword) { BeginLeChengyanBorrowedWithAuxiliary(u); return; }
        var action = IsSlashCard(kind) ? LegalActionKind.Slash : Enum.Parse<LegalActionKind>(kind.ToString());
        CardKind? required = kind == CardKind.BarbarianAssault ? CardKind.Slash : kind == CardKind.ArrowBarrage ? CardKind.Dodge : null;
        BeginNullificationWindow(u.Id, LeChengyanOriginalCard(u), u.SourceSeat, p.Targets, action, requiredCardKind: required, playedCardKind: u.CardKind);
    }
    private Card LeChengyanOriginalCard(CardUseFrame u) => GetTrickRepresentation(u.Id, u.CardId);
    private bool TryBypassLeChengyanNullification(long id, Card card, IReadOnlyList<int> targets, LegalActionKind action, int? targetCard, CardKind? required)
    {
        if (LifecycleCardUse(id) is not { LeChengyanEffect.Started: true } u || !IsSlashCard(u.CardKind) || !ValidLeChengyanEffect(u)) return false;
        ResolveNullifiableEffect(new NullificationWindowFrame(++_resolutionSequence, id, u.SourceSeat, Array.AsReadOnly(targets.ToArray()), card.Id,
            u.CardKind, action, targetCard, required, [])); return true;
    }
    private bool TryResolveLeChengyanNativeEffect(NullificationWindowFrame pending)
    {
        if (LifecycleCardUse(pending.ParentFrameId) is not { LeChengyanEffect: { Started: true, Finished: false } p } u ||
            p.TranslatedWindowId == pending.Id && p.TranslatedTargetIndex == u.TargetIndex || !ValidLeChengyanEffect(u)) return false;
        if (pending.EffectCardKind != u.CardKind || pending.SourceSeat != u.SourceSeat || pending.EffectCardId != u.CardId)
            throw new InvalidOperationException("Chengyan effect must retain its paid card, actor and original counterspell eligibility.");
        ReplaceRuntimeFrame(u.Id, u = u with { LeChengyanEffect = p with { TranslatedWindowId = pending.Id, TranslatedTargetIndex = u.TargetIndex } });
        if (IsSlashCard(p.EffectKind))
        {
            BeginLeChengyanSlash(u); return true;
        }
        ResolveNullifiableEffectCore(pending with { EffectCardKind = p.EffectKind }); return true;
    }
    private void BeginLeChengyanSlash(CardUseFrame u)
    {
        var p = u.LeChengyanEffect!; var index = p.SlashCursor;
        while (index < p.Targets.Count && (!_players[p.Targets[index]].IsAlive || IsCardEffectIneffective(u.Id, p.Targets[index]))) index++;
        if (index >= p.Targets.Count || _winner != Winner.None)
        {
            ReplaceRuntimeFrame(u.Id, u = u with { LeChengyanEffect = p with { Finished = true } });
            FinishCardUse(u.Id, LeChengyanOriginalCard(u), u.CardKind); return;
        }
        ReplaceRuntimeFrame(u.Id, u = u with { TargetIndex = index, LeChengyanEffect = p with { SlashCursor = index } });
        var target = p.Targets[index]; var card = LeChengyanOriginalCard(u);
        var attack = new CardAttackHandle(this, u.Id, u.SourceSeat, target, card.Id == 0 ? null : card,
            damageAmount: 1 + (IsSlashCard(u.CardKind) ? u.ConsumedAlcoholBonus ?? (u.ConsumedAlcoholBoost ? 1 : 0) : 0),
            playedCardKind: p.EffectKind, ignoresArmor: HasCardArmorBypass(_players[u.SourceSeat], _players[target], u.CardKind),
            physicalCards: GetCardUsePhysicalCards(u.Id));
        ActiveCardAttack = attack;
        ContinueSlashAfterProgramTargetEffects(attack);
    }
    private bool FinishLeChengyanAttack(CardAttackHandle attack, out bool paused)
    {
        paused = false;
        if (LifecycleCardUse(attack.ResolutionId) is not { LeChengyanEffect: { Started: true, Finished: false } p } u ||
            !ValidLeChengyanEffect(u)) return false;
        if (!IsSlashCard(p.EffectKind))
        {
            if (attack.EffectiveCardKind != p.EffectKind || ActiveGroupCard is not null) return false;
            ActiveCardAttack = null; ActiveDuel = null; ClearPendingDecision();
            FinishCardUse(u.Id, LeChengyanOriginalCard(u), u.CardKind); paused = true; return true;
        }
        if (attack.TargetSeat != p.Targets[p.SlashCursor] || attack.EffectiveCardKind != p.EffectKind)
            throw new InvalidOperationException("Chengyan Slash lost its current original target.");
        ActiveCardAttack = null; ActiveDuel = null; ClearPendingDecision(); u = LifecycleCardUse(u.Id)!;
        ReplaceRuntimeFrame(u.Id, u = u with { LeChengyanEffect = p with { SlashCursor = p.SlashCursor + 1 }, CausedDamage = u.CausedDamage || attack.DamageWasApplied });
        BeginLeChengyanSlash(u); paused = true; return true;
    }
}
