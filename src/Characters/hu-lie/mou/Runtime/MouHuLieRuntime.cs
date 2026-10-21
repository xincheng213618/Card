namespace CardGame.Core;
[ProgramRuntimeModule, ProgramCapability]
internal sealed class MouHuLieRuntime(IMouHuLieFacts facts, IMouHuLieNative native, IProgramRuntimeFrames frames, IProgramRuntimeEvents events, IProgramRuntimePrompts prompts, IProgramOperationDraws draws) : IMouHuLieRuntime
{
    internal const string Chuanyu = "mou:chuanyu", Yitou = "mou:yitou", Action = "mou-hu-lie-anniversary";
    internal static CardConversionSource Source(ProgramSkillFrame f) => new(f.SkillId, f.ActivationId, f.OwnerSeat, f.SkillInstanceId);
    private int[] Recipients(MouHuLieYu yu) => facts.History<MouHuLieObtainedEvent>().Where(e => e.MarkId == yu.MarkId).Select(e => e.Recipient).Distinct().ToArray();
    private int[] GiftTargets(MouHuLieReceipt r) => facts.Living().Where(s => r.Yu is null || !Recipients(r.Yu).Contains(s)).ToArray();
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 37500, SkillProgramEffectOp.StartMouHuLieChuanyu, SkillProgramEffectOp.RegiveMouHuLieChuanyu, SkillProgramEffectOp.EndMouHuLieChuanyu, SkillProgramEffectOp.ResolveMouHuLieYitou, SkillProgramEffectOp.DrawMouHuLieYitou)]
    private bool Qualifies(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext w) => t.Effects.Single().Op switch {
        SkillProgramEffectOp.StartMouHuLieChuanyu => facts.Owned(c.OwnerSeat, false).Count > 0 && !facts.History<MouHuLieMarkedEvent>().Any(e => e.Yu.Source.OwnerSeat == c.OwnerSeat && e.Yu.Source.SkillInstanceId == c.SkillInstanceId && e.Yu.Round == facts.Round),
        SkillProgramEffectOp.RegiveMouHuLieChuanyu => facts.UsedYu(w, c.OwnerSeat, c.SkillInstanceId) is { } yu && facts.Living().Any(s => !Recipients(yu).Contains(s)),
        SkillProgramEffectOp.EndMouHuLieChuanyu => facts.ActiveYu(c.OwnerSeat, c.SkillInstanceId) is not null,
        SkillProgramEffectOp.ResolveMouHuLieYitou => facts.YitouTarget(w, c.OwnerSeat) is not null && facts.HandCount(c.OwnerSeat) > 0,
        _ => facts.Damage(c, w) is not null };
    public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var f = frames.ActiveProgram(supplied.Id);
        if (f.MouHuLie is not null || f.InstructionIndex != 1 || f.TriggerId != f.ActivationId || f.WindowContext is not { } w || !facts.Parent(f) || !Qualifies(new(f.OwnerSeat, f.SkillId, f.ActivationId, f.SkillInstanceId, f.GameplayHash, 0, w.OccurrenceIndex), facts.Program(f.SkillId).Triggers.Single(t => t.Id == f.TriggerId), w)) throw new InvalidOperationException("Mou Hu Lie requires his exact native round, used-discard, other Play or issued actual damage occurrence.");
        var damage = operation == SkillProgramEffectOp.DrawMouHuLieYitou ? facts.Damage(new(f.OwnerSeat, f.SkillId, f.ActivationId, f.SkillInstanceId, f.GameplayHash, 0, w.OccurrenceIndex), w) : null;
        var r = new MouHuLieReceipt(Source(f), f.GameplayHash, operation, w.ParentFrameId, facts.Round, facts.Turn, damage?.DamageId ?? 0) { Before = facts.Sequence, After = facts.Sequence, Stage = MouHuLieStage.Card };
        if (operation == SkillProgramEffectOp.RegiveMouHuLieChuanyu) { var yu = facts.UsedYu(w, f.OwnerSeat, f.SkillInstanceId)!; r = r with { Yu = yu, Cards = [facts.Current(yu.CardId)], Stage = MouHuLieStage.GiftTarget }; }
        if (operation == SkillProgramEffectOp.EndMouHuLieChuanyu) { var yu = facts.ActiveYu(f.OwnerSeat, f.SkillInstanceId)!; r = r with { Yu = yu, Cards = [facts.Current(yu.CardId)], Actors = facts.ActionOrder(Recipients(yu).Where(facts.Alive)), Stage = MouHuLieStage.Clearing }; }
        if (operation == SkillProgramEffectOp.ResolveMouHuLieYitou) r = r with { Cards = facts.Owned(f.OwnerSeat, true), Target = facts.YitouTarget(w, f.OwnerSeat)!.Value, Stage = MouHuLieStage.Giving };
        if (operation == SkillProgramEffectOp.DrawMouHuLieYitou) r = r with { Stage = MouHuLieStage.Drawing };
        frames.ReplaceTop(f = f with { MouHuLie = r }); events.AdvanceFact(new MouHuLieStartedEvent(f.Id, r.Source, r.GameplayHash, operation, r.ParentId, r.Round, r.Turn, r.DamageId));
        if (r.Stage == MouHuLieStage.Drawing) draws.BeginOperationProgramDraw(f, f.OwnerSeat, 1, new("skill-program.mou:yitou.draw")); else Continue(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
    internal bool Valid(ProgramSkillFrame f) => f.MouHuLie is { } r && f.InstructionIndex == 1 && f.TriggerId == f.ActivationId && r.Source == Source(f) && r.GameplayHash == f.GameplayHash && facts.Program(f.SkillId).GameplayHash == f.GameplayHash && r.ParentId == f.WindowContext?.ParentFrameId && facts.Parent(f) && r.Round == facts.Round && r.Turn == facts.Turn && r.Before >= 0 && r.After >= r.Before && r.After <= facts.Sequence && Enum.IsDefined(r.Stage) && r.Index >= 0 && r.Index <= Math.Max(r.Cards.Count, r.Actors.Count) && r.Cards.Select(c => c.Id).Distinct().Count() == r.Cards.Count && facts.History<MouHuLieStartedEvent>().Count(e => e == new MouHuLieStartedEvent(f.Id, r.Source, r.GameplayHash, r.Operation, r.ParentId, r.Round, r.Turn, r.DamageId)) == 1;
    private PromptChoice Choice(ProgramSkillFrame f, string branch, string label, int? card = null, int? target = null) => new(new($"mou-hu-lie.{f.Id}.{f.MouHuLie!.Stage}.{branch}.{card}.{target}"), label, card is { } id ? [id] : [], target is { } t ? [t] : [], new Dictionary<string, string> { ["program-action"] = Action, ["branch"] = branch });
    private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
    {
        var r = f.MouHuLie!;
        if (r.Stage == MouHuLieStage.Card) return facts.Owned(f.OwnerSeat, false).Select(c => Choice(f, "card", "以此为舆：" + c.Label, c.Id)).ToArray();
        if (r.Stage == MouHuLieStage.GiftTarget) return GiftTargets(r).Select(t => Choice(f, "target", "将舆交给" + facts.Name(t), target: t)).ToArray();
        if (r.Stage == MouHuLieStage.SlashOffer) return facts.Living().Select(t => Choice(f, "slash", "令获得舆者依次对" + facts.Name(t) + "使用杀", target: t)).Append(Choice(f, "stop", "不发动轮末用杀")).ToArray();
        return [];
    }
    private void Publish(ProgramSkillFrame f)
    {
        var choices = Choices(f); if (choices.Count == 0) { Complete(f); return; } frames.ReplaceTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        prompts.Set(new(DecisionKind.ProgramTrigger, f.OwnerSeat, "传舆：选择当前实体或本次固定角色。", choices.SelectMany(c => c.Cards).ToArray(), choices.SelectMany(c => c.Targets).ToArray(), f.OwnerSeat) { PromptId = prompts.NextId(), IsPrivate = f.MouHuLie!.Stage == MouHuLieStage.Card, Choices = Array.AsReadOnly(choices.ToArray()) }, facts.Human(f.OwnerSeat)); native.Publish();
    }
    [ProgramAnswer(Action)] private void Answer(PromptChoice supplied)
    {
        if (frames.Top is not ProgramSkillFrame { MouHuLie: { } r } f || !Valid(f) || prompts.Current?.PlayerSeat != f.OwnerSeat || Choices(f).SingleOrDefault(c => c.Id == supplied.Id) is not { } c || !c.Cards.SequenceEqual(supplied.Cards) || !c.Targets.SequenceEqual(supplied.Targets) || c.Parameters.Count != supplied.Parameters.Count || c.Parameters.Any(p => supplied.Parameters.GetValueOrDefault(p.Key) != p.Value)) throw new InvalidOperationException("Chuanyu requires its own original published current entity and fixed target.");
        prompts.Clear(); frames.ReplaceTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        if (r.Stage == MouHuLieStage.Card) { var card = facts.Owned(f.OwnerSeat, false).Single(c => c.Id == supplied.Cards.Single()); frames.ReplaceTop(f with { MouHuLie = r with { Cards = [card], Stage = MouHuLieStage.GiftTarget } }); Continue(f.Id); return; }
        if (r.Stage == MouHuLieStage.GiftTarget) { frames.ReplaceTop(f with { MouHuLie = r with { Target = supplied.Targets.Single(), Stage = MouHuLieStage.Giving } }); Continue(f.Id); return; }
        if (supplied.Parameters["branch"] == "stop") { Complete(f); return; }
        frames.ReplaceTop(f with { MouHuLie = r with { Target = supplied.Targets.Single(), Index = 0, Stage = MouHuLieStage.SlashIssuing } }); Continue(f.Id);
    }
    internal void Given(long id, IReadOnlyList<CardMovementRecord> moves, bool self = false)
    {
        var f = frames.ActiveProgram(id); var r = f.MouHuLie!;
        if (!Valid(f) || r.Stage != MouHuLieStage.GiveChildren || (self ? r.Operation != SkillProgramEffectOp.StartMouHuLieChuanyu || r.Target != f.OwnerSeat || moves.Count != 0 || r.Cards is not [var selfCard] || facts.Current(selfCard.Id) != selfCard : moves.Count != r.Cards.Count || moves.Any(m => !r.Cards.Any(c => c.Id == m.CardId && c.From == m.From) || m.Sequence <= r.Before || m.To != CardLocation.Hand(r.Target) && m.To != CardLocation.OutsideGame))) throw new InvalidOperationException("Chuanyu/Yitou lost the original successful native Give or explicit self-in-place mark proof.");
        frames.ReplaceTop(f = f with { MouHuLie = r with { After = moves.Count == 0 ? r.Before : moves.Max(m => m.Sequence) } });
        foreach (var m in moves) events.AdvanceFact(new MouHuLieMovedEvent(f.Id, r.Stage, m.CardId, m.From, m.To, m.Sequence));
        if (r.Operation == SkillProgramEffectOp.ResolveMouHuLieYitou) { if (moves.Any(m => m.To == CardLocation.Hand(r.Target))) events.AdvanceFact(new MouHuLieYitouIssuedEvent(f.Id, r.Source, r.GameplayHash, r.Turn, r.Target)); return; }
        if (!self && !moves.Any(m => m.To == CardLocation.Hand(r.Target))) return;
        var yu = r.Yu ?? new MouHuLieYu(f.Id, r.Source, r.GameplayHash, r.Round, r.Cards.Single().Id, r.Cards.Single().Kind, r.Cards.Single().From); var sequence = self ? r.Before : moves.Single().Sequence;
        if (r.Yu is null) events.AdvanceFact(new MouHuLieMarkedEvent(yu, r.Target, sequence, self));
        events.AdvanceFact(new MouHuLieObtainedEvent(yu.MarkId, r.Target, sequence));
    }
    internal void ClearedMoves(long id, IReadOnlyList<CardMovementRecord> moves)
    {
        var f = frames.ActiveProgram(id); var r = f.MouHuLie!;
        if (!Valid(f) || r.Stage != MouHuLieStage.ClearChildren || moves is not [var m] || m.CardId != r.Cards[r.Index - 1].Id || m.From != r.Cards[r.Index - 1].From || m.Sequence <= r.Before || m.To != CardLocation.DiscardPile && m.To != CardLocation.OutsideGame) throw new InvalidOperationException("Chuanyu clearing requires this exact currently reachable physical entity's actual discard.");
        frames.ReplaceTop(f with { MouHuLie = r with { After = m.Sequence } }); events.AdvanceFact(new MouHuLieMovedEvent(f.Id, r.Stage, m.CardId, m.From, m.To, m.Sequence));
    }
    internal void Continue(long id)
    {
        while (prompts.Current is null && frames.Top is ProgramSkillFrame { MouHuLie: { } r } f && f.Id == id)
        {
            if (!Valid(f)) throw new InvalidOperationException("Mou Hu Lie lost his owning native parent and exact issued round/turn receipt.");
            if (r.Stage is MouHuLieStage.NativeSlash or MouHuLieStage.Drawing) return;
            if (r.Stage is MouHuLieStage.GiveChildren or MouHuLieStage.ClearChildren or MouHuLieStage.SlashChildren or MouHuLieStage.DrawChildren)
            {
                if (native.Drain(f)) return; frames.ReplaceTop(f = f with { PendingMovementContinuation = null });
                if (r.Stage is MouHuLieStage.GiveChildren or MouHuLieStage.DrawChildren) { Complete(f); return; }
                frames.ReplaceTop(f with { MouHuLie = r with { Stage = r.Stage == MouHuLieStage.ClearChildren ? MouHuLieStage.Clearing : MouHuLieStage.SlashIssuing } }); continue;
            }
            if (r.Stage == MouHuLieStage.Giving)
            {
                if (!facts.Alive(f.OwnerSeat) || !facts.Alive(r.Target)) { Complete(f); return; }
                frames.ReplaceTop(f = f with { MouHuLie = r with { Stage = MouHuLieStage.GiveChildren, Before = facts.Sequence, After = facts.Sequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) }); native.Give(f); continue;
            }
            if (r.Stage == MouHuLieStage.Clearing)
            {
                if (r.Index < r.Cards.Count)
                {
                    var card = facts.Current(r.Cards[r.Index].Id); frames.ReplaceTop(f = f with { MouHuLie = r with { Cards = r.Cards.Select((c, i) => i == r.Index ? card : c).ToArray(), Index = r.Index + 1 } });
                    if (card.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment or CardZoneKind.DrawPile)) continue;
                    frames.ReplaceTop(f = f with { MouHuLie = f.MouHuLie! with { Stage = MouHuLieStage.ClearChildren, Before = facts.Sequence, After = facts.Sequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) }); native.Clear(f, card); continue;
                }
                events.AdvanceFact(new MouHuLieClearedEvent(f.Id, r.Yu!.MarkId, r.Round));
                frames.ReplaceTop(f with { MouHuLie = r with { Stage = MouHuLieStage.SlashOffer, Index = 0 } }); continue;
            }
            if (r.Stage == MouHuLieStage.SlashIssuing)
            {
                if (r.Index >= r.Actors.Count || !facts.Alive(r.Target) || facts.Finished) { Complete(f); return; }
                if (!facts.CanSlash(f)) { frames.ReplaceTop(f with { MouHuLie = r with { Index = r.Index + 1 } }); continue; }
                native.Use(f); return;
            }
            if (!facts.Alive(f.OwnerSeat) || facts.Finished) { Complete(f); return; }
            Publish(f); return;
        }
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.DrawMouHuLieYitou)] private void DrawReturned(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (!Valid(f) || f.MouHuLie is not { Stage: MouHuLieStage.Drawing, Draw: null } r || outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != 1) throw new InvalidOperationException("Yitou requires its own exact post-damage native draw return.");
        frames.ReplaceTop(f with { MouHuLie = r with { Stage = MouHuLieStage.DrawChildren, Draw = outcome, Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) }); events.AdvanceFact(new MouHuLieDrawReturnedEvent(f.Id, r.DamageId, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.AttemptFrameId)); Continue(f.Id);
    }
    private void Complete(ProgramSkillFrame f) { events.AdvanceFact(new MouHuLieCompletedEvent(f.Id)); frames.ReplaceTop(f = f with { MouHuLie = null, PendingMovementContinuation = null }); native.Finish(f); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 37500)] private bool Resume(long id) { if (frames.Top is not ProgramSkillFrame { MouHuLie: not null } f || f.Id != id) return false; Continue(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 37500)] private bool Movement(ProgramSkillFrame f) => f.MouHuLie is not null && Resume(f.Id);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 37500)] private void Invariant(ProgramSkillFrame f) { if (f.MouHuLie is not null && !Valid(f)) throw new InvalidOperationException("Invalid Mou Hu Lie immutable owning-frame continuation."); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 37500)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 37500)] private bool Paid(ProgramSkillFrame f) => f.MouHuLie is not null && Valid(f);
    [ProgramAiChoice(SkillProgramEffectOp.StartMouHuLieChuanyu, SkillProgramEffectOp.RegiveMouHuLieChuanyu, SkillProgramEffectOp.EndMouHuLieChuanyu)] private PromptChoice Ai(PendingDecision p, ProgramSkillFrame f) => p.Choices.OrderBy(c => c.Cards is [var id] ? facts.Keep(p.PlayerSeat, id) : c.Targets is [var target] ? (f.MouHuLie!.Stage == MouHuLieStage.SlashOffer ? !facts.Friendly(p.PlayerSeat, target) : facts.Friendly(p.PlayerSeat, target)) ? 0 : 1 : int.MaxValue).First();
}
