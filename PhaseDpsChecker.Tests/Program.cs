using Newtonsoft.Json.Linq;
using PhaseDpsChecker.Combat;

var tests = new (string Name, Action Run)[]
{
    ("DPS と Crit/DH 率", DamageRatesAndDps),
    ("パーティ対象の回復のみ集計", HealingTargets),
    ("GCD 区間から Active% を算出", GcdActiveRate),
    ("DoT tick は使用回数を増やさない", PeriodicTickDoesNotCountAsUse),
    ("履歴削除後も Phase 番号を維持", PhaseNumberAfterTrim),
    ("全滅時の履歴保存と現在表示クリア", ArchiveCombatHistory),
	("被ダメージとステータスを履歴へ保存", ArchiveIncomingDamage),
	("撃破したアンカーへの最終攻撃を判定", DefeatingAnchorHit),
	("絶妖星乱舞の専用フェーズ遷移", FuturesRewrittenPhaseTransitions),
	("絶妖星乱舞 Phase 2 は敵視リスト消失で終了", FuturesRewrittenEnemyListTransition),
	("絶妖星乱舞 Phase 3 はメテオ中断ログで終了", FuturesRewrittenPhase3BattleLogTransition),
	("リプレイのジョブ名メンバーを識別", ReplayPartyMemberResolution),
	("追加リキャストグループの GCD 判定", AdditionalCooldownGroupGcd),
	("被ダメージ時ステータスを防御系に限定", DefensiveStatusesOnly),
	("履歴JSONの保存と復元", HistoryPersistenceRoundTrip),
	("v0.11.2以前の不正なIINACT履歴をアクション実測へ修復", LegacyHistoryMismatchMigration),
	("詠唱中断ログの解析と集計", InterruptedCastCounting),
	("履歴を個別に削除", DeleteIndividualHistory),
	("履歴ファイル容量の境界判定", HistoryFileSizeThresholds),
	("IINACT累積値をPhase差分へ変換", IinactPhaseDelta),
	("inactiveの古いIINACT集計を新Phaseへ適用しない", IinactInactiveSnapshotIgnored),
	("Encounter title変更後もIINACT差分を維持", IinactTitleChangeKeepsBaseline),
	("IINACT集計とアクション内訳を完全一致", IinactActionReconciliation),
	("IINACTのYOU表記をローカルプレイヤーへ対応", IinactYouAlias),
	("IINACT CombatData JSONを解析", IinactCombatDataParsing),
	("IINACT Legacy IPCのCombatDataを展開", IinactLegacyCombatDataExtraction),
	("IINACT CombatDataの開始・END遷移を検知", IinactEncounterTransitions),
	("IPCとWebSocketのどちらのENDも検知", IinactMultiSourceEndTransition),
	("mopimopi URLからWebSocket接続先を解決", IinactWebSocketEndpointResolution),
};

foreach (var test in tests)
{
    test.Run();
    Console.WriteLine($"PASS: {test.Name}");
}

Console.WriteLine($"{tests.Length} tests passed.");

static void DamageRatesAndDps()
{
    var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    var party = new Dictionary<uint, string> { [1] = "Player One", [2] = "Player Two" };
    var partyIds = party.Keys.ToHashSet();
    var aggregator = new CombatAggregator();
    aggregator.BeginPhase(t0, party, 900);
    aggregator.RecordAction(Event(t0, 1, 10, "Strike", new EffectSample(900, 1000, 0, true, true)), partyIds);
    aggregator.RecordAction(Event(t0.AddSeconds(5), 1, 10, "Strike", new EffectSample(900, 500, 0, false, true)), partyIds);
    aggregator.EndCurrentPhase(t0.AddSeconds(10));
    var phase = aggregator.Phases.Single();
    var player = phase.Players[1];
    Equal(1500L, player.TotalDamage, "total damage");
    Near(150, player.Dps(phase.DurationSeconds(t0.AddSeconds(10))), 0.001, "DPS");
    Near(0.5, player.CriticalRate, 0.001, "critical rate");
    Near(1.0, player.DirectHitRate, 0.001, "direct-hit rate");
    Near(0.5, player.CriticalDirectHitRate, 0.001, "critical direct-hit rate");
    Equal(1000u, player.MaximumDamage, "maximum damage");
    Equal(2, player.Actions[10].UseCount, "use count");
	AssertActionTotalsMatch(player, "ActionEffect fallback exact totals");
}

static void HealingTargets()
{
    var t0 = DateTime.UtcNow;
    var party = new Dictionary<uint, string> { [1] = "Healer", [2] = "Tank" };
    var partyIds = party.Keys.ToHashSet();
    var aggregator = new CombatAggregator();
    aggregator.BeginPhase(t0, party, 900);
    var effects = new[]
    {
        new EffectSample(2, 0, 3000, true, false),
        new EffectSample(900, 0, 9999, false, false),
        new EffectSample(2, 1234, 0, false, false),
    };
    aggregator.RecordAction(new CombatActionEvent(t0, 1, "Healer", 20, "Heal", ActionKind.Magic, true, true, 2.5, effects), partyIds);
    var player = aggregator.CurrentPhase!.Players[1];
    Equal(3000L, player.TotalHealing, "party healing");
	Near(300.0, player.Hps(10), 0.001, "HPS");
    Equal(0L, player.TotalDamage, "friendly damage excluded");
	aggregator.EndCurrentPhase(t0.AddSeconds(10));
	Near(0.0, player.DamageActiveRate(t0, t0.AddSeconds(10)), 0.001, "healing GCD excluded from damage active");
	Near(0.25, player.HealingActiveRate(t0, t0.AddSeconds(10)), 0.001, "healing GCD included in healing active");
	AssertActionTotalsMatch(player, "healing exact totals");
}

