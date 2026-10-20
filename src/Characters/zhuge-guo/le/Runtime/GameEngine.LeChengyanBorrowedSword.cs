using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    // This native auxiliary-target producer does not change the paid Action's
    // designated targets. It is called only when that interpretation is selected.
    private void BeginLeChengyanBorrowedWithAuxiliary(CardUseFrame supplied)
    {
        var u = LifecycleCardUse(supplied.Id)!; var p = u.LeChengyanEffect!; var cursor = p.BorrowedCursor;
        while (cursor < p.Targets.Count && (!_players[p.Targets[cursor]].IsAlive || GetWeapon(_players[p.Targets[cursor]]) is null ||
            !_players.Any(v => IsLegalBorrowedSwordSlashTarget(_players[p.Targets[cursor]], v)))) cursor++;
        if (cursor == p.Targets.Count || _winner != Winner.None)
        {
            ReplaceRuntimeFrame(u.Id, u = u with { LeChengyanEffect = p with { Finished = true, AwaitingVictim = false, AwaitingBorrowedEffect = false } });
            FinishCardUse(u.Id, LeChengyanOriginalCard(u), u.CardKind); return;
        }
        ReplaceRuntimeFrame(u.Id, u = u with { TargetIndex = cursor, LeChengyanEffect = p with { BorrowedCursor = cursor, AwaitingVictim = true,
            AwaitingBorrowedEffect = false, AuxiliaryVictimSeat = null } });
        var choices = LeChengyanBorrowedChoices(u);
        _pendingDecision = new(DecisionKind.ProgramTrigger, u.SourceSeat, $"乘烟·借刀杀人：为{_players[p.Targets[cursor]].Name}选择杀的目标。", [],
            choices.SelectMany(c => c.Targets).ToArray(), u.SourceSeat, CardKind.BorrowedSword)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = u.SourceSeat, Choices = choices,
            SkillPrompt = new(LeChengyan, "乘烟", "乘烟 · 借刀杀人", "原用牌的目标不变，此处仅补充借刀的辅助受害者。") };
        _status = _players[u.SourceSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    private IReadOnlyList<PromptChoice> LeChengyanBorrowedChoices(CardUseFrame u)
    {
        var p = u.LeChengyanEffect!; var actor = _players[p.Targets[p.BorrowedCursor]];
        return Array.AsReadOnly(_players.Where(v => IsLegalBorrowedSwordSlashTarget(actor, v)).Select(v => new PromptChoice(new($"le-chengyan.{u.Id}.victim.{p.BorrowedCursor}.{v.Seat}"),
            $"令{actor.Name}对{v.Name}使用杀", [], [v.Seat], new Dictionary<string, string> { ["program-action"] = "le-chengyan-borrowed-victim",
                ["frame-id"] = u.Id.ToString(CultureInfo.InvariantCulture), ["target-index"] = p.BorrowedCursor.ToString(CultureInfo.InvariantCulture) })).ToArray());
    }
    private bool HasLeChengyanBorrowedPrompt() => _resolutionStack.LastOrDefault() is CardUseFrame { LeChengyanEffect: { AwaitingVictim: true } } u &&
        ValidLeChengyanEffect(u) && _pendingDecision is { Kind: DecisionKind.ProgramTrigger } d && d.PlayerSeat == u.SourceSeat &&
        d.Choices.Count > 0 && AssistedChoicesEqual(d.Choices, LeChengyanBorrowedChoices(u));
    [ProgramAnswer("le-chengyan-borrowed-victim")]
    private void AnswerLeChengyanBorrowedVictim(PromptChoice selected)
    {
        if (!HasLeChengyanBorrowedPrompt() || _resolutionStack.LastOrDefault() is not CardUseFrame { LeChengyanEffect: { } p } u ||
            !AssistedChoicesEqual([selected], LeChengyanBorrowedChoices(u).Where(c => c.Id == selected.Id).ToArray()))
            throw new InvalidOperationException("Chengyan auxiliary target must belong to its current paid Use and native Borrowed Sword option.");
        var victim = selected.Targets.Single(); ClearPendingDecision();
        ReplaceRuntimeFrame(u.Id, u = u with { LeChengyanEffect = p with { AwaitingVictim = false, AwaitingBorrowedEffect = true, AuxiliaryVictimSeat = victim } });
        BeginNullificationWindow(u.Id, LeChengyanOriginalCard(u), u.SourceSeat, [p.Targets[p.BorrowedCursor], victim], LegalActionKind.BorrowedSword, playedCardKind: u.CardKind);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.BeforeDeclaration, 30201)]
    private bool AdvanceLeChengyanBorrowedAi()
    {
        if (!HasLeChengyanBorrowedPrompt() || _players[_pendingDecision!.PlayerSeat].IsHuman) return false;
        var d = _pendingDecision; var view = CreateSnapshot(d.PlayerSeat);
        var choice = d.Choices.OrderByDescending(c => _aiBrains[d.PlayerSeat].ScoreProgramTarget(view, c.Targets.Single(), new(OwnerDraw: 0, OwnerRecovery: 0, OwnerHpLoss: 0,
            TargetDraw: 0, TargetRecovery: 0, TargetHpLoss: 1, GivesSelected: false, DiscardsSelected: false))).ThenBy(c => c.Targets.Single()).First();
        AnswerLeChengyanBorrowedVictim(choice); AdvanceRulesAndPublishState(); return true;
    }
    private bool FinishLeChengyanBorrowedNative(BorrowedSwordHandle borrowed)
    {
        if (LifecycleCardUse(borrowed.ResolutionId) is not { LeChengyanEffect: { AwaitingBorrowedEffect: true, EffectKind: CardKind.BorrowedSword } p } u || !ValidLeChengyanEffect(u)) return false;
        if (borrowed.WeaponOwnerSeat != p.Targets[p.BorrowedCursor] || borrowed.SlashTargetSeat != p.AuxiliaryVictimSeat)
            throw new InvalidOperationException("Chengyan Borrowed Sword returned to a different original target or auxiliary victim.");
        ActiveBorrowedSword = null;
        if (SameContinuationOwner(ActiveFactionCardRequest?.BorrowedSword, borrowed)) ActiveFactionCardRequest = null;
        FinishCardUse(u.Id, LeChengyanOriginalCard(u), u.CardKind); return true;
    }
    private bool KeepLeChengyanBorrowedMaterial(long id) => LifecycleCardUse(id) is { LeChengyanEffect: { Started: true, Finished: false, EffectKind: CardKind.BorrowedSword } } u && ValidLeChengyanEffect(u);
    private bool TryContinueLeChengyanBorrowed(long id)
    {
        if (LifecycleCardUse(id) is not { LeChengyanEffect: { Finished: false, AwaitingBorrowedEffect: true, EffectKind: CardKind.BorrowedSword } p } u || !ValidLeChengyanEffect(u)) return false;
        ReplaceRuntimeFrame(id, u = u with { LeChengyanEffect = p with { BorrowedCursor = p.BorrowedCursor + 1, AwaitingBorrowedEffect = false, AuxiliaryVictimSeat = null } });
        BeginLeChengyanBorrowedWithAuxiliary(u); return true;
    }
}
