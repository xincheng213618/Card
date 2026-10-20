using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class SpSunShangxiangOlChecks
{
    public static void ActualRecoveryHelpAndFanxiang()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var setup = new ShanheBattleSetup(7, 1, "sp-sun-shangxiang", Owner, 3, 0, 0, 0, 0, [],
            [new(CardKind.Crossbow, Suit.Club, 7), new(CardKind.Dodge, Suit.Spade, 7)], [],
            [new(EnemyA, Hp: 8), new(EnemyB, Hp: 8)], [new(Ally, Hp: 8)], new string('a', 64));
        var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
            HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        PendingDecision? P() => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
        T[] E<T>() => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
        void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        void Accept(GameCommand c) { var result = g.Submit(c); Need(result.Accepted, result.Error?.Message ?? "Real command rejected."); }
        void Reach(Func<bool> done)
        {
            for (var i = 0; i < 160; i++)
            {
                if (done()) return;
                Need(g.State.Winner == Winner.None, "The native small game ended before Liangzhu/Fanxiang returned.");
                if (P() is { PlayerSeat: 0 } p) throw new InvalidOperationException("Unexpected Human choice: " + p.Prompt);
                Accept(new AdvanceOneStepCommand(g.Revision));
            }
            throw new InvalidOperationException("SP Sun Shangxiang native flow not reached;turn=" + g.State.TurnNumber + ";phase=" + g.State.Phase + ";frames=" +
                string.Join(';', g.ResolutionStack.Select(f => f.GetType().Name + '/' + f.Id + (f is ProgramSkillFrame p ? "/" + p.SkillId + "/" + p.InstructionIndex + "/" + JsonSerializer.Serialize(p.SpSunShangxiangLiangzhu) : ""))));
        }
        void Play() => Reach(() => P() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        void Answer(Func<PromptChoice, bool> choose)
        { var p = P()!; Need(p.PlayerSeat == 0, "Only Human inputs are submitted."); Accept(new AnswerPromptCommand(0, p.PromptId, p.Choices.First(choose).Id, g.Revision)); }
        void Drive(string id, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], g.Revision, P()!.PromptId));
        Accept(new StartGameCommand()); Play();
        Drive("wound-self"); Play(); Drive("recover-self");
        Reach(() => P()?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "self") == true);
        Need(E<SpSunShangxiangLiangzhuStartedEvent>() is [{ RecoveredSeat: 0 }] && E<SpSunShangxiangRecoveryQualifiedEvent>() is [{ HpBefore: 1, HpAfter: 3 }],
            "One genuine two-point Recovery in her own actual Play creates one Liangzhu occurrence and includes herself.");
        Answer(c => c.Parameters.GetValueOrDefault("branch") == "self"); Play();
        Need(E<SpSunShangxiangLiangzhuDrawEvent>() is [{ RecipientSeat: 0, Requested: 1, Actual: 1 }] && E<SpSunShangxiangHelpedEvent>().Length == 0,
            "Self Draw1 is native and does not manufacture a branch-two assisted seat.");
        Drive("foreign-heal", targets: [1]); Play();
        Need(E<SpSunShangxiangLiangzhuStartedEvent>().Length == 1,
            "Healing another role during Sun Shangxiang's Play is not that role's own Play and cannot trigger Liangzhu.");
        Accept(new EndPlayPhaseCommand(0, g.Revision, P()!.PromptId));
        Reach(() => P()?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "help") == true);
        Need(E<SpSunShangxiangLiangzhuStartedEvent>().Last().RecoveredSeat == 1 && g.State.Phase == TurnPhase.Play,
            "The real ally's own actual Play recovery reaches the distinct SP owner through the exact native recovery window.");
        var before = SnapshotJson.Serialize(g.CreateSnapshot(0));
        g = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(before == SnapshotJson.Serialize(g.CreateSnapshot(0)) && E<SpSunShangxiangHelpedEvent>().Length == 0,
            "One representative pending foreign-recovery branch restores its exact parent without issuing draw or help early.");
        Answer(c => c.Parameters.GetValueOrDefault("branch") == "help");
        Reach(() => E<SpSunShangxiangFanxiangCompletedEvent>().Length == 1); Play();
        var owner = g.CreateSnapshot(0).Players[0];
        Need(E<SpSunShangxiangHelpedEvent>() is [{ OwnerSeat: 0, HelpedSeat: 1 }] && E<SpSunShangxiangLiangzhuDrawEvent>().Last() is { RecipientSeat: 1, Requested: 2, Actual: 2 } &&
            E<SpSunShangxiangFanxiangAcceptedEvent>() is [{ Receipt.QualifiedSeat: 1, Receipt.MaximumBefore: 3 }] && owner.MaxHp == 4 && owner.Hp == 4 &&
            owner.Skills!.Any(s => s.Id == Xiaoji) && owner.Skills.All(s => s.Id != Liangzhu) && E<SkillAwakenedEvent>().Count(e => e.PlayerSeat == 0 && e.SkillId == Fanxiang) == 1,
            "The stable assisted ally remains alive and wounded; next real own Preparation increases MaxHP, genuinely recovers1, loses Liangzhu and grants existing Xiaoji once.");
        var equip = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip);
        var cardId = equip.CardId!.Value;
        Accept(new PlayCardCommand(0, cardId, equip.TargetSeats, g.Revision, P()!.PromptId, equip.PlayedCardKind, equip.TargetCardId) { ConversionSource = equip.ConversionSource }); Play();
        Drive("discard-equipment", [cardId]); Reach(() => P()?.SkillPrompt?.SkillId == Xiaoji);
        Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Play();
        Need(g.CardMovements.Any(m => m.CardId == cardId && m.From == CardLocation.Equipment(0) && m.To == CardLocation.DiscardPile) &&
            E<ProgramBindingStartedEvent>().Any(e => e.OwnerSeat == 0 && e.SkillId == Xiaoji) && E<SpSunShangxiangFanxiangCompletedEvent>().Length == 1,
            "The acquired existing Xiaoji actually observes native equipment loss and returns its normal draw; awakening is not repeated.");
    }
    private const string Liangzhu = "ol:liangzhu", Fanxiang = "ol:fanxiang", Xiaoji = "classic:xiaoji", Driver = "fixture:spssx-driver", Heal = "fixture:spssx-heal", Quiet = "fixture:spssx-quiet",
        Owner = "fixture:spssx-owner", Ally = "fixture:spssx-ally", EnemyA = "fixture:spssx-enemy-a", EnemyB = "fixture:spssx-enemy-b", Deck = "fixture:spssx-deck";
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-sp-sun-shangxiang-ol", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, ["ol:sp-sun-shangxiang", "classic:sun-shangxiang"]);
            foreach (var kind in new[] { CardKind.Dodge, CardKind.Crossbow })
            { var card = CardCatalog.Get(kind); b.AddCard(new("fixture:spssx-" + kind, card.DisplayName, card.CategoryName, card.Description, kind)); }
            var rules = "{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[" +
                "{\"id\":\"" + Driver + "\",\"revision\":1,\"activations\":[" +
                "{\"id\":\"wound-self\",\"minCards\":0,\"maxCards\":0,\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"usesPerTurn\":null,\"effects\":[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":2}]}," +
                "{\"id\":\"recover-self\",\"minCards\":0,\"maxCards\":0,\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"usesPerTurn\":null,\"effects\":[{\"op\":\"recover\",\"target\":\"owner\",\"amount\":2}]}," +
                "{\"id\":\"foreign-heal\",\"minCards\":0,\"maxCards\":0,\"minTargets\":1,\"maxTargets\":1,\"targetKind\":\"otherLiving\",\"usesPerTurn\":null,\"effects\":[{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":1},{\"op\":\"recover\",\"target\":\"selectedTarget\",\"amount\":1}]}," +
                "{\"id\":\"discard-equipment\",\"minCards\":1,\"maxCards\":1,\"sourceZones\":[\"equipment\"],\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"usesPerTurn\":null,\"effects\":[{\"op\":\"discardSelected\",\"target\":\"owner\",\"amount\":1}]}]}," +
                "{\"id\":\"" + Heal + "\",\"revision\":1,\"triggers\":[{\"id\":\"native-own-play-recovery\",\"window\":\"playPhaseStarting\",\"subject\":\"owner\",\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":2},{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1}]}]}," +
                "{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}]}";
            var labels = new Dictionary<string, string> { [Driver] = "Native recovery driver", [Heal] = "Native own-Play heal", [Quiet] = "Wait" };
            var programs = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = labels.ToDictionary(e => e.Key, e => new { name = e.Value, description = e.Value }) }));
            foreach (var (id, program) in programs.Programs) b.AddSkill(new(id, labels[id], labels[id]) { Program = program, ProgramPresentation = programs.Presentations[id] });
            b.AddGeneral(new(Owner, "SP Sun Shangxiang", "supporter", Liangzhu, "shu", 3, [Fanxiang, Driver], Gender: GeneralGender.Female));
            b.AddGeneral(new(Ally, "Native healed ally", "supporter", Heal, "shu", 8));
            foreach (var id in new[] { EnemyA, EnemyB }) b.AddGeneral(new(id, "Waiting peer", "supporter", Quiet, "wei", 8));
            b.AddDeck(new(Deck, "Small fixed recovery stock", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 18).Select(_ => new ContentDeckPhysicalCard("fixture:spssx-Dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "SP Sun Shangxiang real native game", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Ally, EnemyA, EnemyB], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> {
                    [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
