using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum YanPuStage { GiftChoice, GiftChildren, EndingChoice, RecoveryChildren, RecipientDrawChildren, OwnerDrawChildren, EndingGiftChoice, EndingGiftChildren, ExitDrawChildren, Complete }
public sealed record YanPuHuantuPromise(long Id, CardConversionSource Source, string GameplayHash, long DrawWindowId,
    int ActualRound, int ActualTurn, int ActualTurnOwner, int RecipientSeat, int CardId, CardLocation From, long MovementSequence);
public sealed record YanPuMovement(int Index, int SubjectSeat, string Reason, long Before, long After, int Actual, int? CardId = null, CardLocation? From = null, CardLocation? To = null);
public sealed record YanPuReceipt
{
    private IReadOnlyList<int> _selected = Array.AsReadOnly(Array.Empty<int>());
    private IReadOnlyList<YanPuMovement> _moves = Array.AsReadOnly(Array.Empty<YanPuMovement>());
    public required CardConversionSource Source { get; init; }
    public required string GameplayHash { get; init; }
    public int InstructionIndex { get; init; }
    public long WindowId { get; init; }
    public int Round { get; init; }
    public int Turn { get; init; }
    public int TurnOwner { get; init; }
    public int Target { get; init; }
    public YanPuStage Stage { get; init; }
    public YanPuHuantuPromise? Promise { get; init; }
    public YanPuDyingExitReturn? Exited { get; init; }
    public int RequiredGift { get; init; }
    public IReadOnlyList<int> SelectedCards { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<YanPuMovement> Movements { get => _moves; init => _moves = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public YanPuReceipt? YanPu { get; init; }
}
public sealed record YanPuDyingExitReturn(long DyingFrameId, long DyingParentFrameId, int VictimSeat, DyingContinuationKind Continuation, int Round, int Turn, int TurnOwner);
public sealed partial record ProgramLifecycleTriggerWindowFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public YanPuDyingExitReturn? YanPuDyingExit { get; init; }
}
public sealed record YanPuStartedEvent(long ProgramFrameId, CardConversionSource Source, string GameplayHash, long WindowId, int Round, int Turn, int TurnOwner, int Target, YanPuStage Stage) : IGameEvent;
public sealed record YanPuHuantuIssuedEvent(YanPuHuantuPromise Promise) : IGameEvent;
public sealed record YanPuHuantuConsumedEvent(long PromiseId, long EndingWindowId, long ProgramFrameId) : IGameEvent;
public sealed record YanPuMovementIssuedEvent(long ProgramFrameId, YanPuMovement Movement) : IGameEvent;
public sealed record YanPuRoundDistanceIssuedEvent(long ProgramFrameId, CardConversionSource Source, string GameplayHash, int Round, int Turn, long DyingFrameId, int TargetSeat) : IGameEvent;
public sealed record YanPuCompletedEvent(long ProgramFrameId, bool Applied) : IGameEvent;

internal interface IYanPuHost { SkillProgramStepOutcome ExecuteYanPu(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class YanPuDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ISkillProgramEffectHandler Handler => new YanPuHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(4));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(Op switch
    { SkillProgramEffectOp.GiveCardSkipDrawForEnding => SkillProgramTriggerWindow.DrawPhaseStarting, SkillProgramEffectOp.ResolveHuantuEnding => SkillProgramTriggerWindow.TurnEnding, _ => SkillProgramTriggerWindow.DyingExited })];
}
internal sealed class GiveCardSkipDrawForEndingDescriptor : YanPuDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.GiveCardSkipDrawForEnding; }
internal sealed class ResolveHuantuEndingDescriptor : YanPuDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveHuantuEnding; }
internal sealed class DrawDyingExitRoundDistanceDescriptor : YanPuDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawDyingExitRoundDistance; }
internal sealed class YanPuHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) => host.GetCapability<IYanPuHost>().ExecuteYanPu(f, op);
}
internal static class YanPuComposition
{
    internal static bool IsOperation(SkillProgramEffectOp op) => op is SkillProgramEffectOp.GiveCardSkipDrawForEnding or SkillProgramEffectOp.ResolveHuantuEnding or SkillProgramEffectOp.DrawDyingExitRoundDistance;
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (p.Activations.Any(a => a.Effects.Any(e => IsOperation(e.Op)))) throw new InvalidOperationException($"{path}: YanPu requires actual triggered boundaries.");
        var gifts = p.Triggers.Count(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.GiveCardSkipDrawForEnding));
        var endings = p.Triggers.Count(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveHuantuEnding));
        if ((gifts > 0 || endings > 0) && (gifts != 1 || endings != 1))
            throw new InvalidOperationException($"{path}: Huantu requires its unique actual Draw gift and matching issued Ending instruction together.");
        foreach (var t in p.Triggers.Where(t => t.Effects.Any(e => IsOperation(e.Op))))
        {
            if (t.Effects is not [var e] || !IsOperation(e.Op) || e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always ||
                t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.MarkerCost is not null || t.ChoiceGroup is not null || t.DeferredTurnEndOnly || t.AllowNoEventTarget || t.IncludeResponseUses ||
                t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.SourceZones.Count != 0 || t.DestinationZones.Count != 0 || t.Suits.Count != 0 || t.DamageCardKinds.Count != 0 ||
                t.SourceSkillId is not null || t.SourceViewAsId is not null || t.OwnerRelation is not null || t.GainPhaseQualification is not null || t.NoDyingAtActivation ||
                t.DynamicUsageLimit is not null || t.NamedUsageGroup is not null || t.DrawPhaseMode != SkillProgramDrawPhaseMode.Additive ||
                t.Window != (e.Op == SkillProgramEffectOp.GiveCardSkipDrawForEnding ? SkillProgramTriggerWindow.DrawPhaseStarting : e.Op == SkillProgramEffectOp.ResolveHuantuEnding ? SkillProgramTriggerWindow.TurnEnding : SkillProgramTriggerWindow.DyingExited) ||
                t.Subject != (e.Op == SkillProgramEffectOp.DrawDyingExitRoundDistance ? SkillProgramTriggerSubject.Any : SkillProgramTriggerSubject.Owner) ||
                t.Optional != (e.Op != SkillProgramEffectOp.ResolveHuantuEnding) ||
                (e.Op == SkillProgramEffectOp.DrawDyingExitRoundDistance ? t.UsageScope != SkillUsageScope.Game || t.UsageLimit != 1 : t.UsageScope is not null || t.UsageLimit is not null) ||
                t.TurnOwnerScope != (e.Op == SkillProgramEffectOp.ResolveHuantuEnding ? SkillProgramTurnOwnerScope.OtherLiving : SkillProgramTurnOwnerScope.Own))
                throw new InvalidOperationException($"{path}: YanPu requires the exact optional foreign Draw gift, issued actual Ending or limited survived-Dying instruction.");
        }
    }
}