static void GcdActiveRate()
{
    var t0 = DateTime.UtcNow;
    var party = new Dictionary<uint, string> { [1] = "Player" };
    var partyIds = party.Keys.ToHashSet();
    var aggregator = new CombatAggregator();
    aggregator.BeginPhase(t0, party, 900);
    aggregator.RecordAction(Event(t0, 1, 10, "GCD", new EffectSample(900, 100, 0, false, false), true, 2.5), partyIds);
    aggregator.RecordAction(Event(t0.AddSeconds(2.5), 1, 10, "GCD", new EffectSample(900, 100, 0, false, false), true, 2.5), partyIds);
	aggregator.RecordAction(new CombatActionEvent(t0.AddSeconds(5), 1, "Player", 11, "DoT GCD", ActionKind.WeaponSkill, true, true, 2.5, [], true), partyIds);
    aggregator.EndCurrentPhase(t0.AddSeconds(10));
    var phase = aggregator.Phases.Single();
	Near(0.75, phase.Players[1].ActiveRate(phase.StartedAt, phase.EndedAt!.Value), 0.001, "active rate");
	Near(0.75, phase.Players[1].DamageActiveRate(phase.StartedAt, phase.EndedAt!.Value), 0.001, "damage active rate");
	Near(0.0, phase.Players[1].HealingActiveRate(phase.StartedAt, phase.EndedAt!.Value), 0.001, "healing active rate");
}

static void AdditionalCooldownGroupGcd()
{
	(bool isGcd, double duration) = ActionGcdClassifier.Resolve(ActionKind.WeaponSkill, 15, 58, 200);
	Equal(true, isGcd, "additional cooldown group is GCD");
	Near(2.5, duration, 0.001, "additional GCD uses shared duration");

	(bool magicIsGcd, double magicDuration) = ActionGcdClassifier.Resolve(ActionKind.Magic, 58, 0, 25);
	Equal(true, magicIsGcd, "primary cooldown group is GCD");
	Near(2.5, magicDuration, 0.001, "primary GCD duration");

	(bool abilityIsGcd, _) = ActionGcdClassifier.Resolve(ActionKind.Ability, 15, 58, 200);
	Equal(false, abilityIsGcd, "abilities are not GCD actions");
}

static void DefensiveStatusesOnly()
{
	Equal(true, DefensiveStatusFilter.IsAllowed("Rampart"), "defensive ability");
	Equal(true, DefensiveStatusFilter.IsAllowed("野戦治療の陣"), "ground healing ability");
	Equal(true, DefensiveStatusFilter.IsAllowed("アサイラム"), "ground healing buff");
	Equal(false, DefensiveStatusFilter.IsAllowed("Iron Will"), "tank stance excluded");
	Equal(false, DefensiveStatusFilter.IsAllowed("Grit"), "tank stance excluded");
	Equal(false, DefensiveStatusFilter.IsAllowed("Sprint"), "sprint excluded");
	Equal(false, DefensiveStatusFilter.IsAllowed("食事効果"), "food excluded");
	Equal(false, DefensiveStatusFilter.IsAllowed("Reassembled"), "offensive buff excluded");
}

static void HistoryPersistenceRoundTrip()
{
	string directory = Path.Combine(Path.GetTempPath(), $"PhaseDpsCheckerTests-{Guid.NewGuid():N}");
	try
	{
		var t0 = new DateTime(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);
		var party = new Dictionary<uint, string> { [1] = "Machinist" };
		var partyIds = party.Keys.ToHashSet();
		var aggregator = new CombatAggregator();
		aggregator.BeginPhase(t0, party, 900);
		aggregator.RecordAction(Event(t0, 1, 10, "Drill", new EffectSample(900, 5000, 0, true, true), true, 2.5), partyIds);
		aggregator.RecordInterruptedCast(1, "Machinist", 20, "ケアルガ", ActionKind.Magic, isHealingAction: true);
		aggregator.RecordIncomingDamage(new IncomingDamageEvent(t0.AddSeconds(2), 1, "Machinist", 900, "Enemy", 20, "Attack", 1000, [new CombatStatusSnapshot(1191, "Rampart", 0, 8)]), partyIds);
		aggregator.CurrentPhase!.SetIinactIncomingDamage(1, 1234);
		aggregator.CurrentPhase.MarkIinactSynchronized(7);
		aggregator.ArchiveCurrent(t0.AddSeconds(10), CombatHistoryEndReason.Wipe);

		var store = new CombatHistoryStore(() => directory, directory, (_, _) => { });
		Equal(true, store.Save(aggregator.Histories), "history saved");
		IReadOnlyList<CombatHistoryRecord> loaded = store.Load();
		Equal(1, loaded.Count, "history count restored");
		PlayerPhaseStatistics player = loaded.Single().Phases.Single().Players[1];
		Equal(5000L, player.TotalDamage, "damage restored");
		Equal("Drill", player.Actions[10].ActionName, "action restored");
		Equal(1, player.Actions[20].InterruptedCastCount, "interrupted cast restored");
		Equal(true, player.Actions[20].IsHealingAction, "healing action classification restored");
		Near(0.25, player.DamageActiveRate(t0, t0.AddSeconds(10)), 0.001, "damage active restored");
		Equal(1000u, loaded.Single().Phases.Single().IncomingDamageEvents.Single().Amount, "incoming damage restored");
		Equal(1234L, loaded.Single().Phases.Single().IinactIncomingDamageTotals[1], "IINACT incoming total restored");
		Equal(true, loaded.Single().Phases.Single().HasIinactData, "IINACT source marker restored");
		AssertActionTotalsMatch(player, "persisted history exact totals");
	}
	finally
	{
		if (Directory.Exists(directory))
		{
			Directory.Delete(directory, true);
		}
	}
}

