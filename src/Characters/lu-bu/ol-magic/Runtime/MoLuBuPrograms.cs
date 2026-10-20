using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record MoLuBuInitialQi(int OriginalOwnerSeat, int CardId, CardKind PrintedKind, long EntrySequence);
public sealed partial record ProgramLifecycleTriggerWindowFrame
{
    private readonly IReadOnlyList<MoLuBuInitialQi>? _moInitial;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<MoLuBuInitialQi>? MoLuBuInitialQi { get => _moInitial; init => _moInitial = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed record MoLuBuOpeningReceipt(long IssuerFrameId, CardConversionSource Source, string GameplayHash,
    int NormalSeat, int ActualTurn, bool PlayStarted = false);
public sealed partial record ActualTurnProgression
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MoLuBuOpeningReceipt? MoLuBuOpening { get; init; }
}
public enum MoLuBuStage { QiChoice, Claiming, MovementChildren, PairChoice, EntryChildren, Complete }
public sealed record MoLuBuQiCandidate(int CardId, CardLocation From);
public sealed record MoLuBuReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation,
    long WindowId, int OriginalOwnerSeat, int ActualTurn, MoLuBuStage Stage, bool Reassign = false,
    int Index = 0, long Before = 0, long After = 0, MoLuBuStage AfterChildren = MoLuBuStage.Complete)
{
    private readonly IReadOnlyList<MoLuBuQiCandidate> _candidates = Array.AsReadOnly(Array.Empty<MoLuBuQiCandidate>());
    public IReadOnlyList<MoLuBuQiCandidate> Candidates { get => _candidates; init => _candidates = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MoLuBuReceipt? MoLuBu { get; init; }
}
public sealed record MoLuBuQiMarkedEvent(long FrameId, int OriginalOwnerSeat, int CardId, CardKind PrintedKind, long EntrySequence) : IGameEvent;
public sealed record MoLuBuInitializedEvent(long FrameId, CardConversionSource Source, string GameplayHash, long WindowId) : IGameEvent;
public sealed record MoLuBuOpeningStartedEvent(long IssuerFrameId, CardConversionSource Source, int ActualTurn, int NormalSeat) : IGameEvent;
public sealed record MoLuBuOpeningCompletedEvent(long IssuerFrameId, int OwnerSeat, int ActualTurn, int NormalSeat) : IGameEvent;
public sealed record MoLuBuActualUseQiEvent(long ActionId, int ActorSeat, bool IsQi) : IGameEvent;
public sealed record MoLuBuLegacyDuelUseEvent(long ProducerFrameId, int InstructionIndex, int ActorSeat) : IGameEvent;
public sealed record MoLuBuStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    SkillProgramEffectOp Operation, long WindowId, int OriginalOwnerSeat, int ActualTurn, bool Reassign) : IGameEvent;
public sealed record MoLuBuQiMovedEvent(long FrameId, int CardId, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record MoLuBuMovementSignedEvent(long FrameId, int CardId, CardKind Kind, CardLocation From, CardLocation To, CardMoveReason Reason, long Sequence) : IGameEvent;
public sealed record MoLuBuPairChangedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int OpponentSeat, int ActualTurn, bool Entry) : IGameEvent;
public sealed record MoLuBuKuangmoEnteredEvent(long FrameId, CardConversionSource Source, string GameplayHash, ActualDiscardRecoveryPhaseKey Phase, int OpponentSeat) : IGameEvent;
public sealed record MoLuBuCompletedEvent(long FrameId) : IGameEvent;

internal interface IMoLuBuHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class MoLuBuDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new MoLuBuHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(6));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op switch
    {
        SkillProgramEffectOp.InitializeMoLuBuQiAndOpeningPlay => [new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting)],
        SkillProgramEffectOp.ResolveMoLuBuDuoqi => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)],
        SkillProgramEffectOp.ResolveMoLuBuKuangmoDeath => [new RequireTriggerWindow(SkillProgramTriggerWindow.CharacterDied)],
        _ => [new ReadSelectedTarget(), new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving)]
    };
}
internal sealed class InitializeMoLuBuQiDescriptor : MoLuBuDescriptor {
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
 public override SkillProgramEffectOp Op => SkillProgramEffectOp.InitializeMoLuBuQiAndOpeningPlay; }
