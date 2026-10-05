namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static bool HasFinalTargetSlashEffects(IEnumerable<SkillProgramEffect> effects) =>
        effects.Any(effect => effect.Op is SkillProgramEffectOp.PreventCurrentTargetSlashCancellation or SkillProgramEffectOp.AddCurrentTargetSlashDamage or
            SkillProgramEffectOp.ClaimCurrentUsePhysicalCards or SkillProgramEffectOp.PreventCurrentTargetSlashCancellationByRule);
    private static bool MatchesFinalTargetComparison(ProgramFinalTargetComparison comparison, SkillProgramTriggerFacts facts) => comparison switch
    {
        ProgramFinalTargetComparison.TargetHandAtMostActor => facts.EventTargetHandCount <= facts.CurrentHandCount,
        ProgramFinalTargetComparison.TargetHpAtLeastActor => facts.EventTargetHp >= facts.CurrentHp,
        ProgramFinalTargetComparison.Always => true,
        _ => throw new InvalidOperationException("Unknown frozen final target comparison.")
    };
    private void IssueFinalTargetSlashReceipt(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized,
                CardUse: { } context, Facts: { } facts, TargetSeat: { } target } ||
            _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(use => use.Id == context.ParentCardUseFrameId) is not { Action: { } action } owner ||
            action.ActionId != context.CardActionId || action.Type != CardActionType.Use || !IsSlashCard(action.EffectiveKind) ||
            frame.OwnerSeat != context.ActorSeat || frame.OwnerSeat != action.ActorSeat ||
            !action.EffectiveDesignatedTargetSeats.Contains(target) ||
            !HasFinalTargetSlashEffects(_contentRegistry.GetSkill(frame.SkillId).Program!.Triggers.Single(t => t.Id == frame.TriggerId).Effects) ||
            effect.FinalTargetComparison is not { } comparison)
            throw new InvalidOperationException("A final target Slash fact lost its exact owning action and frozen target.");
        if (!_players[target].IsAlive || !MatchesFinalTargetComparison(comparison, facts)) return;
        var entries = owner.FinalTargetSlashReceipts ?? [];
        var index = frame.InstructionIndex - 1;
        if (entries.Any(item => item.ProducerFrameId == frame.Id && item.EffectIndex == index)) return;
        var receipt = new ProgramTargetSlashReceipt(action.ActionId, action.ActorSeat, target, owner.Id, frame.Id, frame.SkillId, frame.SkillInstanceId, frame.GameplayHash, frame.TriggerId!, index,
            effect.Op == SkillProgramEffectOp.PreventCurrentTargetSlashCancellation,
            effect.Op == SkillProgramEffectOp.AddCurrentTargetSlashDamage ? effect.Amount : 0);
        ReplaceRuntimeFrame(owner.Id, owner with { FinalTargetSlashReceipts = Array.AsReadOnly(entries.Append(receipt).ToArray()) });
        AdvanceEventRulesAndQueueFact(new ProgramTargetSlashReceiptIssuedEvent(owner.Id, receipt));
    }
    private IEnumerable<ProgramTargetSlashReceipt> FinalTargetSlashReceipts(long frameId, int target) =>
        _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(use => use.Id == frameId) is { Action: { } action, FinalTargetSlashReceipts: { } entries }
            ? entries.Where(item => item.ActionId == action.ActionId && item.CardUseFrameId == frameId && item.ActorSeat == action.ActorSeat && item.TargetSeat == target && action.EffectiveDesignatedTargetSeats.Contains(target)) : [];
    private int FinalTargetSlashDamage(IDamageAttempt attack) => !attack.IsChainPropagation && attack is CardAttackHandle &&
        IsSlashCard(attack.EffectiveCardKind ?? CardKind.Slash)
            ? FinalTargetSlashReceipts(attack.ResolutionId, attack.TargetSeat).Sum(item => item.DamageBonus) : 0;
    private void AssertFinalTargetSlashReceipts()
    {
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(use => use.FinalTargetSlashReceipts is not null))
        {
            var entries = use.FinalTargetSlashReceipts!;
            if (use.Action is not { Type: CardActionType.Use } action || !IsSlashCard(action.EffectiveKind) ||
                entries.DistinctBy(item => (item.ProducerFrameId, item.EffectIndex)).Count() != entries.Count ||
                entries.Any(item => item.ActionId != action.ActionId || item.CardUseFrameId != use.Id || !ReceiptProducerMatches(item) || !IsValidPlayerSeat(item.ActorSeat) ||
                    !IsValidPlayerSeat(item.TargetSeat) || item.ActorSeat == item.TargetSeat || item.ProducerFrameId <= 0 || item.EffectIndex < 0 ||
                    item.DamageBonus is < 0 or > 20 || item.PreventCancellation && item.DamageBonus != 0 ||
                    !item.PreventCancellation && item.DamageBonus == 0))
                throw new InvalidOperationException("A final target Slash receipt lost its owning use or producer identity.");
        }
    }
    private bool ReceiptProducerMatches(ProgramTargetSlashReceipt receipt)
    {
        if (string.IsNullOrEmpty(receipt.SkillInstanceId) || _contentRegistry.Skills.GetValueOrDefault(receipt.SkillId)?.Program is not { } program ||
            program.GameplayHash != receipt.GameplayHash || program.Triggers.SingleOrDefault(t => t.Id == receipt.TriggerId) is not { } trigger ||
            trigger.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || trigger.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor ||
            receipt.EffectIndex < 0 || receipt.EffectIndex >= trigger.Effects.Count) return false;
        var effect = trigger.Effects[receipt.EffectIndex];
        return receipt.PreventCancellation
            ? effect.Op is SkillProgramEffectOp.PreventCurrentTargetSlashCancellation or SkillProgramEffectOp.PreventCurrentTargetSlashCancellationByRule && receipt.DamageBonus == 0
            : effect.Op == SkillProgramEffectOp.AddCurrentTargetSlashDamage && effect.Amount == receipt.DamageBonus;
    }
    private bool CanOfferFinalTargetSlash(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext window)
    {
        if (!HasFinalTargetSlashEffects(trigger.Effects)) return true;
        return window is { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, TargetSeat: { } target, CardUse: { } context, Facts: not null } &&
            IsValidPlayerSeat(target) && _players[target].IsAlive &&
            _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(use => use.Id == context.ParentCardUseFrameId)?.Action is { Type: CardActionType.Use } action &&
            action.ActionId == context.CardActionId && action.ActorSeat == candidate.OwnerSeat && context.ActorSeat == candidate.OwnerSeat &&
            IsSlashCard(action.EffectiveKind) && action.EffectiveDesignatedTargetSeats.Contains(target);
    }
    private bool HasRankSlashRange(CharacterState actor, CardKind kind) =>
        IsSlashCard(kind) && (HasCardPolicy(actor, SkillProgramCardPolicyKind.SlashRangeFromEffectiveRank, kind) || CurrentJudgedRankSlashPolicy(actor.Seat) is not null);
    private bool IsWithinSpecificSlashRange(CharacterState actor, CharacterState target, CardKind kind, int? rank, long? existingUseFrameId = null) =>
        HasJudgedRankSlashDistance(actor.Seat, kind, rank, existingUseFrameId) ||
        (HasCardPolicy(actor, SkillProgramCardPolicyKind.SlashRangeFromEffectiveRank, kind) && rank is > 0
            ? GetCombatDistance(actor.Seat, target.Seat) <= rank.Value
            : IsWithinAttackRange(actor.Seat, target.Seat));
    private bool HasPotentialRankSlashRange(CharacterState actor, CharacterState target, CardKind kind) =>
        (CurrentJudgedRankSlashPolicy(actor.Seat) is { Rank: > 1 } || HasRankSlashRange(actor, kind) && (GetSlashUseCards(actor).Any(card => IsWithinSpecificSlashRange(actor,target,kind,SpecificSlashRank(actor,card,kind))) ||
            GetZhangbaSlashPairs(actor).Any(pair => IsWithinSpecificSlashRange(actor,target,kind,ZhangbaSpecificSlashRank(actor,pair)))));
    private int? ProvidedSpecificSlashRank(CharacterState actor, CharacterState provider, IReadOnlyList<Card> cards, bool isTrueZhangba, CardKind kind = CardKind.Slash) =>
        cards.Count == 1 ? HasRankSlashRange(actor, kind) ? CaptureAlcoholIdentityRank(provider, cards[0]) ?? SpecificSlashRank(actor,cards[0],kind) : null : isTrueZhangba ? ZhangbaSpecificSlashRank(actor,cards,provider) : null;
    private bool IsSpecificRankProviderPaymentLegal(FactionCardRequestHandle pending, CardKind kind, IReadOnlyList<Card> cards, bool isTrueZhangba = false)
    {
        if (!IsFactionSlashUse(pending) || !HasRankSlashRange(_players[pending.OwnerSeat],kind) || pending.TargetSeat is not { } target) return true;
        var actor = _players[pending.OwnerSeat];
        var rank = ProvidedSpecificSlashRank(actor,_players[pending.CurrentCandidateSeat],cards,isTrueZhangba,kind);
        var suit = PhysicalGroupSuit(actor,cards); var color = PhysicalGroupColor(actor,cards);
        return pending.IsBorrowedSwordUse ? IsLegalBorrowedSwordSlashTarget(actor,_players[target],kind,suit,false,color,rank)
            : pending.IsQinglongCrescentBladeUse ? CanUseQinglongCrescentBladeTarget(actor,_players[target],kind,color,false,suit,rank)
            : pending.IsAssistedProgramUse ? IsAssistedProvidedSlashTarget(actor.Seat,target,kind,rank,allowPotentialRank:false)
            : CanUseProvidedSlashTarget(actor,_players[target],kind,suit,false,color,rank);
    }
    private int? SpecificSlashRank(CharacterState actor, Card card, CardKind kind, long? useId = null)
    {
        if (!HasRankSlashRange(actor, kind)) return null;
        if (useId is { } id && _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(use => use.Id == id)?.Action is { } action)
            return action.EffectiveRank is > 0 ? action.EffectiveRank : null;
        var rank = EffectiveOwnedCardRank(actor, card);
        return rank > 0 ? rank : null;
    }
    private int? ZhangbaSpecificSlashRank(CharacterState actor, IReadOnlyList<Card> cards, CharacterState? provider = null) =>
        HasCardPolicy(actor, SkillProgramCardPolicyKind.SlashRangeFromEffectiveRank, CardKind.Slash) && UsesFormalZhangbaSerpentSpear && HasWeaponAbility(provider ?? actor, CardKind.ZhangbaSerpentSpear) && cards.Count == 2
            ? Math.Min(13, cards.Sum(card => card.Rank)) : null;
    private sealed partial class ProgramSkillHost : IFinalTargetSlashProgramHost
    {
        public void IssueFinalTargetSlashReceipt(ProgramSkillFrame frame, SkillProgramEffect effect) => engine.IssueFinalTargetSlashReceipt(frame, effect);
    }
}
