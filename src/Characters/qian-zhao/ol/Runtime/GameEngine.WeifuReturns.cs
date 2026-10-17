namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasWeifuTargetTail(CardUseFrame use)
    {
        if (use.WeifuUse is not { Offered: true, AddedTargets.Count: > 0 } r || !WeifuUseProof(use) ||
            use.Action is not { Type: CardActionType.Use } action || !WeifuCanAddTargets(use) ||
            !action.TargetSeats.SequenceEqual(r.OriginalTargets.Concat(r.AddedTargets))) return false;
        for (var i = 0; i < r.AddedTargets.Count; i++)
        {
            var index = i;
            if (ProgramEventHistory<WeifuTargetAddedEvent>().Count(e => e.UseId == use.Id && e.ActionId == action.ActionId && e.Index == index &&
                e.TargetSeat == r.AddedTargets[index] && e.Source.OwnerSeat == r.ActorSeat && e.Source.SkillId == "ol:weifu" &&
                e.Source.BindingId == "issued-category-targets" && r.Grants.Any(g => g.Source.SkillInstanceId == e.Source.SkillInstanceId && g.Source.OwnerSeat == e.Source.OwnerSeat)) != 1) return false;
        }
        var actual = r.OriginalTargets.Concat(r.AddedTargets).ToArray();
        foreach (var redirect in ProgramEventHistory<WeifuRedirectedEvent>().Where(e => e.UseId == use.Id))
        {
            if (!IsSlashCard(use.CardKind) || redirect.ActionId != action.ActionId || redirect.TargetIndex < 0 || redirect.TargetIndex >= actual.Length ||
                actual[redirect.TargetIndex] != redirect.PreviousSeat || redirect.Source.OwnerSeat != redirect.PreviousSeat ||
                _contentRegistry.GetSkill(redirect.Source.SkillId).Program is not { } p || p.GameplayHash != redirect.GameplayHash ||
                ProgramInstructionResolver.Default.Find(p, ProgramInstructionSourceKind.Trigger, redirect.Source.BindingId) is not { } plan ||
                plan.Trigger?.Window != SkillProgramTriggerWindow.SlashTargetRedirecting || redirect.InstructionIndex < 1 || redirect.InstructionIndex > plan.Instructions.Count ||
                plan.GetPausedInstruction(redirect.InstructionIndex).Effect.Op != SkillProgramEffectOp.RedirectCurrentAttack ||
                ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == redirect.FrameId && e.OwnerSeat == redirect.Source.OwnerSeat &&
                    e.SkillId == redirect.Source.SkillId && e.BindingId == redirect.Source.BindingId && e.SkillInstanceId == redirect.Source.SkillInstanceId) != 1) return false;
            actual[redirect.TargetIndex] = redirect.TargetSeat;
        }
        return use.TargetSeats.SequenceEqual(actual);
    }
    private void RecordWeifuRedirect(ProgramSkillFrame redirector, long useId, int previousSeat, int nextSeat)
    {
        if (LifecycleCardUse(useId) is not { WeifuUse: { Offered: true, AddedTargets.Count: > 0 }, Action: { } action } use) return;
        var p = _contentRegistry.GetSkill(redirector.SkillId).Program!;
        if (p.GameplayHash != redirector.GameplayHash || redirector.InstructionIndex < 1 ||
            ProgramInstructionResolver.Default.Resolve(redirector, p).GetPausedInstruction(redirector.InstructionIndex).Effect is not { Op: SkillProgramEffectOp.RedirectCurrentAttack } effect ||
            nextSeat != ResolveProgramEffectTarget(redirector, effect.Target) || redirector.OwnerSeat != previousSeat ||
            redirector.WindowContext is not { Window: SkillProgramTriggerWindow.SlashTargetRedirecting, CardUse: { } context } c ||
            context.ParentCardUseFrameId != use.Id || context.CardActionId != action.ActionId || c.TargetSeat != previousSeat || c.SourceSeat != use.SourceSeat ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId) is not { } window ||
            window.Continuation != ProgramCardContinuation.SlashTargetRedirecting || window.ParentFrameId != use.Id || window.Action.ActionId != action.ActionId ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count || !MountObserverCandidateMatches(redirector, ToSharedCandidate(window.Candidates[window.CandidateIndex])) ||
            use.TargetIndex < 0 || use.TargetIndex >= use.TargetSeats.Count || use.TargetSeats[use.TargetIndex] != nextSeat ||
            ProgramEventHistory<WeifuRedirectedEvent>().Any(e => e.FrameId == redirector.Id && e.UseId == use.Id && e.InstructionIndex == redirector.InstructionIndex))
            throw new InvalidOperationException("Weifu's native redirect lost its exact producer and current target cursor.");
        AdvanceEventRulesAndQueueFact(new WeifuRedirectedEvent(redirector.Id, use.Id, action.ActionId, QianZhaoSource(redirector), redirector.GameplayHash,
            redirector.InstructionIndex, use.TargetIndex, previousSeat, nextSeat));
    }
    private bool TryGetWeifuVirtualPrimaryReturn(CardUseFrame use, ProgramSkillFrame parent, out IReadOnlyList<int> primary)
    {
        primary = [];
        if (((HasBingzhongTargetTail(use) || HasPijingTargetTail(use)) || HasWeifuTargetTail(use)) &&
            TryGetSuyiVirtualPrimaryReturn(use, parent, out primary)) return true;
        if (!(HasWeifuTargetTail(use) || (HasBingzhongTargetTail(use) || HasPijingTargetTail(use))) || use.CardId != 0 || use.PhysicalCardIds is not { Count: 0 } || use.CardKind != CardKind.Slash ||
            use.CardAttack is not { CardId: null, EffectiveCardKind: CardKind.Slash, PhysicalCardIds.Count: 0 } attack ||
            attack.ProgramSkillCardUseFrameId != parent.Id || attack.SourceSeat != use.SourceSeat ||
            use.Action is not { Type: CardActionType.Use, EffectiveKind: CardKind.Slash } action || action.ActorSeat != use.SourceSeat || action.ProviderSeat != action.ActorSeat ||
            action.PhysicalCards.Count != 0 || action.RequesterSeat is not null || action.ResponderSeat is not null || action.OpponentSeat is not null ||
            action.EffectiveSuit != Suit.None || action.EffectiveRank != 0 || parent.InstructionIndex < 1) return false;
        var index = _resolutionStack.FindIndex(f => f.Id == use.Id);
        if (index <= 0 || _resolutionStack[index - 1].Id != parent.Id || _contentRegistry.GetSkill(parent.SkillId).Program is not { } p || p.GameplayHash != parent.GameplayHash) return false;
        var history = CompleteProgramEventHistory().ToArray();
        if (history.OfType<TargetsConfirmedEvent>().SingleOrDefault(e => e.ResolutionId == use.Id) is not { TargetSeats.Count: 1 } confirmed ||
            history.OfType<CardUseDeclaredEvent>().SingleOrDefault(e => e.ResolutionId == use.Id) is not { CardId: 0, CardKind: CardKind.Slash } declared || declared.SourceSeat != action.ActorSeat ||
            history.OfType<CardActionAcceptedEvent>().Select(e => e.Action).FirstOrDefault(a => a.ActionId == action.ActionId) is not { Type: CardActionType.Use, EffectiveKind: CardKind.Slash, PhysicalCards.Count: 0 } accepted ||
            accepted.ActorSeat != action.ActorSeat || accepted.ProviderSeat != action.ProviderSeat || !accepted.TargetSeats.SequenceEqual(confirmed.TargetSeats) ||
            !accepted.ConversionChain.SequenceEqual(action.ConversionChain)) return false;
        var original = confirmed.TargetSeats[0]; var op = ProgramInstructionResolver.Default.Resolve(parent, p).GetPausedInstruction(parent.InstructionIndex).Effect;
        var exact = op.Op switch
        {
            SkillProgramEffectOp.OfferVirtualSlashOrDraw => parent.SelectedTargetSeats.SequenceEqual([action.ActorSeat]) && action.ConversionChain.Count == 0,
            SkillProgramEffectOp.UseVirtualSlash => parent.OwnerSeat == action.ActorSeat &&
                action.ConversionChain.SequenceEqual([new CardConversionSource(parent.SkillId, GetProgramBindingId(parent), parent.OwnerSeat, parent.SkillInstanceId)]) &&
                (op.TargetReference is null ? parent.SelectedTargetSeats.SequenceEqual([original]) : op.TargetReference.Kind == ProgramParticipantRef.EventTarget && parent.WindowContext?.TargetSeat == original),
            SkillProgramEffectOp.UseVirtualCard or SkillProgramEffectOp.OfferUnlimitedVirtualSlash => parent.OwnerSeat == action.ActorSeat && parent.SelectedTargetSeats.SequenceEqual([original]) &&
                action.ConversionChain.Count == 0 && op.UseCardActionWindows && op.OutputKind == CardKind.Slash && op.TargetRestriction == SkillProgramCardTargetRestriction.DistanceUnlimitedAgainstTarget &&
                (parent.TriggerId is not null || op.Op == SkillProgramEffectOp.OfferUnlimitedVirtualSlash),
            _ => false
        };
        if (!exact) return false;
        primary = Array.AsReadOnly(new[] { HasPijingTargetTail(use) ? original : use.TargetSeats[0] }); return true;
    }
}