static void InterruptedCastCounting()
{
	Equal(true, CastInterruptionParser.TryParse("Alice Exampleは「鼓舞激励の策」の詠唱を中断した。", out CastInterruption interruption), "Japanese interruption parsed");
	Equal("Alice Example", interruption.PlayerName, "parsed player");
	Equal("鼓舞激励の策", interruption.ActionName, "parsed action");
	Equal(true, CastInterruptionParser.TryParse("バトルログ：White Mageは「ケアルガ」の詠唱を中断した。", out CastInterruption replayInterruption), "prefixed replay interruption parsed");
	var party = new Dictionary<uint, string> { [1] = "Alice Example", [2] = "White Mage" };
	Equal(true, CastInterruptionParser.TryResolvePartyMember(replayInterruption.PlayerName, party, out uint entityId, out string playerName), "replay member resolved");
	Equal(2u, entityId, "replay entity");

	var t0 = DateTime.UtcNow;
	var aggregator = new CombatAggregator();
	aggregator.BeginPhase(t0, party, 900);
	aggregator.RecordInterruptedCast(entityId, playerName, 20, replayInterruption.ActionName, ActionKind.Magic, isHealingAction: true);
	aggregator.RecordInterruptedCast(entityId, playerName, 20, replayInterruption.ActionName, ActionKind.Magic, isHealingAction: true);
	ActionStatistics action = aggregator.CurrentPhase!.Players[2].Actions[20];
	Equal(0, action.UseCount, "interruption does not increment use count");
	Equal(2, action.InterruptedCastCount, "interruption count");
	Equal(true, action.IsHealingAction, "healing spell classification");
	Equal(false, CastInterruptionParser.TryResolvePartyMember("Exdeath", party, out _, out _), "enemy cast interruption excluded");
}

static void DeleteIndividualHistory()
{
	var t0 = DateTime.UtcNow;
	var party = new Dictionary<uint, string> { [1] = "Player" };
	var aggregator = new CombatAggregator();
	aggregator.BeginPhase(t0, party, 900);
	aggregator.ArchiveCurrent(t0.AddSeconds(5), CombatHistoryEndReason.Wipe);
	aggregator.BeginPhase(t0.AddSeconds(10), party, 901);
	aggregator.ArchiveCurrent(t0.AddSeconds(15), CombatHistoryEndReason.Wipe);
	Equal(true, aggregator.RemoveArchivedHistory(1), "selected history removed");
	Equal(1, aggregator.Histories.Count, "remaining history count");
	Equal(2, aggregator.Histories.Single().Number, "correct history remains");
	Equal(false, aggregator.RemoveArchivedHistory(999), "missing history not removed");
	aggregator.BeginPhase(t0.AddSeconds(20), party, 902);
	CombatHistoryRecord next = aggregator.ArchiveCurrent(t0.AddSeconds(25), CombatHistoryEndReason.Wipe)!;
	Equal(3, next.Number, "history number remains monotonic");
}

static void HistoryFileSizeThresholds()
{
	Equal(HistoryFileSizeLevel.Normal, HistoryFileSizeMonitor.GetLevel(HistoryFileSizeMonitor.WarningThresholdBytes), "500 MB is below warning");
	Equal(HistoryFileSizeLevel.Warning, HistoryFileSizeMonitor.GetLevel(HistoryFileSizeMonitor.WarningThresholdBytes + 1), "over 500 MB warning");
	Equal(HistoryFileSizeLevel.Warning, HistoryFileSizeMonitor.GetLevel(HistoryFileSizeMonitor.DangerThresholdBytes), "1 GB is still warning");
	Equal(HistoryFileSizeLevel.Danger, HistoryFileSizeMonitor.GetLevel(HistoryFileSizeMonitor.DangerThresholdBytes + 1), "over 1 GB danger");
}

static void LegacyHistoryMismatchMigration()
{
	string directory = Path.Combine(Path.GetTempPath(), $"PhaseDpsCheckerLegacyTests-{Guid.NewGuid():N}");
	try
	{
		DateTime t0 = DateTime.UtcNow;
		var party = new Dictionary<uint, string> { [1] = "Rowsai Elakha" };
		var partyIds = party.Keys.ToHashSet();
		var aggregator = new CombatAggregator();
		PhaseRecord phase = aggregator.BeginPhase(t0, party, 900);
		aggregator.RecordAction(new CombatActionEvent(t0, 1, "Rowsai Elakha", 10, "ファストブレード", ActionKind.WeaponSkill, true, true, 2.5,
			[new EffectSample(900, 14_093, 0, false, false)]), partyIds);
		aggregator.RecordAction(new CombatActionEvent(t0.AddSeconds(1), 1, "Rowsai Elakha", 11, "攻撃", ActionKind.Other, true, false, 0,
			[new EffectSample(900, 33_067, 0, false, false)]), partyIds);
		aggregator.RecordIncomingDamage(new IncomingDamageEvent(t0.AddSeconds(2), 1, "Rowsai Elakha", 900, "Enemy", 20, "攻撃", 1_234, []), partyIds);
		phase.Players[1].ApplyIinactTotals(7_903_291, 1_364_842, 200, 29, null, null);
		phase.SetIinactIncomingDamage(1, 1_361_355);
		aggregator.ArchiveCurrent(t0.AddSeconds(15.627), CombatHistoryEndReason.Manual);

		var store = new CombatHistoryStore(() => directory, directory, (_, _) => { });
		Equal(true, store.Save(aggregator.Histories), "legacy fixture saved");
		JObject root = JObject.Parse(File.ReadAllText(store.FilePath));
		root["SchemaVersion"] = 1;
		var actions = (Newtonsoft.Json.Linq.JArray)root["Histories"]![0]!["Phases"]![0]!["Players"]![0]!["Actions"]!;
		actions.First(action => action.Value<uint>("ActionId") == PlayerPhaseStatistics.IinactReconciliationActionId).Remove();
		File.WriteAllText(store.FilePath, root.ToString());

		PhaseRecord restoredPhase = store.Load().Single().Phases.Single();
		PlayerPhaseStatistics restored = restoredPhase.Players[1];
		Equal(47_160L, restored.TotalDamage, "legacy stale IINACT damage replaced by action total");
		Equal(0L, restored.TotalHealing, "legacy stale IINACT healing replaced by action total");
		Equal(1_234L, restoredPhase.IncomingDamageTotal(1), "legacy stale IINACT incoming damage replaced by event total");
		AssertActionTotalsMatch(restored, "legacy history migration exact totals");
	}
	finally
	{
		if (Directory.Exists(directory))
		{
			Directory.Delete(directory, true);
		}
	}
}

