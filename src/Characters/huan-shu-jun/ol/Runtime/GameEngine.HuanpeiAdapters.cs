namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void PrepareHuanpeiOriginalParent(HuanpeiPaidOrigin p)
    {
        if (p.Request.ParentSkillId is null || p.Request.Intent == HuanpeiIntent.WenyiPeach) return;
        var f = GetActiveProgramFrame(p.Request.ParentFrameId!.Value);
        if (!HuanpeiRequestParentCurrent(p.Request) || f.HuanpeiReturn is not null) throw new InvalidOperationException("Huanpei cannot replace another native request lease.");
        f = f with { HuanpeiReturn = new(p, 0, 0) };
        if (p.Request.Intent == HuanpeiIntent.KeBiNengBasic)
        {
            var k = f.KeBiNengPingduan!;
            var a = new LegalAction(IsSlashCard(p.Kind) ? LegalActionKind.Slash : p.Kind == CardKind.Peach ? LegalActionKind.Peach : LegalActionKind.Alcohol, 0,
                p.Targets.Count == 0 ? null : p.Targets[0], "环佩：真实零实体基本牌", p.Kind, TargetSeats: p.Targets) { ConversionSource = p.Source };
            f = f with { KeBiNengPingduan = k with { Stage = KeBiNengPingduanStage.BasicIssuing, BasicActions = k.BasicActions.Append(a).ToArray(), SelectedBasicAction = a } };
            AdvanceEventRulesAndQueueFact(new HuanpeiKeActionAcceptedEvent(f.Id, f.InstructionIndex, k.Source, f.GameplayHash, p.ProgramFrameId, p.Kind));
        }
        else if (IsQigongRequest(f)) f = f with { LvKuangLvXiang = f.LvKuangLvXiang! with { Stage = "issuing", IssuingIds = [], IssuingKind = p.Kind, IssuingProvider = p.Source.OwnerSeat } };
        else if (IsWuJingHejiRequest(f)) f = f with { WuJing = f.WuJing! with { Stage = WuJingStage.IssuingUse, Selection = new(p.Kind, false, false) { Conversions = [p.Source] }, Before = _movementSequence, After = _movementSequence } };
        else if (IsCaoChunInitialRequest(f))
        {
            var c = f.CaoChun!;
            f = f with { CaoChun = c with { Stage = CaoChunStage.NativeSlash, Slash = new(f.Id, c.Source, c.GameplayHash, c.InstructionIndex, c.ActualTurn, c.PhaseInstance, false, p.Targets.Single(), p.Kind) { ProviderSeat = p.Source.OwnerSeat, Materials = [], PaymentBefore = _movementSequence } } };
        }
        ReplaceRuntimeTop(f);
    }
    private bool HasHuanpeiGenericBasic(ProgramSkillFrame f) => f.KeBiNengPingduan is { Stage: KeBiNengPingduanStage.BasicChoice } k && ValidKeBiNeng(f) &&
        HuanpeiSources(_players[k.ActorSeat]).Any(s => CanAdjustHuanpeiHand(_players[k.ActorSeat]) && HuanpeiNativePlayActions(_players[k.ActorSeat], s, true).Count > 0);
    private bool HasAcceptedHuanpeiKeAction(ProgramSkillFrame f, LegalAction action)
    {
        if (f.HuanpeiReturn is not { Payment: { } p } || !ValidHuanpeiPayment(p) || p.Request.Intent != HuanpeiIntent.KeBiNengBasic || p.Request.ParentFrameId != f.Id || p.Request.Cursor != f.InstructionIndex || f.KeBiNengPingduan is not { } k || p.Source.OwnerSeat != k.ActorSeat ||
            action.CardId != 0 || action.SelectableCardIds.Count != 0 || action.ConversionSource != p.Source || action.PlayedCardKind != p.Kind || !action.TargetSeats.SequenceEqual(p.Targets) || action.Kind != (IsSlashCard(p.Kind) ? LegalActionKind.Slash : p.Kind == CardKind.Peach ? LegalActionKind.Peach : LegalActionKind.Alcohol)) return false;
        return ProgramEventHistory<HuanpeiKeActionAcceptedEvent>().Count(e => e == new HuanpeiKeActionAcceptedEvent(f.Id, f.InstructionIndex, k.Source, f.GameplayHash, p.ProgramFrameId, p.Kind)) == 1;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 28103)]
    private void AssertHuanpeiCaller(ProgramSkillFrame f)
    {
        if (f.HuanpeiReturn is not { } r) return;
        if (!ValidHuanpeiPayment(r.Payment) || r.Payment.Request.ParentFrameId != f.Id || r.Payment.Request.RequestFrameId != f.Id || r.Payment.Request.Cursor != f.InstructionIndex || r.Payment.Request.ParentSkillId != f.SkillId || r.Payment.Request.ParentSkillInstanceId != f.SkillInstanceId || r.Payment.Request.ParentGameplayHash != f.GameplayHash || r.Payment.Request.ParentOperation != GetPausedPrivateOfferEffect(f)?.Op || r.UseId < 0 || r.ActionId < 0 || r.UseId == 0 && r.ActionId != 0 || r.UseId > 0 && !HasHuanpeiProgramPayment(f)) throw new InvalidOperationException("Huanpei's caller lost its exact native request, once-paid source or child return.");
        if (f.KeBiNengPingduan is { SelectedBasicAction: { } selected } && selected.ConversionSource?.SkillId == "ol:huanpei" && !HasAcceptedHuanpeiKeAction(f, selected)) throw new InvalidOperationException("Huanpei's generic basic request changed its accepted native action.");
    }
}