internal sealed class ResolveMoLuBuDuoqiDescriptor : MoLuBuDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMoLuBuDuoqi; }
internal sealed class EnterMoLuBuKuangmoDescriptor : MoLuBuDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.EnterMoLuBuKuangmo; }
internal sealed class ResolveMoLuBuKuangmoDeathDescriptor : MoLuBuDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMoLuBuKuangmoDeath; }
internal sealed class MoLuBuHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMoLuBuHost>().Resolve(f, op);
}
internal static class MoLuBuComposition
{
    internal const string Duoqi = "ol:mo-duoqi", Kuangmo = "ol:mo-kuangmo", Gangquan = "ol:mo-gangquan";
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.InitializeMoLuBuQiAndOpeningPlay or SkillProgramEffectOp.ResolveMoLuBuDuoqi or SkillProgramEffectOp.EnterMoLuBuKuangmo or SkillProgramEffectOp.ResolveMoLuBuKuangmoDeath;
    [ProgramCatalogValidation(1100)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var owned = p.Id is Duoqi or Kuangmo or Gangquan;
        if (!owned) { if (p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => Handles(e.Op))) throw new InvalidOperationException(path + ": OL Lu Bu operations require their complete original source."); return; }
        bool Plain(SkillProgramEffect e, SkillProgramEffectOp op) => e.Op == op && e.Target == SkillProgramEffectTarget.Owner && e.Condition.Kind == SkillProgramConditionKind.Always;
        var valid = p.Id switch
        {
            Duoqi => p.Activations.Count == 0 && p.ViewAs.Count == 0 && p.Triggers is [var initial, var damage] &&
                initial.Id == "initial-qi-opening-play" && initial.Window == SkillProgramTriggerWindow.GameStarting && initial.Subject == SkillProgramTriggerSubject.Owner && !initial.Optional && initial.Effects.Count == 1 && Plain(initial.Effects[0], SkillProgramEffectOp.InitializeMoLuBuQiAndOpeningPlay) &&
                damage.Id == "first-victim-damage-qi" && damage.Window == SkillProgramTriggerWindow.AfterDamageApplied && damage.Subject == SkillProgramTriggerSubject.DamageSource && !damage.Optional && damage.Effects.Count == 1 && Plain(damage.Effects[0], SkillProgramEffectOp.ResolveMoLuBuDuoqi),
            Kuangmo => p.ViewAs.Count == 0 && p.Activations is [var enter] && enter.Id == "enter-and-pair" && enter.MinCards == 0 && enter.MaxCards == 0 && enter.MinTargets == 1 && enter.MaxTargets == 1 && enter.TargetKind == SkillProgramTargetKind.OtherLiving && enter.Effects.Count == 1 && Plain(enter.Effects[0], SkillProgramEffectOp.EnterMoLuBuKuangmo) &&
                p.Triggers is [var death] && death.Id == "pair-death-and-qi" && death.Window == SkillProgramTriggerWindow.CharacterDied && death.Subject == SkillProgramTriggerSubject.Owner && !death.Optional && death.Effects.Count == 1 && Plain(death.Effects[0], SkillProgramEffectOp.ResolveMoLuBuKuangmoDeath),
            _ => p.Triggers.Count == 0 && p.Activations.Count == 0 && p.ViewAs is [var fire, var duel] && fire.Id == "qi-equipment-fire-slash" && fire.OutputKind == CardKind.FireSlash && fire.InputCount == 1 && fire.InputCategories.SequenceEqual([SkillProgramCardCategory.Equipment]) && fire.ForPlay && fire.ForResponse && fire.ExtendedUse && !fire.UseOnly &&
                duel.Id == "nonqi-trick-duel" && duel.OutputKind == CardKind.Duel && duel.InputCount == 1 && duel.InputCategories.SequenceEqual([SkillProgramCardCategory.Trick]) && duel.ForPlay && !duel.ForResponse && duel.SingleCardTrickUse
        };
        if (!valid || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.BooleanStates.Count != 0) throw new InvalidOperationException(path + ": OL Lu Bu requires complete original Qi, reciprocal Kuangmo and two-state native Gangquan programs.");
    }
}
