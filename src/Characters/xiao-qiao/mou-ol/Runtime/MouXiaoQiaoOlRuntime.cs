using System.Globalization;

namespace CardGame.Core;

internal interface IMouXiaoQiaoOlFacts : IProgramSelectionQuery
{
    bool Enabled { get; }
    bool SetupComplete { get; }
    int Round { get; }
    int Turn { get; }
    int CurrentSeat { get; }
    TurnPhase Phase { get; }
    Winner Winner { get; }
    int PlayerCount { get; }
    long MovementSequence { get; }
    bool Alive(int seat);
    bool Human(int seat);
    string Name(int seat);
    SkillProgram Program(string id);
    bool HasSource(CardConversionSource source);
    IEnumerable<T> History<T>() where T : IGameEvent;
    IReadOnlyList<Card> Hand(int seat);
    IReadOnlyList<Card> Equipment(int seat);
    Card Card(int id);
    CardLocation Location(int id);
    CardKind EffectiveKind(int seat, int id);
    Suit EffectiveSuit(int seat, int id);
    bool? EffectiveRed(int seat, int id);
    IReadOnlyList<CardMovementRecord> Movements(long before, long after);
    IReadOnlyList<MouXiaoQiaoOlUsePlan> NativePlans(int seat, CardKind kind, CardConversionSource source);
    IReadOnlyList<ProgramTriggerCandidate> Sources(int seat, string skillId);
    SkillPromptPresentation SkillPrompt(string skillId);
    double KeepValue(int seat, int cardId);
    double TargetValue(int owner, int target, bool helpful);
    (long FrameId, IReadOnlyList<int> CardIds) PublicReveal(IGameEvent payload);
    bool IsResponseRequest(ProgramSkillFrame frame);
    bool ActorLineage(long useId,long actionId,int originalActor,int actor,int provider,CardActionType type);
    bool GiftRecoveryChild(ProgramSkillFrame frame, ResolutionFrame child);
}
internal interface IMouXiaoQiaoOlOperations : IProgramRuntimeOperations
{
    void Finish(ProgramSkillFrame frame);
    void Reveal(ProgramSkillFrame frame, IReadOnlyList<int> ids);
    bool BeginRevealChildren(long frameId);
    void Gift(ProgramSkillFrame frame, int cardId, int target);
    void Recover(ProgramSkillFrame frame, int target);
    void LoseHp(ProgramSkillFrame frame, int target);
    bool BeginRecovery(long id);
    bool BeginCharacterState(long id);
    bool BeginHpChanged(long id);
    bool BeginCardsMoved(long id);
    bool BeginSkillsChanged(long id);
    void IssueNativeUse(ProgramSkillFrame frame, MouXiaoQiaoOlUsePlan plan);
    void SignNativeBatches(ProgramSkillFrame frame,CardUseFrame use);
    void BeginRevealTrigger(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context);
    void ReturnRevealWindow(PublicHandRevealWindowFrame window);
    bool BeginResponse(PromptChoice choice);
    void ReturnResponse(ProgramSkillFrame frame);
}

