using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class AnqiFeirenChecks
{
    private const string Actor = "fixture:anqi-actor", Peer = "fixture:anqi-peer", Driver = "fixture:anqi-driver";
    private const string First = "fixture:anqi-first", Second = "fixture:anqi-second", Quiet = "fixture:anqi-locked", Watch = "fixture:anqi-watch";
    private const string Deck = "fixture:anqi-deck";

    public static void EquippedNativeDamageRandomSkillQuotaTargetTurnExpiryAndCold()
    {
        var s = Scene.Start(); s.Play();
        var weapon = s.V().Hand.Single(c => c.Kind == CardKind.AnqiFeiren).Id;
        Need(s.V().Equipment.Count == 0 && s.V().Skills!.All(skill => skill.ContentId != StandardShanheS5MaPairPackage.AnqiFeirenSkillId),
            "The physical weapon starts in Hand and grants no damage ability before actual equipment use.");
        s.Card(CardKind.AnqiFeiren, []); s.Play();
        Need(s.V().Equipment.Single().Id == weapon && s.G.CardMovements.Any(m => m.CardId == weapon &&
            m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) &&
            s.G.CardMovements.Any(m => m.CardId == weapon && m.From == CardLocation.Processing && m.To == CardLocation.Equipment(0)),
            "Anqi Feiren must pay the original real equipment card from Hand into its weapon slot.");

        var hp = s.V(1).Hp;
        s.Card(CardKind.Slash, [1]); s.Until(() => s.P?.SkillPrompt?.SkillId == Watch);
        var issued = s.E<AnqiFeirenSuppressedEvent>().Single();
        Need(issued is { TargetSeat: 1, TurnNumber: 1, TurnOwnerSeat: 0, CandidateCount: 2 } && issued.WeaponCardId == weapon &&
            issued.Source.OwnerSeat == 0 && issued.SuppressedSkillId is First or Second && issued.RandomIndex is >= 0 and < 2 &&
            s.V(1).Hp == hp && !s.E<DamageAppliedEvent>().Any() &&
            s.G.ResolutionStack.OfType<BeforeDamageProgramWindowFrame>().Any(f => f.Id == issued.DamageWindowId && f.ParentFrameId == issued.DamageFrameId) &&
            SuppressedLimit(s, issued),
            "The equipped weapon randomly disables one current non-locked skill in the genuine positive Slash damage window before HP is paid; the locked skill remains effective. Actual: " +
            JsonSerializer.Serialize(new { Issued = issued, OriginalHp = hp, TargetHp = s.V(1).Hp, PublishedHandLimit = s.V(1).HandLimit,
                EffectiveHandLimit = s.HandLimit(1), Damage = s.E<DamageAppliedEvent>(), Windows = s.G.ResolutionStack.OfType<BeforeDamageProgramWindowFrame>() }));
        s.Cold(); s.Answer(); s.Play();
        Need(s.V(1).Hp == hp - 1 && s.E<DamageAppliedEvent>().Single() is { SourceSeat: 0, TargetSeat: 1, Amount: 1, SourceCardUseResolutionId: not null } &&
            s.E<AnqiFeirenSuppressedEvent>().Length == 1 && SuppressedLimit(s, issued),
            "Returning from the restored native damage child neither repeats the random choice nor pays the original suppression twice.");
        s.Use("hit", [1]);
        Need(s.E<AnqiFeirenSuppressedEvent>().Length == 1 && s.V(1).Hp == hp - 2,
            "A second actual cardless hit on the same recipient in this actual turn still damages normally without suppressing another skill.");
        s.Use("hit", [2]);
        var other = s.E<AnqiFeirenSuppressedEvent>().Single(e => e.TargetSeat == 2);
        Need(s.E<AnqiFeirenSuppressedEvent>().Length == 2 && SuppressedLimit(s, other) &&
            s.E<DamageAppliedEvent>().Last() is { SourceSeat: 0, TargetSeat: 2, SourceCardUseResolutionId: null },
            "A different recipient has its own opportunity, including genuine skill damage without any card use.");
        s.Use("self"); s.Use("source-less"); s.Use("lose");
        Need(s.E<AnqiFeirenSuppressedEvent>().Length == 2 && s.E<DamageAppliedEvent>().Last() is { TargetSeat: 0, SourceLess: true },
            "Self damage, source-less damage and direct HP loss cannot borrow the equipped source for an extra suppression.");
        s.Use("unequip"); s.Use("hit", [3]);
        Need(s.V().Equipment.Count == 0 && s.E<AnqiFeirenSuppressedEvent>().Length == 2 && SuppressedLimit(s, issued),
            "Losing the actual weapon revokes future triggers while its already issued suppression remains in force.");
        s.Cold(); s.End();
        s.Until(() => s.E<TurnEndedEvent>().Any(e => e.ActorSeat == 0 && e.TurnNumber == 1));
        Need(!s.E<AnqiFeirenExpiredEvent>().Any() && SuppressedLimit(s, issued),
            "The damage source's turn ending must not restore the recipient's disabled skill.");
        s.Until(() => s.E<AnqiFeirenExpiredEvent>().Any(e => e.SuppressionFrameId == issued.FrameId));
        Need(s.HandLimit(1) == s.V(1).Hp + 12 && !s.E<AnqiFeirenExpiredEvent>().Any(e => e.SuppressionFrameId == other.FrameId) &&
            s.E<TurnEndedEvent>().Any(e => e.ActorSeat == 1),
            "Only the recipient's own genuine actual-turn end restores its skill; another recipient remains independently suppressed.");
        s.Cold(); s.Play();
        Need(s.HandLimit(2) == s.V(2).Hp + 12 && s.E<AnqiFeirenExpiredEvent>().Length == 2,
            "Both independent suppressions expire once on their own actual-turn ends and remain restored after cold recovery.");
    }

    private static bool SuppressedLimit(Scene s, AnqiFeirenSuppressedEvent e) =>
        s.HandLimit(e.TargetSeat) == s.V(e.TargetSeat).Hp + 12 - (e.SuppressedSkillId == First ? 2 : 3);
    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, G.PlayerCount).Select(seat => G.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        // Public views intentionally publish a HandLimit only for the current
        // turn owner. Query the real rule service for an off-turn target without
        // changing the actual turn or widening that presentation contract.
        public int HandLimit(int seat) => (int)typeof(GameEngine).GetMethod("GetHandLimit", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(G, [((IEnumerable<CharacterState>)typeof(GameEngine).GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(G)!).Single(p => p.Seat == seat)])!;
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        private void Accept(GameCommand command) { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.ToString() ?? "Anqi native command rejected."); }
        public void Answer() { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.Single(c => c.Parameters.GetValueOrDefault("option-id") == "continue").Id, G.Revision)); }
        public void Play() => Until(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Use(string activation, int[]? targets = null)
        { Accept(new UseProgramSkillCommand(0, Driver, activation, [], targets ?? [], G.Revision, P!.PromptId)); Play(); }
        public void Card(CardKind kind, int[] targets)
        {
            var card = V().Hand.First(c => c.Kind == kind);
            var action = G.GetHumanLegalActions().Single(a => a.CardId == card.Id && a.ConversionSource is null && a.TargetSeats.SequenceEqual(targets));
            Accept(new PlayCardCommand(0, card.Id, targets, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId));
        }
        public void Until(Func<bool> ready)
        {
            for (var i = 0; i < 140; i++)
            {
                if (ready()) return;
                Need(G.State.Winner == Winner.None && P?.PlayerSeat != 0, "Unexpected Anqi native boundary: " + JsonSerializer.Serialize(P));
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The bounded Anqi native boundary was not reached.");
        }
        private string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, G.PlayerCount).Select(seat => SnapshotJson.Serialize(G.CreateSnapshot(seat))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), G.CardMovements,
            Checkpoint = GameCheckpointJson.Serialize(G.CreateCheckpoint())
        });
        public void Cold()
        {
            var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State() == before, "Cold replay preserves the real equipped entity, random suppression, current target quota, native damage child and independent target-turn expiries.");
        }
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
                new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new StandardShanheS5MaSiblingsPackage(),
                new StandardShanheS5MaPairPackage(), new Fixture());
            var setup = new ShanheBattleSetup(87, 1, "fixture-anqi", Actor, 10, 6, 0, 0, 0, [],
                [new(CardKind.AnqiFeiren, Suit.Spade, 5), new(CardKind.Slash, Suit.Heart, 7)], [],
                [new(Peer, Hp: 12, MaxHpBonus: 8) { StartingHand = [] }, new(Peer, Hp: 12, MaxHpBonus: 8) { StartingHand = [] },
                 new(Peer, Hp: 12, MaxHpBonus: 8) { StartingHand = [] }], [], new string('e', 64));
            var g = GameEngine.CreateStandard(new()
            {
                Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8
            }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anqi-native", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
             {"id":"{{{Driver}}}","revision":1,"activations":[
              {"id":"hit","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1,"sourceRef":{"kind":"owner"}}]},
              {"id":"self","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1,"sourceRef":{"kind":"owner"}}]},
              {"id":"source-less","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"receiveOwnerDamage","target":"owner","amount":1}]},
              {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
              {"id":"unequip","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["equipment"]}]}]},
             {"id":"{{{First}}}","revision":1,"modifiers":[{"id":"first","query":"handLimit","operation":"add","value":2,"priority":0}]},
             {"id":"{{{Second}}}","revision":1,"modifiers":[{"id":"second","query":"handLimit","operation":"add","value":3,"priority":0}]},
             {"id":"{{{Quiet}}}","revision":1,"modifiers":[{"id":"locked","query":"handLimit","operation":"add","value":7,"priority":0}],"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
             {"id":"{{{Watch}}}","revision":1,"triggers":[{"id":"native-damage-child","window":"beforeDamageApplied","subject":"any","priority":-10,"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var names = new Dictionary<string, object>
            {
                [Driver] = new { name = "真实伤害及卸装", description = "原生伤害、失血与真实装备移动" },
                [First] = new { name = "普通手牌上限二", description = "可随机失效的有效技能" },
                [Second] = new { name = "普通手牌上限三", description = "第二个可随机失效的有效技能" },
                [Quiet] = new { name = "锁定上限及静默", description = "保留锁定效果并跳过自主出牌" },
                [Watch] = new { name = "真实伤害窗口孩子", description = "已失效技能后暂停原伤害", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
            };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, program) in catalog.Programs)
                b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id], Tags = id == Quiet ? SkillTag.Locked : SkillTag.None });
            b.AddGeneral(new(Actor, "真实持刀者", "supporter", Driver, "qun", 4, [Watch]));
            b.AddGeneral(new(Peer, "真实失效目标", "supporter", First, "qun", 4, [Second, Quiet]));
            b.AddDeck(new(Deck, "固定真实基本牌库", 0, 0, [])
            { PhysicalCards = Enumerable.Range(0, 32).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "飞刃真实武器小局", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Actor, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
