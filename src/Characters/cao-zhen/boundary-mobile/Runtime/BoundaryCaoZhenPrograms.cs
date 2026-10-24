using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface IBoundaryCaoZhenHost { SkillProgramStepOutcome Execute(ProgramSkillFrame frame, SkillProgramEffect effect); }
internal sealed class BoundaryCaoZhenHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) => host.GetCapability<IBoundaryCaoZhenHost>().Execute(frame, effect);
}
internal abstract class BoundaryCaoZhenDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new BoundaryCaoZhenHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, context) => context.PublicControlValue(2));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        if (Op == SkillProgramEffectOp.PrepareBoundaryCaoZhenSidi) return ParseOwnerAlways(reader);
        reader.AllowOnly("op", "target", "resultBind", "condition");
        var effect = new SkillProgramEffect(Op, reader.RequiredEnum<SkillProgramEffectTarget>("target"), 0, reader.Condition(), resultBind: reader.RequiredIdentifier("resultBind"));
        if (effect.Target != SkillProgramEffectTarget.Owner) throw new InvalidOperationException("Sidi requires its owning observer.");
        RequireAlways(effect, reader.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        Op == SkillProgramEffectOp.PrepareBoundaryCaoZhenSidi ? [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)] :
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted), new CreateChoiceResult(effect.ResultBind!, ["none", "self", "draw", "cancel-only", "cancel-damage"])];
}
internal sealed class PrepareBoundaryCaoZhenSidiDescriptor : BoundaryCaoZhenDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.PrepareBoundaryCaoZhenSidi; }
internal sealed class ResolveBoundaryCaoZhenSidiDescriptor : BoundaryCaoZhenDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveBoundaryCaoZhenSidi; }

public sealed record BoundarySidiPrediction(long Id, int OwnerSeat, string SkillInstanceId, string GameplayHash, int HolderSeat, int TargetSeat);
public sealed record BoundarySidiPredictionView(int HolderSeat, int TargetSeat);
public enum BoundarySidiStage { Holder, Target, Benefit }
public sealed record BoundarySidiDraft(int InstructionIndex, BoundarySidiStage Stage, long WindowId, long ActionId, int HolderSeat = -1, int PredictedSeat = -1, string? ResultBind = null);
public sealed record BoundarySidiCancelledEvent(long ProgramFrameId, long UseFrameId, long ActionId, int OwnerSeat, string SkillInstanceId, string GameplayHash) : IGameEvent;
public sealed record BoundarySidiForecastRecordedEvent(long FrameId, int OwnerSeat) : IGameEvent;
public sealed record BoundarySidiResultEvent(long FrameId, int OwnerSeat, long ActionId, string ResultBind, string Outcome) : IGameEvent;
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public BoundarySidiDraft? BoundarySidi { get; init; } }
public sealed partial record CardUseFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public BoundarySidiCancelledEvent? BoundarySidiCancellation { get; init; } }
public sealed partial class CharacterState { internal IReadOnlyList<BoundarySidiPrediction>? BoundarySidiPredictions { get; set; } }
public sealed partial record GameSnapshot { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<BoundarySidiPredictionView>? BoundarySidiPredictions { get; init; } }

internal sealed class BoundarySidiCommittedContract : IProgramCommittedUseContract
{
    public string SkillId => "boundary:sidi-mobile";
    public bool AllowsSingleActionInstance => true;
    public void ValidateTrigger(SkillProgramTrigger trigger)
    {
        if (trigger.OwnerRelation != SkillProgramCardActionOwnerRelation.Observer || !trigger.IncludeResponseUses || !trigger.SingleActionInstance || trigger.AllowNoEventTarget ||
            !(trigger is { Id: "prepare-forecast", Window: SkillProgramTriggerWindow.CardUseCompleted, Optional: true, Effects: [{ Op: SkillProgramEffectOp.PrepareBoundaryCaoZhenSidi }] } ||
              trigger is { Id: "consume-first-use", Window: SkillProgramTriggerWindow.CardUseCommitted, Optional: false, Effects: [{ Op: SkillProgramEffectOp.ResolveBoundaryCaoZhenSidi }, { Op: SkillProgramEffectOp.Draw }, { Op: SkillProgramEffectOp.Draw }, { Op: SkillProgramEffectOp.Damage }] }))
            throw new InvalidOperationException("Mobile Sidi requires its exact completed forecast and first committed true-use observer contracts.");
    }
    [ProgramCatalogValidation(53400)]
    private static void Validate(string path, SkillProgram program)
    {
        if (program.Id != "boundary:sidi-mobile") return;
        if (program.Triggers.Count != 2 || program.Activations.Count != 0) throw new InvalidOperationException(path + ": Sidi requires both native-use triggers.");
        foreach (var trigger in program.Triggers) new BoundarySidiCommittedContract().ValidateTrigger(trigger);
    }
}