static void IinactPhaseDelta()
{
	var t0 = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
	var party = new Dictionary<uint, string> { [1] = "Alice Example", [2] = "Bob Example" };
	var aggregator = new CombatAggregator();
	PhaseRecord phase = aggregator.BeginPhase(t0, party, 900);
	var synchronizer = new IinactPhaseSynchronizer();
	var baseline = new IinactCombatSnapshot(1, t0, "enc-1", true, new Dictionary<string, IinactCombatantSnapshot>(StringComparer.OrdinalIgnoreCase)
	{
		["Alice Example"] = new("Alice Example", 100, 20, 10, 1, 0, 0, 0),
		["Carbuncle (Alice Example)"] = new("Carbuncle (Alice Example)", 50, 0, 0, 1, 0, null, null),
	});
	synchronizer.Begin(phase, baseline);
	var current = new IinactCombatSnapshot(2, t0.AddSeconds(10), "enc-1", true, new Dictionary<string, IinactCombatantSnapshot>(StringComparer.OrdinalIgnoreCase)
	{
		["Alice Example"] = new("Alice Example", 1100, 520, 410, 11, 4, 5, 2),
		["Carbuncle (Alice Example)"] = new("Carbuncle (Alice Example)", 150, 0, 0, 3, 1, null, null),
		["Bob Example"] = new("Bob Example", 500, 100, 200, 5, 2, 1, 1),
	});
	Equal(true, synchronizer.Apply(phase, current), "IINACT snapshot applied");
	Equal(1100L, phase.Players[1].TotalDamage, "owner and pet damage delta");
	Equal(500L, phase.Players[1].TotalHealing, "healing delta");
	Equal(12, phase.Players[1].DamageHitCount, "hit delta");
	Equal(5, phase.Players[1].CriticalDamageHits, "critical delta");
	Equal(5, phase.Players[1].DirectDamageHits, "direct hit delta");
	Equal(400L, phase.IinactIncomingDamageTotals[1], "incoming damage delta");
	Equal(500L, phase.Players[2].TotalDamage, "second player damage");
	Equal(true, phase.HasIinactData, "phase marked as IINACT data");
	AssertActionTotalsMatch(phase.Players[1], "phase delta exact totals");
	aggregator.RecordAction(Event(t0.AddSeconds(11), 1, 10, "Later local event", new EffectSample(900, 999, 0, false, false)), party.Keys.ToHashSet());
	Equal(1100L, phase.Players[1].TotalDamage, "IINACT total remains authoritative between snapshots");
	AssertActionTotalsMatch(phase.Players[1], "phase delta remains exact after local event");
}

static void IinactInactiveSnapshotIgnored()
{
	DateTime t0 = DateTime.UtcNow;
	var aggregator = new CombatAggregator();
	var party = new Dictionary<uint, string> { [1] = "Player One" };
	PhaseRecord phase = aggregator.BeginPhase(t0, party, 900);
	aggregator.RecordAction(Event(t0, 1, 10, "Fast Blade", new EffectSample(900, 14_093, 0, false, false)), party.Keys.ToHashSet());
	aggregator.RecordAction(Event(t0.AddSeconds(1), 1, 11, "Attack", new EffectSample(900, 33_067, 0, false, false)), party.Keys.ToHashSet());
	var synchronizer = new IinactPhaseSynchronizer();
	synchronizer.Begin(phase, IinactCombatSnapshot.Empty(t0));
	var stale = new IinactCombatSnapshot(10, t0, "old encounter", false, new Dictionary<string, IinactCombatantSnapshot>
	{
		["Player 1"] = new("Player 1", 7_903_291, 1_364_842, 1_361_355, 200, 29, null, null),
	});
	Equal(false, synchronizer.Apply(phase, stale), "inactive stale snapshot rejected");
	Equal(47_160L, phase.Players[1].TotalDamage, "screenshot action damage remains authoritative against stale snapshot");
	Equal(0L, phase.Players[1].TotalHealing, "stale healing not applied");
	AssertActionTotalsMatch(phase.Players[1], "inactive snapshot exact totals");
}