[ProgramRuntimeModule, ProgramCapability]
internal sealed partial class MouXiaoQiaoOlRuntime(IMouXiaoQiaoOlFacts facts, IMouXiaoQiaoOlOperations operations,
    IProgramRuntimeFrames frames, IProgramRuntimeEvents events, IProgramRuntimePrompts prompts,
    IProgramOperationDraws draws, IProgramHealthChildQueries health, IProgramHealthChildOperations healthOperations) : IMouXiaoQiaoOlRuntime
{
    internal const string Miluo = "mou-ol:miluo", Jueyan = "mou-ol:jueyan";
    internal const string GiveReason = "skill-program.mou-ol:miluo.give", DrawReason = "skill-program.mou-ol:jueyan.draw";
    private static CardConversionSource Source(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId ?? f.ActivationId, f.OwnerSeat, f.SkillInstanceId);
    private static bool SameChoice(PromptChoice a, PromptChoice b) => a.Id == b.Id && a.Cards.SequenceEqual(b.Cards) &&
        a.Targets.SequenceEqual(b.Targets) && a.Parameters.Count == b.Parameters.Count && a.Parameters.All(p => b.Parameters.GetValueOrDefault(p.Key) == p.Value);
    private PromptChoice Choice(ProgramSkillFrame f, string branch, string label, int card = 0, int target = -1, int option = -1) =>
        new(new($"mou-xiao-qiao.{f.Id}.{branch}.{card}.{target}.{option}"), label, card > 0 ? [card] : [], target >= 0 ? [target] : [],
            new Dictionary<string, string> { ["program-action"] = "mou-xiao-qiao-ol", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture),
                ["branch"] = branch, ["option"] = option.ToString(CultureInfo.InvariantCulture) });
    private bool HasMiluo(int owner, int round, int recipient) => facts.Hand(recipient).Any(card =>
        facts.History<MouXiaoQiaoMiluoGiftEvent>().Any(e => e.OwnerSeat == owner && e.Round == round && e.CardId == card.Id &&
            e.To == CardLocation.Hand(e.RecipientSeat)));
    internal bool IsMiluoCard(int id) => facts.History<MouXiaoQiaoMiluoGiftEvent>().Any(e => e.CardId == id && e.Round == facts.Round &&
        e.To == CardLocation.Hand(e.RecipientSeat)) && !facts.History<RoundEndedEvent>().Any(e => e.RoundNumber == facts.Round);
    private IReadOnlyList<int> Recipients(int owner, int round) => facts.History<MouXiaoQiaoMiluoGiftEvent>()
        .Where(e => e.OwnerSeat == owner && e.Round == round && e.To == CardLocation.Hand(e.RecipientSeat))
        .Select(e => e.RecipientSeat).Distinct().Where(facts.Alive).ToArray();
    [ProgramPipelineHook(ProgramPipelinePhase.CardEntitySkillLabel, 34000)]
    internal string? MiluoLabel(int id) => IsMiluoCard(id) ? "迷落" : null;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 34000, SkillProgramEffectOp.ResolveMouXiaoQiaoMiluoRoundEnd)]
    internal bool RoundEndEligible(ProgramTriggerLegalityContext q) => q.Context.Window == SkillProgramTriggerWindow.RoundEnding &&
        q.Candidate.SkillId == Miluo && Recipients(q.Candidate.OwnerSeat, facts.Round).Count > 0 &&
        !facts.History<MouXiaoQiaoOlStartedEvent>().Any(e=>e.Source.OwnerSeat==q.Candidate.OwnerSeat&&e.Round==facts.Round&&e.Operation==SkillProgramEffectOp.ResolveMouXiaoQiaoMiluoRoundEnd);

    public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var f = frames.ActiveProgram(supplied.Id);
        if (f.MouXiaoQiaoOl is not null) { Continue(f.Id); return SkillProgramStepOutcome.AwaitChild; }
        if (f.InstructionIndex != 1) throw new InvalidOperationException("Mou Xiao Qiao cannot repeat the accepted instruction.");
        if (operation == SkillProgramEffectOp.ResolveMouXiaoQiaoJueyanReveal) return BeginSuitReward(f);
        var end = operation == SkillProgramEffectOp.ResolveMouXiaoQiaoMiluoRoundEnd;
        if (end ? f.SkillId != Miluo || f.TriggerId != "round-end-consequence" || f.WindowContext?.Window != SkillProgramTriggerWindow.RoundEnding :
            operation == SkillProgramEffectOp.ResolveMouXiaoQiaoMiluo && (f.SkillId != Miluo || f.ActivationId != "reveal-and-give" ||
                facts.CurrentSeat != f.OwnerSeat || facts.Phase != TurnPhase.Play))
            throw new InvalidOperationException("Miluo requires its actual Play or native Round-ending binding.");
        var r = new MouXiaoQiaoOlReceipt(Source(f), f.GameplayHash, operation, facts.Round, frames.ParentOf(f.Id)?.Id ?? 0)
        { Stage = end ? MouXiaoQiaoOlStage.EndChoice : MouXiaoQiaoOlStage.GiftCards, Before = facts.MovementSequence, After = facts.MovementSequence };
        frames.ReplaceTop(f = f with { MouXiaoQiaoOl = r });
        events.AdvanceFact(new MouXiaoQiaoOlStartedEvent(f.Id, r.Source, f.GameplayHash, operation, r.Round, r.ParentFrameId));
        if (operation == SkillProgramEffectOp.ResolveMouXiaoQiaoJueyan) BeginJueyan(f);
        else Publish(f);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
    {
        var r = f.MouXiaoQiaoOl!; var list = new List<PromptChoice>();
        if (r.Stage == MouXiaoQiaoOlStage.GiftCards)
        {
            if (r.Gifts.Count < 2)
                foreach (var card in facts.Hand(f.OwnerSeat).Concat(facts.Equipment(f.OwnerSeat)).Where(c => !r.Gifts.Any(g => g.CardId == c.Id)))
                    list.Add(Choice(f, "card", "展示并交给其他角色：" + card.DisplayName, card.Id));
            list.Add(Choice(f, "confirm", $"确认展示并交出{r.Gifts.Count}张牌"));
        }
        else if (r.Stage == MouXiaoQiaoOlStage.GiftRecipient)
        {
            foreach (var seat in Enumerable.Range(0, facts.PlayerCount).Where(s => s != f.OwnerSeat && facts.Alive(s) && !r.Gifts.Any(g => g.RecipientSeat == s)))
                list.Add(Choice(f, "recipient", "交给" + facts.Name(seat), target: seat));
            list.Add(Choice(f, "back", "重新选择牌"));
        }
        else if (r.Stage == MouXiaoQiaoOlStage.EndChoice)
        {
            foreach (var seat in Recipients(f.OwnerSeat, r.Round))
            {
                var recover = HasMiluo(f.OwnerSeat, r.Round, seat);
                list.Add(Choice(f, recover ? "recover" : "lose", facts.Name(seat) + (recover ? "回复1点体力" : "失去1点体力"), target: seat));
            }
            list.Add(Choice(f, "decline", "不结算迷落效果"));
        }
        else if (r.Stage == MouXiaoQiaoOlStage.UseChoice)
            list.AddRange(facts.NativePlans(f.OwnerSeat, r.OutputKind, r.Source).Select((p, index) =>
                new PromptChoice(new($"mou-xiao-qiao.{f.Id}.use.{index}"), p.Description, [], p.Targets,
                    new Dictionary<string, string> { ["program-action"] = "mou-xiao-qiao-ol", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture),
                        ["branch"] = "use", ["option"] = index.ToString(CultureInfo.InvariantCulture) })));
        return Array.AsReadOnly(list.ToArray());
    }
    private void Publish(ProgramSkillFrame f)
    {
        var choices = Choices(f);
        if (!facts.Alive(f.OwnerSeat) || facts.Winner != Winner.None || choices.Count == 0) { Complete(f); return; }
        frames.ReplaceTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        prompts.Set(new(DecisionKind.ProgramTrigger, f.OwnerSeat, facts.SkillPrompt(f.SkillId).Instructions,
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = prompts.NextId(), IsPrivate = true, Choices = choices, SkillPrompt = facts.SkillPrompt(f.SkillId) }, facts.Human(f.OwnerSeat));
    }
    [ProgramAnswer("mou-xiao-qiao-ol")]
    internal void Answer(PromptChoice choice)
    {
        if (frames.Top is not ProgramSkillFrame { MouXiaoQiaoOl: { } r } f || !Valid(f) || prompts.Current?.PlayerSeat != f.OwnerSeat ||
            !Choices(f).Any(c => SameChoice(c, choice))) throw new InvalidOperationException("Mou Xiao Qiao requires its exact current private choice.");
        prompts.Clear(); frames.ReplaceTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        switch (choice.Parameters["branch"])
        {
            case "card":
                frames.ReplaceTop(f = f with { MouXiaoQiaoOl = r with { SelectedCard = choice.Cards.Single(), Stage = MouXiaoQiaoOlStage.GiftRecipient } }); Publish(f); return;
            case "back": frames.ReplaceTop(f = f with { MouXiaoQiaoOl = r with { SelectedCard = 0, Stage = MouXiaoQiaoOlStage.GiftCards } }); Publish(f); return;
            case "recipient":
                var card = facts.Card(r.SelectedCard); var from = facts.Location(card.Id);
                if (from.OwnerSeat != f.OwnerSeat || from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)) throw new InvalidOperationException("Miluo requires actual owned HE.");
                frames.ReplaceTop(f = f with { MouXiaoQiaoOl = r with { Gifts = r.Gifts.Append(new(card.Id, card.Kind, from, choice.Targets.Single())).ToArray(), SelectedCard = 0, Stage = MouXiaoQiaoOlStage.GiftCards } }); Publish(f); return;
            case "confirm":
                if (r.Gifts.Count > 2 || r.Gifts.Select(g => g.CardId).Distinct().Count() != r.Gifts.Count || r.Gifts.Select(g => g.RecipientSeat).Distinct().Count() != r.Gifts.Count ||
                    r.Gifts.Any(g => !facts.Alive(g.RecipientSeat) || facts.Location(g.CardId) != g.From)) throw new InvalidOperationException("Miluo lost a selected actual gift before revelation.");
                frames.ReplaceTop(f = f with { MouXiaoQiaoOl = r = r with { Paid = true, Stage = MouXiaoQiaoOlStage.RevealChildren } });
                events.AdvanceFact(new MouXiaoQiaoMiluoAcceptedEvent(f.Id, f.OwnerSeat, r.Round, r.Gifts.Count));
                if (r.Gifts.Count > 0) operations.Reveal(f, r.Gifts.Select(g => g.CardId).ToArray());
                Continue(f.Id); return;
            case "recover": case "lose":
                var target = choice.Targets.Single(); var recover = choice.Parameters["branch"] == "recover";
                frames.ReplaceTop(f = f with { MouXiaoQiaoOl = r with { Paid = true, TargetSeat = target, Recover = recover, Stage = MouXiaoQiaoOlStage.HealthChildren } });
                events.AdvanceFact(new MouXiaoQiaoMiluoConsequenceEvent(f.Id, f.OwnerSeat, r.Round, target, recover));
                if (recover) operations.Recover(f, target); else operations.LoseHp(f, target);
                if (frames.Top?.Id == f.Id) Continue(f.Id); return;
            case "decline": Complete(f); return;
            case "use":
                var plan = facts.NativePlans(f.OwnerSeat, r.OutputKind, r.Source)[int.Parse(choice.Parameters["option"], CultureInfo.InvariantCulture)];
                frames.ReplaceTop(f = f with { MouXiaoQiaoOl = r with { Plan = plan, Stage = MouXiaoQiaoOlStage.NativeIssuing,Before=facts.MovementSequence,After=facts.MovementSequence } });
                operations.IssueNativeUse(f, plan); return;
            default: throw new InvalidOperationException("Unknown Mou Xiao Qiao branch.");
        }
    }
    private bool Drain(long id) => operations.BeginRecovery(id) || operations.BeginCharacterState(id) || operations.BeginHpChanged(id) || operations.BeginCardsMoved(id) || operations.BeginSkillsChanged(id);
    private void Continue(long id)
    {
        if (frames.Top is not ProgramSkillFrame { MouXiaoQiaoOl: { } r } f || f.Id != id) return;
        Assert(f);
        if (r.Stage == MouXiaoQiaoOlStage.RevealChildren)
        {
            if (operations.BeginRevealChildren(id)) return;
            if (r.Operation == SkillProgramEffectOp.ResolveMouXiaoQiaoJueyan) { AfterJueyanReveal(f); return; }
            frames.ReplaceTop(f = f with { MouXiaoQiaoOl = r = r with { Stage = MouXiaoQiaoOlStage.GiftChildren } });
        }
        if (r.Stage == MouXiaoQiaoOlStage.GiftChildren)
        {
            if (Drain(id)) return;
            f = frames.ActiveProgram(id); r = f.MouXiaoQiaoOl!;
            frames.ReplaceTop(f = f with { PendingMovementContinuation = null });
            while (r.Cursor < r.Gifts.Count && facts.Winner == Winner.None)
            {
                var gift = r.Gifts[r.Cursor]; var index = r.Cursor;
                frames.ReplaceTop(f = f with { MouXiaoQiaoOl = r = r with { Cursor = index + 1, Before = facts.MovementSequence, After = facts.MovementSequence } });
                if (!facts.Alive(gift.RecipientSeat) || facts.Location(gift.CardId) != gift.From) continue;
                operations.Gift(f, gift.CardId, gift.RecipientSeat);
                f = frames.ActiveProgram(id); r = f.MouXiaoQiaoOl!;
                frames.ReplaceTop(f = f with { MouXiaoQiaoOl = r = r with { After = facts.MovementSequence }, PendingMovementContinuation = new(gift.RecipientSeat, 0, null) });
                var movement = facts.Movements(r.Before, r.After).Single(m => m.CardId == gift.CardId && m.From == gift.From && m.Reason.Value == GiveReason);
                events.AdvanceFact(new MouXiaoQiaoMiluoGiftEvent(f.Id, f.OwnerSeat, r.Round, index, gift.CardId, gift.PrintedKind, gift.RecipientSeat, movement.From, movement.To, movement.Sequence));
                if (Drain(id)) return;
                f = frames.ActiveProgram(id); r = f.MouXiaoQiaoOl!; frames.ReplaceTop(f = f with { PendingMovementContinuation = null });
            }
            Complete(f); return;
        }
        if (r.Stage is MouXiaoQiaoOlStage.HealthChildren or MouXiaoQiaoOlStage.NativeChildren or MouXiaoQiaoOlStage.DrawChildren)
        { if (!Drain(id)) Complete(frames.ActiveProgram(id)); return; }
        if (r.Stage is MouXiaoQiaoOlStage.GiftCards or MouXiaoQiaoOlStage.GiftRecipient or MouXiaoQiaoOlStage.EndChoice or MouXiaoQiaoOlStage.UseChoice) Publish(f);
    }
    private void Complete(ProgramSkillFrame f)
    {
        events.AdvanceFact(new MouXiaoQiaoOlCompletedEvent(f.Id, f.MouXiaoQiaoOl!.Operation));
        frames.ReplaceTop(f = f with { MouXiaoQiaoOl = null, PendingMovementContinuation = null }); operations.Finish(f);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 34000)]
    internal bool Resume(long id)
    { if (frames.Top is not ProgramSkillFrame { MouXiaoQiaoOl: not null } f || f.Id != id) return false; Continue(id); return true; }
    private bool Valid(ProgramSkillFrame f) => f.MouXiaoQiaoOl is { } r && r.Source == Source(f) && r.GameplayHash == f.GameplayHash &&
        facts.Program(f.SkillId).GameplayHash == f.GameplayHash && f.InstructionIndex == 1 && r.ParentFrameId == (frames.ParentOf(f.Id)?.Id ?? 0) &&
        r.Gifts.Count <= 2 && r.Gifts.Select(g => g.CardId).Distinct().Count() == r.Gifts.Count && r.Gifts.Select(g => g.RecipientSeat).Distinct().Count() == r.Gifts.Count &&
        facts.History<MouXiaoQiaoOlStartedEvent>().Count(e => e == new MouXiaoQiaoOlStartedEvent(f.Id, r.Source, r.GameplayHash, r.Operation, r.Round, r.ParentFrameId)) == 1 &&
        !facts.History<MouXiaoQiaoOlCompletedEvent>().Any(e => e.FrameId == f.Id);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 34000)]
    internal void Assert(ProgramSkillFrame f)
    { if (f.MouXiaoQiaoOl is not null && !Valid(f)) throw new InvalidOperationException("Mou Xiao Qiao lost its original frame, source or issued receipt."); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 34000)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 34000)]
    internal bool PreservePaid(ProgramSkillFrame f) => f.MouXiaoQiaoOl is { Paid: true } && Valid(f) ||
        f.SkillId==Jueyan&&f.TriggerId=="public-hand-first-suit"&&f.InstructionIndex is 0 or 1&&f.WindowContext is{}context&&
        IssuedRevealCandidate(new(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,0),context);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouXiaoQiaoMiluo, SkillProgramEffectOp.ResolveMouXiaoQiaoMiluoRoundEnd, SkillProgramEffectOp.ResolveMouXiaoQiaoJueyan)]
    internal PromptChoice Ai(PendingDecision prompt, ProgramSkillFrame f, SkillProgramEffect effect)
    {
        var r = f.MouXiaoQiaoOl!;
        return r.Stage switch
        {
            MouXiaoQiaoOlStage.GiftCards => prompt.Choices.First(c => c.Parameters["branch"] == "confirm"),
            MouXiaoQiaoOlStage.GiftRecipient => prompt.Choices.Where(c => c.Targets.Count == 1).OrderByDescending(c => facts.TargetValue(f.OwnerSeat, c.Targets[0], true)).First(),
            MouXiaoQiaoOlStage.EndChoice => prompt.Choices.OrderByDescending(c => c.Targets.Count == 0 ? 0 : facts.TargetValue(f.OwnerSeat, c.Targets[0], c.Parameters["branch"] == "recover")).First(),
            _ => prompt.Choices[0]
        };
    }
}
