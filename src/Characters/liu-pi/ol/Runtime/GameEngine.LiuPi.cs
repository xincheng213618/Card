namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string LiuPiTableBind = "yicheng-public-table";
    private CardConversionSource LiuPiSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private static string LiuPiMovementReason(LiuPiExchangeStage stage) => stage switch {
        LiuPiExchangeStage.RevealChildren => "skill-program.ol:yicheng.exchange.reveal",
        LiuPiExchangeStage.EqualExchangeChildren => "skill-program.ol:yicheng.equal-exchange",
        LiuPiExchangeStage.FullExchangeChildren => "skill-program.ol:yicheng.full-exchange",
        LiuPiExchangeStage.CleanupChildren => "skill-program.ol:yicheng.return-top", _ => "" };

    [ProgramCapability]
    private sealed class LiuPiCapability(GameEngine engine) : ILiuPiProgramHost
    { public SkillProgramStepOutcome Exchange(ProgramSkillFrame f) => engine.BeginLiuPiExchange(f); }

    private SkillProgramStepOutcome BeginLiuPiExchange(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.LiuPiExchange is not null || f.TriggerId is not null || GetProgramBindingId(f) != "exchange")
            throw new InvalidOperationException("Yicheng needs its one original active program frame.");
        var owner = _players[f.OwnerSeat];
        if (!owner.IsAlive || !owner.HasHpValue) return SkillProgramStepOutcome.Continue;
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { LiuPiExchange = new(LiuPiSource(f), f.GameplayHash, f.InstructionIndex,
            LiuPiExchangeStage.RevealChildren, Math.Max(0, owner.MaxHp), before, before) });
        RevealProgramTopCards(f.Id, f.OwnerSeat, f.LiuPiExchange!.RevealLimit, null, LiuPiTableBind, SkillProgramCardSetVisibility.Public);
        f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f with { LiuPiExchange = f.LiuPiExchange! with {
            Table = GetProgramCardSet(f, LiuPiTableBind).CardIds.Select(LiuPiEntry).ToArray(), SequenceAfter = _movementSequence } });
        ContinueLiuPiExchange(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private LiuPiTableEntry LiuPiEntry(int id) => new(id, GetAdvancedCard(id).Rank,
        _cardMovements.LastOrDefault(m => m.CardId == id)?.Sequence ?? 0);
    private bool LiuPiEntryAvailable(LiuPiTableEntry e) => _cardZones.GetLocation(e.CardId) == CardLocation.Processing &&
        (_cardMovements.LastOrDefault(m => m.CardId == e.CardId)?.Sequence ?? 0) == e.MovementSequence && GetAdvancedCard(e.CardId).Rank == e.Rank;
    private bool LiuPiEntryHistoryMatches(ProgramSkillFrame f, LiuPiTableEntry e)
    {
        if (e.MovementSequence > _movementSequence || GetAdvancedCard(e.CardId).Rank != e.Rank ||
            _cardMovements.SingleOrDefault(m => m.Sequence == e.MovementSequence) is not { } moved ||
            moved.CardId != e.CardId || moved.To != CardLocation.Processing) return false;
        if (moved.From == CardLocation.DrawPile && moved.Reason.Value == LiuPiMovementReason(LiuPiExchangeStage.RevealChildren))
            return ProgramEventHistory<ProgramCardsRevealedEvent>().Any(p => p.FrameId == f.Id && p.SkillId == f.SkillId &&
                p.Bind == LiuPiTableBind && p.Cards.Any(c => c.Id == e.CardId && c.Rank == e.Rank));
        return moved.From == CardLocation.Hand(f.OwnerSeat) && ProgramEventHistory<LiuPiExchangeCommittedEvent>().Any(p =>
            p.ProgramFrameId == f.Id && p.Source == LiuPiSource(f) && moved.Sequence > p.SequenceBefore && moved.Sequence <= p.SequenceAfter &&
            moved.Reason.Value == LiuPiMovementReason(p.FullExchange ? LiuPiExchangeStage.FullExchangeChildren : LiuPiExchangeStage.EqualExchangeChildren));
    }
    private IReadOnlyList<LiuPiTableEntry> LiuPiAvailableTable(ProgramSkillFrame f) => f.LiuPiExchange!.Table.Where(LiuPiEntryAvailable).ToArray();
    private ProgramSkillFrame ReplaceLiuPiPublicTable(long id, IReadOnlyList<int> ids)
    {
        var f = GetActiveProgramFrame(id);
        if (f.LiuPiExchange is null || GetProgramCardSet(f, LiuPiTableBind).Visibility != SkillProgramCardSetVisibility.Public ||
            ids.Distinct().Count() != ids.Count || ids.Any(card => _cardZones.GetLocation(card) != CardLocation.Processing))
            throw new InvalidOperationException("Only the owning Yicheng frame may replace its exact public processing row.");
        var table = new ProgramSkillCardSetBinding(LiuPiTableBind, Array.AsReadOnly(ids.ToArray()), SkillProgramCardSetVisibility.Public,
            Array.AsReadOnly(ids.Select(_ => CardLocation.Processing).ToArray()));
        ReplaceRuntimeTop(f = f with { CardSetBindings = Array.AsReadOnly(f.CardSetBindings.Select(binding => binding.Name == LiuPiTableBind ? table : binding).ToArray()) });
        return f;
    }
    private bool DrainLiuPiChildren(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);

    private void ContinueLiuPiExchange(long id)
    {
        while (_resolutionStack.LastOrDefault() is ProgramSkillFrame { LiuPiExchange: { } r } f && f.Id == id)
        {
            if (DrainLiuPiChildren(id)) return;
            f = GetActiveProgramFrame(id); r = f.LiuPiExchange!;
            if (r.Stage == LiuPiExchangeStage.CleanupChildren)
            { ReplaceRuntimeTop(f with { LiuPiExchange = null }); AdvanceRuntimeProgram(id); return; }
            if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None)
            { CleanupLiuPiExchange(f); return; }
            var available = LiuPiAvailableTable(f);
            if (available.Count != r.Table.Count)
            {
                ReplaceRuntimeTop(f = f with { LiuPiExchange = r = r with { Table = available } });
                f = ReplaceLiuPiPublicTable(id, available.Select(e => e.CardId).ToArray());
            }
            switch (r.Stage)
            {
                case LiuPiExchangeStage.RevealChildren:
                    ReplaceRuntimeTop(f = f with { LiuPiExchange = r with { Stage = LiuPiExchangeStage.HandSelection } }); break;
                case LiuPiExchangeStage.EqualExchangeChildren:
                    if (r.EqualRankAfter <= r.EqualRankBefore || available.Count == 0) { CleanupLiuPiExchange(f); return; }
                    ReplaceRuntimeTop(f = f with { LiuPiExchange = r with { Stage = LiuPiExchangeStage.FullOffer, SelectedHand = [], SelectedTable = [] } }); break;
                case LiuPiExchangeStage.FullExchangeChildren:
                    CleanupLiuPiExchange(f); return;
            }
            PublishLiuPiExchange(f); return;
        }
    }

    private IReadOnlyList<PromptChoice> LiuPiChoices(ProgramSkillFrame f)
    {
        var r = f.LiuPiExchange!; var result = new List<PromptChoice>();
        void Add(string action, string label, IReadOnlyList<int>? cards = null) => result.Add(new(
            new($"liu-pi.{f.Id}.{r.Stage}.{action}.{string.Join('.', cards ?? [])}"), label, cards ?? [], [],
            new Dictionary<string, string> { ["program-action"] = "liu-pi", ["option"] = action }));
        if (r.Stage == LiuPiExchangeStage.HandSelection)
        {
            foreach (var card in GetHand(_players[f.OwnerSeat]))
                if (r.SelectedHand.Contains(card.Id) || r.SelectedHand.Count < LiuPiAvailableTable(f).Count)
                    Add("hand", $"{(r.SelectedHand.Contains(card.Id) ? "取消" : "选择")}【{card.DisplayName}】{card.RankText}", [card.Id]);
            Add("select-table", r.SelectedHand.Count == 0 ? "不交换，结束易城" : $"已选{r.SelectedHand.Count}张手牌，选择等量展示牌", r.SelectedHand);
        }
        else if (r.Stage == LiuPiExchangeStage.TableSelection)
        {
            foreach (var e in LiuPiAvailableTable(f))
                if (r.SelectedTable.Contains(e.CardId) || r.SelectedTable.Count < r.SelectedHand.Count)
                    Add("table", $"{(r.SelectedTable.Contains(e.CardId) ? "取消" : "选择")}【{GetAdvancedCard(e.CardId).DisplayName}】{e.Rank}", [e.CardId]);
            if (r.SelectedTable.Count == r.SelectedHand.Count) Add("equal-exchange", "完成等量交换", r.SelectedTable);
            Add("back", "重新选择手牌");
        }
        else if (r.Stage == LiuPiExchangeStage.FullOffer)
        {
            if (r.EqualRankAfter > r.EqualRankBefore) Add("full-order", "以全部手牌交换全部展示牌，先排列回顶顺序");
            Add("finish", "保留手牌，将展示牌按原顺序放回牌堆顶");
        }
        else if (r.Stage == LiuPiExchangeStage.FullOrder)
        {
            foreach (var id in r.FullHand.Where(id => !r.FullOrder.Contains(id)))
                Add("order", $"第{r.FullOrder.Count + 1}张回到牌堆顶：【{GetAdvancedCard(id).DisplayName}】", [id]);
            if (r.FullOrder.Count == r.FullHand.Count) Add("full-exchange", "按所选顺序完成全手交换", r.FullOrder);
            if (r.FullOrder.Count > 0) Add("reset-order", "重新排列回顶顺序");
            Add("finish", "取消第二次交换，按原顺序放回展示牌");
        }
        return Array.AsReadOnly(result.ToArray());
    }
    private void PublishLiuPiExchange(ProgramSkillFrame f)
    {
        var choices = LiuPiChoices(f);
        if (choices.Count == 0) { CleanupLiuPiExchange(f); return; }
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "易城：选择交换牌或回顶顺序",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(skill.Id, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    [ProgramAnswer("liu-pi")]
    private void AnswerLiuPiExchange(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { LiuPiExchange: { } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual([choice], LiuPiChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Yicheng lost its private chooser or an exact current physical selection.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var action = choice.Parameters["option"];
        static IReadOnlyList<int> Toggle(IReadOnlyList<int> ids, int id) => ids.Contains(id) ? ids.Where(e => e != id).ToArray() : ids.Append(id).ToArray();
        if (action == "hand") ReplaceRuntimeTop(f with { LiuPiExchange = r with { SelectedHand = Toggle(r.SelectedHand, choice.Cards.Single()) } });
        else if (action == "table") ReplaceRuntimeTop(f with { LiuPiExchange = r with { SelectedTable = Toggle(r.SelectedTable, choice.Cards.Single()) } });
        else if (action == "select-table")
        {
            if (r.SelectedHand.Count == 0) { CleanupLiuPiExchange(f); return; }
            ReplaceRuntimeTop(f with { LiuPiExchange = r with { Stage = LiuPiExchangeStage.TableSelection, SelectedTable = [] } });
        }
        else if (action == "back") ReplaceRuntimeTop(f with { LiuPiExchange = r with { Stage = LiuPiExchangeStage.HandSelection, SelectedTable = [] } });
        else if (action == "equal-exchange") IssueLiuPiExchange(f, false);
        else if (action == "full-order") ReplaceRuntimeTop(f with { LiuPiExchange = r with { Stage = LiuPiExchangeStage.FullOrder,
            FullHand = GetHand(_players[f.OwnerSeat]).Select(c => c.Id).ToArray(), FullOrder = [] } });
        else if (action == "order") ReplaceRuntimeTop(f with { LiuPiExchange = r with { FullOrder = r.FullOrder.Append(choice.Cards.Single()).ToArray() } });
        else if (action == "reset-order") ReplaceRuntimeTop(f with { LiuPiExchange = r with { FullOrder = [] } });
        else if (action == "full-exchange") IssueLiuPiExchange(f, true);
        else if (action == "finish") { CleanupLiuPiExchange(f); return; }
        else throw new InvalidOperationException("Unknown Yicheng answer.");
        ContinueLiuPiExchange(f.Id);
    }

    private void IssueLiuPiExchange(ProgramSkillFrame f, bool full)
    {
        var r = f.LiuPiExchange!; var hand = GetHand(_players[f.OwnerSeat]).ToArray(); var table = LiuPiAvailableTable(f).ToArray();
        var outgoing = full ? r.FullOrder.ToArray() : hand.Where(c => r.SelectedHand.Contains(c.Id)).Select(c => c.Id).ToArray();
        var incoming = table.Where(e => full || r.SelectedTable.Contains(e.CardId)).Select(e => e.CardId).ToArray();
        if (full ? r.EqualRankAfter <= r.EqualRankBefore || !hand.Select(c => c.Id).Order().SequenceEqual(r.FullHand.Order()) ||
                outgoing.Length != r.FullHand.Count || !outgoing.Order().SequenceEqual(r.FullHand.Order())
            : outgoing.Length != r.SelectedHand.Count || outgoing.Length != incoming.Length || outgoing.Length == 0)
            throw new InvalidOperationException("Yicheng must pay the exact equal subset, or its entire current hand after a real strict rank increase.");
        var beforeRank = table.Sum(e => e.Rank); var afterRank = full ? outgoing.Sum(id => GetAdvancedCard(id).Rank)
            : beforeRank - table.Where(e => incoming.Contains(e.CardId)).Sum(e => e.Rank) + outgoing.Sum(id => GetAdvancedCard(id).Rank);
        var stage = full ? LiuPiExchangeStage.FullExchangeChildren : LiuPiExchangeStage.EqualExchangeChildren;
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { LiuPiExchange = r with { Stage = stage, SequenceBefore = before, SequenceAfter = before,
            EqualRankBefore = full ? r.EqualRankBefore : beforeRank, EqualRankAfter = full ? r.EqualRankAfter : afterRank } });
        // The existing native helper commits both physical directions in ONE rules
        // batch. No observer sees a fictional empty hand between two exchange legs.
        MoveProgramCardsFromMultipleSources(outgoing.Concat(incoming).ToArray(), CardLocation.Processing, new(LiuPiMovementReason(stage)),
            afterPhysicalRecords: (_, movements) => {
                if (movements.Count != outgoing.Length + incoming.Length ||
                    movements.Any(m => m.From != (outgoing.Contains(m.CardId) ? CardLocation.Hand(f.OwnerSeat) : CardLocation.Processing) ||
                        m.To != (outgoing.Contains(m.CardId) ? CardLocation.Processing : CardLocation.Hand(f.OwnerSeat))))
                    throw new InvalidOperationException("Yicheng did not commit its exact two-direction physical batch.");
                var replacements = new Queue<int>(outgoing);
                var resulting = full ? outgoing.Select(LiuPiEntry).ToArray() : table.Select(e => incoming.Contains(e.CardId) ? LiuPiEntry(replacements.Dequeue()) : e).ToArray();
                var current = GetActiveProgramFrame(f.Id);
                ReplaceRuntimeTop(current with { LiuPiExchange = current.LiuPiExchange! with { Table = resulting, SequenceAfter = _movementSequence } });
                ReplaceLiuPiPublicTable(f.Id, resulting.Select(e => e.CardId).ToArray());
                AdvanceEventRulesAndQueueFact(new ProgramCardsRevealedEvent(f.Id, f.SkillId, GetProgramBindingId(f), f.OwnerSeat,
                    LiuPiTableBind, Array.AsReadOnly(resulting.Select(e => ToSnapshot(GetAdvancedCard(e.CardId))).ToArray())));
                AdvanceEventRulesAndQueueFact(new LiuPiExchangeCommittedEvent(f.Id, r.Source, full, outgoing.Length, incoming.Length,
                    beforeRank, afterRank, before, _movementSequence));
            }, destinationForCard: (_, source) => source == CardLocation.Hand(f.OwnerSeat) ? CardLocation.Processing : CardLocation.Hand(f.OwnerSeat));
    }

    private void CleanupLiuPiExchange(ProgramSkillFrame f)
    {
        if (f.LiuPiExchange!.Stage != LiuPiExchangeStage.CleanupChildren)
        {
            var returning = LiuPiAvailableTable(f).Select(e => e.CardId).ToArray(); var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { LiuPiExchange = f.LiuPiExchange with { Stage = LiuPiExchangeStage.CleanupChildren,
                SequenceBefore = before, SequenceAfter = before } });
            MoveProgramCardsFromMultipleSources(returning, CardLocation.DrawPile, new(LiuPiMovementReason(LiuPiExchangeStage.CleanupChildren)),
                afterPhysicalRecords: (_, _) => _cardZones.PlaceDrawPileCardsAtTop(returning));
            f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { LiuPiExchange = f.LiuPiExchange! with { SequenceAfter = _movementSequence } });
        }
        ContinueLiuPiExchange(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 26120)]
    private bool ResumeLiuPiExchange(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { LiuPiExchange: not null } f || f.Id != id) return false; ContinueLiuPiExchange(id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 26120)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 26120)]
    private bool ContinueIssuedLiuPiExchange(ProgramSkillFrame f) => f.LiuPiExchange is not null;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 26120)]
    private void AssertLiuPiExchange(ProgramSkillFrame f)
    {
        if (f.LiuPiExchange is not { } r) return;
        if (r.Source != LiuPiSource(f) || r.GameplayHash != f.GameplayHash || r.InstructionIndex != f.InstructionIndex ||
            f.TriggerId is not null || GetProgramBindingId(f) != "exchange" || r.RevealLimit < 0 ||
            r.SequenceBefore < 0 || r.SequenceAfter < r.SequenceBefore || r.SequenceAfter > _movementSequence || r.EqualRankBefore < 0 || r.EqualRankAfter < 0 ||
            r.Table.Select(e => e.CardId).Distinct().Count() != r.Table.Count || r.Table.Any(e => e.CardId <= 0 || e.Rank is < 1 or > 13 || e.MovementSequence <= 0) ||
            r.Table.Any(e => !LiuPiEntryHistoryMatches(f, e)) ||
            r.SelectedHand.Distinct().Count() != r.SelectedHand.Count || r.SelectedTable.Distinct().Count() != r.SelectedTable.Count ||
            r.FullHand.Distinct().Count() != r.FullHand.Count || r.FullOrder.Distinct().Count() != r.FullOrder.Count || r.FullOrder.Except(r.FullHand).Any() ||
            r.SelectedTable.Except(r.Table.Select(e => e.CardId)).Any() && r.Stage == LiuPiExchangeStage.TableSelection ||
            r.Stage == LiuPiExchangeStage.HandSelection && (r.SelectedTable.Count != 0 || r.SelectedHand.Count > r.Table.Count) ||
            r.Stage == LiuPiExchangeStage.TableSelection && (r.SelectedHand.Count == 0 || r.SelectedTable.Count > r.SelectedHand.Count) ||
            r.Stage is LiuPiExchangeStage.HandSelection or LiuPiExchangeStage.TableSelection && r.SelectedHand.Except(GetHand(_players[f.OwnerSeat]).Select(c => c.Id)).Any() ||
            r.Stage is LiuPiExchangeStage.FullOffer or LiuPiExchangeStage.FullOrder or LiuPiExchangeStage.FullExchangeChildren && r.EqualRankAfter <= r.EqualRankBefore ||
            r.Stage == LiuPiExchangeStage.FullOrder && !r.FullHand.Order().SequenceEqual(GetHand(_players[f.OwnerSeat]).Select(c => c.Id).Order()) ||
            r.Stage == LiuPiExchangeStage.FullExchangeChildren && r.FullOrder.Count != r.FullHand.Count)
            throw new InvalidOperationException("Yicheng lost its frozen owning activation, physical table, rank receipts or order.");
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveYichengPublicExchange)]
    private PromptChoice AiLiuPiExchange(PendingDecision p, ProgramSkillFrame f)
    {
        var r = f.LiuPiExchange!;
        if (r.Stage == LiuPiExchangeStage.HandSelection)
        {
            if (r.SelectedHand.Count == 0 && LiuPiAvailableTable(f) is { Count: > 0 } table &&
                p.Choices.Where(c => c.Parameters["option"] == "hand").OrderByDescending(c => GetAdvancedCard(c.Cards.Single()).Rank).FirstOrDefault() is { } pick &&
                GetAdvancedCard(pick.Cards.Single()).Rank > table.Min(e => e.Rank)) return pick;
            return p.Choices.Single(c => c.Parameters["option"] == "select-table");
        }
        if (r.Stage == LiuPiExchangeStage.TableSelection)
            return r.SelectedTable.Count == r.SelectedHand.Count ? p.Choices.Single(c => c.Parameters["option"] == "equal-exchange") :
                p.Choices.Where(c => c.Parameters["option"] == "table" && !r.SelectedTable.Contains(c.Cards.Single()))
                    .OrderBy(c => GetAdvancedCard(c.Cards.Single()).Rank).First();
        if (r.Stage == LiuPiExchangeStage.FullOffer)
            return p.Choices.Single(c => c.Parameters["option"] == (GetHand(_players[f.OwnerSeat]).Count < LiuPiAvailableTable(f).Count ? "full-order" : "finish"));
        return p.Choices.FirstOrDefault(c => c.Parameters["option"] == "order") ?? p.Choices.Single(c => c.Parameters["option"] == "full-exchange");
    }
}