static void IinactTitleChangeKeepsBaseline()
{
	DateTime t0 = DateTime.UtcNow;
	var aggregator = new CombatAggregator();
	PhaseRecord phase = aggregator.BeginPhase(t0, new Dictionary<uint, string> { [1] = "Player One" }, 900);
	var synchronizer = new IinactPhaseSynchronizer();
	synchronizer.Begin(phase, new IinactCombatSnapshot(1, t0, "Training Dummy A", true, new Dictionary<string, IinactCombatantSnapshot>
	{
		["Player One"] = new("Player One", 10_000, 2_000, 1_000, 10, 2, null, null),
	}));
	var changedTitle = new IinactCombatSnapshot(2, t0.AddSeconds(10), "Training Dummy B", true, new Dictionary<string, IinactCombatantSnapshot>
	{
		["Player One"] = new("Player One", 13_500, 2_750, 1_600, 14, 3, null, null),
	});
	Equal(true, synchronizer.Apply(phase, changedTitle), "active snapshot applied after title change");
	Equal(3_500L, phase.Players[1].TotalDamage, "title change keeps damage baseline");
	Equal(750L, phase.Players[1].TotalHealing, "title change keeps healing baseline");
	Equal(600L, phase.IinactIncomingDamageTotals[1], "title change keeps incoming baseline");
	AssertActionTotalsMatch(phase.Players[1], "title change exact totals");
}

static void IinactActionReconciliation()
{
	DateTime t0 = DateTime.UtcNow;
	var party = new Dictionary<uint, string> { [1] = "Player One", [2] = "Player Two" };
	var partyIds = party.Keys.ToHashSet();
	var aggregator = new CombatAggregator();
	PhaseRecord phase = aggregator.BeginPhase(t0, party, 900);
	aggregator.RecordAction(new CombatActionEvent(t0, 1, "Player One", 10, "Local Action", ActionKind.WeaponSkill, true, true, 2.5,
		[new EffectSample(900, 900, 0, false, false), new EffectSample(2, 0, 300, false, false)]), partyIds);
	var synchronizer = new IinactPhaseSynchronizer();
	synchronizer.Begin(phase, IinactCombatSnapshot.Empty(t0));
	var snapshot = new IinactCombatSnapshot(1, t0.AddSeconds(1), "enc", true, new Dictionary<string, IinactCombatantSnapshot>
	{
		["Player One"] = new("Player One", 1_000, 500, 0, 2, 1, null, null),
	});
	Equal(true, synchronizer.Apply(phase, snapshot), "IINACT totals applied");
	PlayerPhaseStatistics player = phase.Players[1];
	Equal(1_000L, player.TotalDamage, "reconciled damage");
	Equal(500L, player.TotalHealing, "reconciled healing");
	Equal(100L, player.Actions[PlayerPhaseStatistics.IinactReconciliationActionId].TotalDamage, "unattributed damage row");
	Equal(200L, player.Actions[PlayerPhaseStatistics.IinactReconciliationActionId].TotalHealing, "unattributed healing row");
	AssertActionTotalsMatch(player, "IINACT reconciliation exact totals");

	aggregator.RecordAction(Event(t0.AddSeconds(2), 1, 11, "Later Local Action", new EffectSample(900, 200, 0, false, false)), partyIds);
	Equal(1_100L, player.TotalDamage, "newer local detail is not discarded while waiting for IINACT");
	AssertActionTotalsMatch(player, "post-local action exact totals");
}

static void IinactYouAlias()
{
	var t0 = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
	var party = new Dictionary<uint, string> { [1] = "Alice Example", [2] = "Bob Example" };
	var aggregator = new CombatAggregator();
	PhaseRecord phase = aggregator.BeginPhase(t0, party, 900);
	var synchronizer = new IinactPhaseSynchronizer();
	synchronizer.Begin(phase, IinactCombatSnapshot.Empty(t0));
	var current = new IinactCombatSnapshot(1, t0.AddSeconds(10), "enc-you", true, new Dictionary<string, IinactCombatantSnapshot>(StringComparer.OrdinalIgnoreCase)
	{
		["YOU"] = new("YOU", 1000, 300, 200, 10, 3, null, null),
		["Carbuncle (YOU)"] = new("Carbuncle (YOU)", 250, 0, 0, 2, 1, null, null),
		["Bob Example"] = new("Bob Example", 500, 100, 400, 5, 2, null, null),
	});
	Equal(true, synchronizer.Apply(phase, current, 1), "YOU snapshot applied");
	Equal(1250L, phase.Players[1].TotalDamage, "YOU and local pet mapped to local player");
	Equal(300L, phase.Players[1].TotalHealing, "YOU healing mapped to local player");
	Equal(200L, phase.IinactIncomingDamageTotals[1], "YOU incoming damage mapped to local player");
	Equal(500L, phase.Players[2].TotalDamage, "named party member remains mapped by name");
	AssertActionTotalsMatch(phase.Players[1], "YOU alias exact totals");
	AssertActionTotalsMatch(phase.Players[2], "named member exact totals");
}

