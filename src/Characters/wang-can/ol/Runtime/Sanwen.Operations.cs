using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record SanwenNamedEntity(int CardId,CardKind Name);
public sealed record SanwenGainProof(long BatchId,int ActualTurn,int OwnerSeat,IReadOnlyList<SanwenNamedEntity> Incoming,IReadOnlyList<SanwenNamedEntity> Witnesses)
{
 private readonly IReadOnlyList<SanwenNamedEntity> _incoming=Array.AsReadOnly(Incoming.ToArray()),_witnesses=Array.AsReadOnly(Witnesses.ToArray());
 public IReadOnlyList<SanwenNamedEntity> Incoming{get=>_incoming;init=>_incoming=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<SanwenNamedEntity> Witnesses{get=>_witnesses;init=>_witnesses=Array.AsReadOnly(value.ToArray());}
}
internal sealed class ResolveSanwenActualGainDuplicatesDescriptor:WangCanDescriptor
{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveSanwenActualGainDuplicates;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardsGained)];}
