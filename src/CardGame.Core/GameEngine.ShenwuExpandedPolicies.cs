namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int ShenwuReplacementDrawBonus(int seat) => _modeDefinition.ShenwuActivity is { Edition: 2020, EnemyTier: >= 4 } &&
        !IsShenwuPlayerSlot(seat) ? 1 : 0;

    private (int Turn, int Seat, TurnPhase Phase, long Grants, bool Alive, bool Revealed)? _shenwuFengdongDependencies;
    private long _shenwuFengdongStamp;

    private long CaptureShenwuFengdongStamp()
    {
        if (!_contentRegistry.Skills.ContainsKey("shenwu:fengdong") || !IsValidPlayerSeat(_currentSeat) || _players.Count <= _currentSeat) return 0;
        var current = _players[_currentSeat];
        var hasSource = current.SkillGrants.Grants.Any(grant => grant.SkillId == "shenwu:fengdong");
        var dependencies = hasSource
            ? (_turnNumber, _currentSeat, _phase, current.SkillGrants.Revision, current.IsAlive, current.GeneralRevealed)
            : (0, -1, TurnPhase.NotStarted, 0L, false, false);
        if (_shenwuFengdongDependencies != dependencies)
        { _shenwuFengdongDependencies = dependencies; _shenwuFengdongStamp++; }
        return _shenwuFengdongStamp;
    }

    private bool ShenwuFengdongQualifiesGrant(CharacterState owner, SkillGrant grant) =>
        !_contentRegistry.ProgramDependencies.HasCardPolicy(SkillProgramCardPolicyKind.ShenwuOwnTurnFreezeNonLocked) ||
        owner.Seat == _currentSeat || _phase is TurnPhase.NotStarted or TurnPhase.Finished ||
        _contentRegistry.GetSkill(grant.SkillId).Tags.HasFlag(SkillTag.Locked) ||
        !HasCardPolicy(_players[_currentSeat], SkillProgramCardPolicyKind.ShenwuOwnTurnFreezeNonLocked);

    private bool ShenwuZhaohuoDisablesArmor(int targetSeat) => _players.Count > _currentSeat && targetSeat != _currentSeat &&
        _phase is not (TurnPhase.NotStarted or TurnPhase.Finished) && ShenwuEnabled(_players[_currentSeat], "shenwu:zhaohuo");

    private bool ShenwuSipuForbidsAction(int actorSeat)
    {
        if (actorSeat == _currentSeat || _phase != TurnPhase.Play || !HasCardPolicy(_players[_currentSeat], SkillProgramCardPolicyKind.ShenwuFirstTwoPlayCardsProhibitOthers)) return false;
        var history = CompleteProgramEventHistory().ToArray();
        var start = Array.FindLastIndex(history, fact => fact is PhaseChangedEvent { Phase: TurnPhase.Play } phase && phase.ActorSeat == _currentSeat);
        return start >= 0 && history.Skip(start + 1).OfType<CardUseDeclaredEvent>().Count(use => use.SourceSeat == _currentSeat) <= 2;
    }

    private bool ShenwuJuyuanExtraTarget(CharacterState player, CardKind kind) => IsSlashCard(kind) &&
        _currentSeat == player.Seat && _phase == TurnPhase.Play && HasCardPolicy(player, SkillProgramCardPolicyKind.ShenwuPreviousEndHealthSlashTarget) &&
        CompleteProgramEventHistory().OfType<ShenwuTurnEndHealthEvent>().LastOrDefault(fact => fact.OwnerSeat == player.Seat) is { } previous && player.Hp < previous.Hp;

    private bool HasExactShenwuYanyuParticipant(ProgramSkillFrame frame)
    {
        if (frame.WindowContext is not { TargetSeat: { } seat } context || seat != _currentSeat || seat == frame.OwnerSeat ||
            !frame.SelectedTargetSeats.SequenceEqual([seat]) || frame.InstructionIndex < 1) return false;
        var first = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).Instructions[0].Op;
        return frame.SkillId == "shenwu:yanyu" && context.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && first == SkillProgramEffectOp.BindShenwuTurnOwner ||
            frame.SkillId == "shenwu:bingxian" && context.Window == SkillProgramTriggerWindow.AfterTurnEnded && first == SkillProgramEffectOp.BindShenwuEndedTurnOwner &&
                _resolutionStack.OfType<DeferredTurnEndFrame>().Any(parent => parent.Id == context.ParentFrameId && parent.OwnerSeat == seat && IsActualAfterTurnEndedParent(parent));
    }

    private bool IsShenwuHonglianHandExempt(CharacterState owner, Card card) =>
        _contentRegistry.Skills.ContainsKey("shenwu:honglian") && ShenwuEnabled(owner, "shenwu:honglian") && IsRedSuit(EffectiveSuit(owner, card));
}