static void IinactCombatDataParsing()
{
	JObject message = JObject.Parse("""
	{
	  "type": "CombatData",
	  "Encounter": { "title": "Encounter 1", "duration": "01:05", "DURATION": "65" },
	  "Combatant": {
	    "YOU": {
	      "name": "YOU",
	      "damage": "12,345",
	      "healed": "678",
	      "damagetaken": "90",
	      "hits": "12",
	      "crithits": "4",
	      "DirectHitCount": "3",
	      "CritDirectHitCount": "2"
	      ,"encdps": "1,234.5"
	      ,"enchps": "67.8"
	    }
	  },
	  "isActive": "true"
	}
	""");
	IinactCombatSnapshot snapshot = IinactCombatDataParser.Parse(message, DateTime.UtcNow, 7);
	Equal(7L, snapshot.Sequence, "parsed sequence");
	Equal("Encounter 1", snapshot.EncounterId, "parsed encounter title");
	Equal(true, snapshot.IsActive, "parsed active state");
	Near(65, snapshot.DurationSeconds, 0.001, "parsed encounter duration");
	IinactCombatantSnapshot combatant = snapshot.Combatants["YOU"];
	Equal(12345L, combatant.Damage, "parsed formatted damage");
	Equal(678L, combatant.Healing, "parsed healing");
	Equal(90L, combatant.DamageTaken, "parsed incoming damage");
	Equal(4, combatant.CriticalHits, "parsed critical hits");
	Equal(3, combatant.DirectHits, "parsed direct hits");
	Equal(2, combatant.CriticalDirectHits, "parsed critical direct hits");
	Near(1234.5, combatant.Dps, 0.001, "parsed encounter DPS");
	Near(67.8, combatant.Hps, 0.001, "parsed encounter HPS");
}

static void IinactLegacyCombatDataExtraction()
{
	JObject direct = JObject.Parse("""{ "type": "CombatData", "Combatant": {}, "isActive": "false" }""");
	JObject legacy = new()
	{
		["type"] = "broadcast",
		["msgtype"] = "CombatData",
		["msg"] = direct,
	};
	Equal(true, IinactCombatDataParser.TryExtract(legacy, out JObject extracted), "legacy CombatData extracted");
	Equal("CombatData", extracted.Value<string>("type"), "legacy payload type");
	Equal(true, IinactCombatDataParser.TryExtract(direct, out JObject extractedDirect), "direct CombatData accepted");
	Equal(direct, extractedDirect, "direct payload preserved");
}

static void IinactEncounterTransitions()
{
	var lifecycle = new IinactEncounterLifecycle();
	DateTime now = DateTime.UtcNow;
	IReadOnlyDictionary<string, IinactCombatantSnapshot> empty = new Dictionary<string, IinactCombatantSnapshot>();
	Equal(IinactEncounterTransition.None, lifecycle.Observe(new IinactCombatSnapshot(1, now, "enc", false, empty)), "idle snapshot is not END");
	Equal(IinactEncounterTransition.Started, lifecycle.Observe(new IinactCombatSnapshot(2, now, "enc", true, empty)), "inactive to active starts");
	Equal(IinactEncounterTransition.None, lifecycle.Observe(new IinactCombatSnapshot(3, now, "enc", true, empty)), "active update does not restart");
	Equal(IinactEncounterTransition.Ended, lifecycle.Observe(new IinactCombatSnapshot(4, now, "enc", false, empty)), "active to inactive is END");
	Equal(IinactEncounterTransition.None, lifecycle.Observe(new IinactCombatSnapshot(5, now, "enc", false, empty)), "repeated inactive snapshot is ignored");
}

static void IinactMultiSourceEndTransition()
{
	var lifecycle = new IinactEncounterLifecycle();
	DateTime now = DateTime.UtcNow;
	IReadOnlyDictionary<string, IinactCombatantSnapshot> empty = new Dictionary<string, IinactCombatantSnapshot>();
	Equal(IinactEncounterTransition.Started, lifecycle.Observe("IPC", new IinactCombatSnapshot(1, now, "enc", true, empty)), "IPC starts encounter");
	Equal(IinactEncounterTransition.None, lifecycle.Observe("WebSocket", new IinactCombatSnapshot(2, now, "enc", true, empty)), "duplicate source start ignored");
	Equal(IinactEncounterTransition.Ended, lifecycle.Observe("WebSocket", new IinactCombatSnapshot(3, now, "enc", false, empty)), "secondary source END accepted immediately");
	Equal(IinactEncounterTransition.None, lifecycle.Observe("IPC", new IinactCombatSnapshot(4, now, "enc", false, empty)), "duplicate IPC END ignored");
	Equal(IinactEncounterTransition.Started, lifecycle.Observe("IPC", new IinactCombatSnapshot(5, now, "next", true, empty)), "next IPC encounter starts");
}

static void IinactWebSocketEndpointResolution()
{
	const string overlayUrl = "http://proxy.iinact.com/overlay/mopimopi/?HOST_PORT=ws://127.0.0.1:10500";
	Equal(true, IinactWebSocketEndpoint.TryResolve(overlayUrl, null, out Uri? fromOverlay, out string overlayError), $"mopimopi URL resolved: {overlayError}");
	Equal("ws://127.0.0.1:10500/ws", fromOverlay!.ToString().TrimEnd('/'), "mopimopi HOST_PORT endpoint");
	Equal(true, IinactWebSocketEndpoint.TryResolve(string.Empty, new Uri("ws://0.0.0.0:10501"), out Uri? discovered, out string discoveredError), $"discovered URL resolved: {discoveredError}");
	Equal("ws://127.0.0.1:10501/ws", discovered!.ToString().TrimEnd('/'), "wildcard discovery uses loopback");
	Equal(false, IinactWebSocketEndpoint.TryResolve("https://example.com/overlay", null, out _, out _), "overlay URL without endpoint rejected");
}

static void PhaseNumberAfterTrim()
{
    var t0 = DateTime.UtcNow;
    var party = new Dictionary<uint, string> { [1] = "Player" };
    var aggregator = new CombatAggregator();
    for (var index = 0; index < 3; index++)
    {
        aggregator.BeginPhase(t0.AddSeconds(index * 10), party, 900);
        aggregator.EndCurrentPhase(t0.AddSeconds(index * 10 + 5));
    }
    aggregator.TrimCurrentPhases(2);
    var fourth = aggregator.BeginPhase(t0.AddSeconds(30), party, 900);
    Equal(4, fourth.Number, "phase number");
}

