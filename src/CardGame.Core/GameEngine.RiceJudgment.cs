namespace CardGame.Core;
public sealed partial class GameEngine
{
    private IReadOnlyList<Card> RiceJudgmentReplacementCards(CharacterState owner, SkillProgramTrigger trigger)
    {
        var effect = trigger.Effects.Single();
        return EnabledUniqueProgramTriggers(owner, SkillProgramTriggerWindow.JudgmentReplacing)
            .Where(binding => ReferenceEquals(binding.Trigger, trigger) &&
                binding.Trigger.Effects is [{ Op: SkillProgramEffectOp.ReplaceJudgmentFromPublicRice }])
            .SelectMany(binding => RiceSources(owner.Seat, effect, binding.SkillInstanceId))
            .SelectMany(PublicPileCards).DistinctBy(card => card.Id).OrderBy(card => card.Id).ToArray();
    }
    private SkillProgramStepOutcome BeginRiceJudgmentReplacement(ProgramSkillFrame supplied, SkillProgramEffect effect)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (!ExactRiceParent(f, effect.Op) || f.RicePile is not null || f.WindowContext?.JudgmentReplacement is not { } context ||
            ActiveJudgment is not { } judgment || judgment.Id != context.JudgmentFrameId || GetJudgmentCard(judgment)?.Id != context.OldCardId)
            throw new InvalidOperationException("Rice judgment must retain its real selected replacement and owning judgment candidate.");
        var pile = RiceSources(f.OwnerSeat, effect, f.SkillInstanceId).Single(p => PublicPileCards(p).Any(c => c.Id == context.ReplacementCardId));
        var replacement = PublicPileCards(pile).Single(c => c.Id == context.ReplacementCardId); var old = GetJudgmentCard(judgment)!;
        var suit = EffectiveSuit(_players[judgment.TargetSeat], replacement); var before = _movementSequence;
        var r = new RicePileReceipt { Operation = effect.Op, Source = RiceIssuer(f), GameplayHash = f.GameplayHash, ParentId = context.JudgmentFrameId,
            ActualTurn = _turnNumber, Pile = pile, BeneficiarySeat = judgment.TargetSeat, Stage = RicePileStage.MovementChildren,
            CardIds = [replacement.Id], From = [pile.Location], Before = before, After = before };
        ReplaceRuntimeTop(f = f with { RicePile = r, PendingMovementContinuation = new(judgment.TargetSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new RicePileStartedEvent(f.Id, r));
        CommitProgramJudgmentReplacement(judgment, _players[f.OwnerSeat], old, replacement, suit,
            SkillProgramOldJudgmentCardDestination.DiscardPile, pile.Location);
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { RicePile = r with { After = _movementSequence }, WindowContext = f.WindowContext! with
        { JudgmentReplacement = context with { ReplacementSuit = suit, ReplacementRank = replacement.Rank } } });
        AdvanceEventRulesAndQueueFact(new RicePilePaymentIssuedEvent(f.Id, f.RicePile!));
        AdvanceEventRulesAndQueueFact(new JudgmentReplacementResolvedEvent(judgment.Id, judgment.Id, judgment.TargetSeat, f.OwnerSeat, judgment.Reason,
            true, old.Id, replacement.Id, replacement.Kind, suit, replacement.Rank));
        AddLog("JudgmentReplaced", $"{_players[f.OwnerSeat].Name} 用米替换了判定牌。", f.OwnerSeat, judgment.TargetSeat);
        if (!DrainRiceChildren(f)) ReturnRiceMovement(f);
        return SkillProgramStepOutcome.AwaitChild;
    }
}
