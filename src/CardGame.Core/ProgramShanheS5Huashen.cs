using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum ShanheS5AvatarChoiceStage { Mode, Exchange }
public sealed record ShanheS5AvatarChoice(PrivateGeneralLibrarySource Source, long LibraryRevision, ShanheS5AvatarChoiceStage Stage)
{
    private IReadOnlyList<string> _selected = Array.AsReadOnly(Array.Empty<string>());
    public IReadOnlyList<string> SelectedGeneralIds
    { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShanheS5AvatarChoice? ShanheS5AvatarChoice { get; init; }
}
public sealed record ShanheS5AvatarsExchangedEvent(long ProgramFrameId, PrivateGeneralLibrarySource Source, int RemovedCount, int GainedCount) : IGameEvent;

internal interface IShanheS5HuashenHost
{ SkillProgramStepOutcome ChooseOrExchangeShanheS5Avatar(SkillProgramEffect effect, ProgramSkillFrame frame); }

internal sealed class ChooseOrExchangeShanheS5AvatarDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseOrExchangeShanheS5Avatar;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ISkillProgramEffectHandler Handler { get; } = new ChooseOrExchangeShanheS5AvatarHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(1));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "excludedSkillTags", "condition");
        var tags = reader.RequiredEnumArray<SkillTag>("excludedSkillTags");
        if (tags.Distinct().Count() != tags.Count || tags.Any(t => t is not (SkillTag.Limited or SkillTag.Awakening or SkillTag.Lord)))
            throw new InvalidOperationException(reader.Path + ": invalid private avatar skill exclusions.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0, reader.Condition())
        { GeneralLibraryPolicy = new(Array.AsReadOnly(tags.ToArray())) };
        RequireAlways(effect, reader.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindows([SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, SkillProgramTriggerWindow.AfterTurnEnded])];
}
public sealed class ChooseOrExchangeShanheS5AvatarHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseOrExchangeShanheS5Avatar;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IShanheS5HuashenHost>().ChooseOrExchangeShanheS5Avatar(effect, frame);
}