static void PeriodicTickDoesNotCountAsUse()
{
    var t0 = DateTime.UtcNow;
    var party = new Dictionary<uint, string> { [1] = "Player" };
    var partyIds = party.Keys.ToHashSet();
    var aggregator = new CombatAggregator();
    aggregator.BeginPhase(t0, party, 900);
    aggregator.RecordAction(Event(t0, 1, 30, "Damage over time", new EffectSample(900, 100, 0, false, false)), partyIds);
    aggregator.RecordAction(new CombatActionEvent(t0.AddSeconds(3), 1, "Player", 30, "Damage over time", ActionKind.Magic, false, false, 0, [new EffectSample(900, 50, 0, false, false)]), partyIds);
    var action = aggregator.CurrentPhase!.Players[1].Actions[30];
    Equal(1, action.UseCount, "use count after tick");
    Equal(150L, action.TotalDamage, "damage including tick");
}

static void ArchiveCombatHistory()
{
    var t0 = new DateTime(2026, 7, 16, 12, 0, 0, DateTimeKind.Utc);
    var party = new Dictionary<uint, string> { [1] = "Player" };
    var partyIds = party.Keys.ToHashSet();
    var aggregator = new CombatAggregator();
    aggregator.BeginPhase(t0, party, 900);
    aggregator.RecordAction(Event(t0.AddSeconds(1), 1, 10, "Strike", new EffectSample(900, 1200, 0, false, false)), partyIds);
    var history = aggregator.ArchiveCurrent(t0.AddSeconds(10), CombatHistoryEndReason.Wipe);
    Equal(0, aggregator.Phases.Count, "current phase count after archive");
    Equal(1, aggregator.Histories.Count, "history count");
    Equal(CombatHistoryEndReason.Wipe, history!.EndReason, "history reason");
    Equal(t0, history.StartedAt, "history start");
    Equal(t0.AddSeconds(10), history.EndedAt, "history end");
    Equal(1200L, history.Phases.Single().Players[1].TotalDamage, "archived total damage");
    var next = aggregator.BeginPhase(t0.AddSeconds(20), party, 901);
    Equal(1, next.Number, "phase number reset for next combat");
}

static void ArchiveIncomingDamage()
{
	var t0 = new DateTime(2026, 7, 16, 12, 0, 0, DateTimeKind.Utc);
	var party = new Dictionary<uint, string> { [1] = "Tank", [2] = "Healer" };
	var partyIds = party.Keys.ToHashSet();
	var aggregator = new CombatAggregator();
	aggregator.BeginPhase(t0, party, 900);
	aggregator.RecordIncomingDamage(new IncomingDamageEvent(
		t0.AddSeconds(2),
		1,
		"Tank",
		900,
		"Enemy",
		7,
		"Auto Attack",
		4321,
		[new CombatStatusSnapshot(100, "Mitigation", 1, 8.5f)]), partyIds);
	aggregator.RecordIncomingDamage(new IncomingDamageEvent(
		t0.AddSeconds(3),
		999,
		"Not Party",
		900,
		"Enemy",
		8,
		"Ignored",
		9999,
		[]), partyIds);
	var history = aggregator.ArchiveCurrent(t0.AddSeconds(10), CombatHistoryEndReason.Wipe)!;
	var incoming = history.Phases.Single().IncomingDamageEvents.Single();
	Equal(4321u, incoming.Amount, "incoming amount");
	Equal("Auto Attack", incoming.ActionName, "incoming action");
	Equal("Mitigation", incoming.Statuses.Single().Name, "status snapshot");
}

static void DefeatingAnchorHit()
{
	var t0 = new DateTime(2026, 7, 16, 12, 0, 0, DateTimeKind.Utc);
	var finalHitAt = t0.AddSeconds(8.25);
	EffectSample[] effects =
	[
		new EffectSample(900, 1234, 0, false, false),
		new EffectSample(901, 567, 0, false, false),
	];
	Equal(true, PhaseEndDetection.IsDefeatingHit(900, effects, anchorIsDefeated: true), "defeated anchor hit");
	Equal(false, PhaseEndDetection.IsDefeatingHit(900, effects, anchorIsDefeated: false), "living anchor");
	Equal(false, PhaseEndDetection.IsDefeatingHit(999, effects, anchorIsDefeated: true), "different target");
	Equal(false, PhaseEndDetection.IsDefeatingHit(0, effects, anchorIsDefeated: true), "missing anchor");

	var party = new Dictionary<uint, string> { [1] = "Player" };
	var partyIds = party.Keys.ToHashSet();
	var aggregator = new CombatAggregator();
	aggregator.BeginPhase(t0, party, 900);
	aggregator.RecordAction(new CombatActionEvent(finalHitAt, 1, "Player", 10, "Final Strike", ActionKind.WeaponSkill, true, true, 2.5, effects), partyIds);
	if (PhaseEndDetection.IsDefeatingHit(900, effects, anchorIsDefeated: true))
	{
		aggregator.EndCurrentPhase(finalHitAt);
	}
	Equal(1801L, aggregator.Phases.Single().Players[1].TotalDamage, "final hit damage included");
	Equal(finalHitAt, aggregator.Phases.Single().EndedAt, "phase ends at final hit timestamp");
}

static void FuturesRewrittenPhaseTransitions()
{
	var controller = new FuturesRewrittenPhaseController();
	Equal(new DedicatedPhaseTransition(DedicatedPhaseCommand.Start, 1), controller.OnCombatStarted(), "phase 1 start");
	Equal(new DedicatedPhaseTransition(DedicatedPhaseCommand.End, 1), controller.OnDialogue("ケフカ：お前たち、「初めて」じゃないな？ ナルホド……さては……」"), "phase 1 end");
	Equal(new DedicatedPhaseTransition(DedicatedPhaseCommand.Start, 2), controller.OnDialogue("絶ッ！！再現したな、この私を……！"), "phase 2 start");
	controller.OnEnemyListState(isEmpty: false);
	Equal(new DedicatedPhaseTransition(DedicatedPhaseCommand.End, 2), controller.OnEnemyListState(isEmpty: true), "phase 2 end");
	Equal(new DedicatedPhaseTransition(DedicatedPhaseCommand.Start, 3), controller.OnDialogue("ボクチンに不可能はなーい！"), "phase 3 start");
	Equal(new DedicatedPhaseTransition(DedicatedPhaseCommand.End, 3), controller.OnDialogue("エクスデスは「メテオ」を中断した。"), "phase 3 end");
	Equal(new DedicatedPhaseTransition(DedicatedPhaseCommand.Start, 4), controller.OnFirstPartyAttack(), "phase 4 first attack start");
	Equal(DedicatedPhaseTransition.None, controller.OnDokiDokiUltimaCompleted(), "keep phase 4 after first Doki Doki Ultima");
	Equal(FuturesRewrittenStage.Phase4, controller.Stage, "phase 4 remains active after first Doki Doki Ultima");
	Equal(new DedicatedPhaseTransition(DedicatedPhaseCommand.End, 4), controller.OnDokiDokiUltimaCompleted(), "phase 4 end after second Doki Doki Ultima");
	Equal(DedicatedPhaseTransition.None, controller.OnFirstPartyAttack(), "ignore party attack before phase 5 dialogue");
	Equal(DedicatedPhaseTransition.None, controller.OnKefkaTargetable(), "ignore targetable Kefka before phase 5 dialogue");
	Equal(DedicatedPhaseTransition.None, controller.OnDialogue("ケフカ：私は破壊し続けよう！"), "phase 5 dialogue arms targetability detection");
	Equal(DedicatedPhaseTransition.None, controller.OnFirstPartyAttack(), "ignore party attack before Kefka becomes targetable");
	Equal(new DedicatedPhaseTransition(DedicatedPhaseCommand.Start, 5), controller.OnKefkaTargetable(), "phase 5 Kefka targetable start");
	Equal(new DedicatedPhaseTransition(DedicatedPhaseCommand.End, 5), controller.OnDutyCompleted(), "phase 5 end");
}

static void FuturesRewrittenEnemyListTransition()
{
	var controller = new FuturesRewrittenPhaseController();
	controller.OnCombatStarted();
	controller.OnDialogue("お前たち、「初めて」じゃないな？ナルホド……さては……");
	controller.OnDialogue("絶ッ！！再現したな、この私を……！");
	Equal(DedicatedPhaseTransition.None, controller.OnEnemyListState(isEmpty: true), "ignore an initially empty list");
	controller.OnEnemyListState(isEmpty: false);
	Equal(new DedicatedPhaseTransition(DedicatedPhaseCommand.End, 2), controller.OnEnemyListState(isEmpty: true), "end after list becomes empty");
}

static void FuturesRewrittenPhase3BattleLogTransition()
{
	var controller = new FuturesRewrittenPhaseController();
	controller.OnCombatStarted();
	controller.OnDialogue("お前たち、「初めて」じゃないな？ナルホド……さては……");
	controller.OnDialogue("絶ッ！！再現したな、この私を……！");
	controller.OnEnemyListState(isEmpty: false);
	controller.OnEnemyListState(isEmpty: true);
	controller.OnDialogue("ボクチンに不可能はなーい！");
	Equal(DedicatedPhaseTransition.None, controller.OnDialogue("エクスデスは「メテオ」を実行した。"), "ignore a different battle log");
	Equal(DedicatedPhaseTransition.None, controller.OnFirstPartyAttack(), "ignore party attack during phase 3");
	Equal(new DedicatedPhaseTransition(DedicatedPhaseCommand.End, 3), controller.OnDialogue("バトルログ：エクスデスは「メテオ」を中断した。"), "end on the meteor interruption log");
}

static void ReplayPartyMemberResolution()
{
	Equal(true, ReplayPartyMemberNames.TryResolve("Dark Knight", 32, out uint darkKnightJobId), "resolve Dark Knight");
	Equal(32u, darkKnightJobId, "Dark Knight job id");
	Equal(true, ReplayPartyMemberNames.TryResolve(" white mage ", 0, out uint whiteMageJobId), "resolve trimmed White Mage");
	Equal(24u, whiteMageJobId, "White Mage fallback job id");
	Equal(false, ReplayPartyMemberNames.TryResolve("Dark Knight", 24, out _), "reject mismatched job id");
	Equal(false, ReplayPartyMemberNames.TryResolve("Player Name", 32, out _), "reject normal player name");
}

static CombatActionEvent Event(DateTime timestamp, uint source, uint actionId, string actionName, EffectSample effect, bool gcd = false, double gcdSeconds = 2.5) =>
    new(timestamp, source, $"Player {source}", actionId, actionName, ActionKind.WeaponSkill, true, gcd, gcdSeconds, [effect]);

static void AssertActionTotalsMatch(PlayerPhaseStatistics player, string label)
{
	Equal(player.TotalDamage, player.ActionDamageTotal, $"{label} damage");
	Equal(player.TotalHealing, player.ActionHealingTotal, $"{label} healing");
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}");
}

static void Near(double expected, double actual, double epsilon, string label)
{
    if (Math.Abs(expected - actual) > epsilon)
        throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}");
}
