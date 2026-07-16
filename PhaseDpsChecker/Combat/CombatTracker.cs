using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.DutyState;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace PhaseDpsChecker.Combat;

public sealed class CombatTracker : IDisposable
{
	private readonly record struct PeriodicKey(uint OwnerEntityId, uint TargetEntityId, uint StatusId);

	private sealed record PeriodicAttribution(uint OwnerEntityId, uint ActionId, string ActionName, ActionKind Kind, DateTime AppliedAt);

	private readonly Configuration configuration;

	private readonly IFramework framework;

	private readonly IDataManager dataManager;

	private readonly IObjectTable objectTable;

	private readonly ICondition condition;

	private readonly IClientState clientState;

	private readonly IChatGui chatGui;

	private readonly IDutyState dutyState;

	private readonly IPluginLog log;

	private readonly ActionEffectCapture? capture;

	private readonly FuturesRewrittenPhaseController futuresRewrittenController = new();

	private readonly ConcurrentQueue<(DateTime Timestamp, string Message)> dialogueEvents = new();

	private readonly Dictionary<PeriodicKey, PeriodicAttribution> periodicAttributions = new Dictionary<PeriodicKey, PeriodicAttribution>();

	private uint anchorTargetEntityId;

	private bool anchorWasTargetable;

	private DateTime? combatLostAt;

	private DateTime? lastPartyDamageAt;

	private DateTime? nextDedicatedPhaseAttackAfter;

	private bool dutyCompletionPending;

	public CombatAggregator Aggregator { get; }

	public PartyRoster Roster { get; }

	public bool CaptureAvailable { get; }

	public string? CaptureError { get; }

	public bool IsDisabledForPvP => clientState.IsPvP;

	public PhaseDetectionPreset ActivePreset => configuration.PhaseDetectionPreset;

	public string PhaseDetectionStatus => ActivePreset == PhaseDetectionPreset.FuturesRewrittenUltimate
		? futuresRewrittenController.StatusLabel
		: Aggregator.CurrentPhase == null ? "通常計測：開始待ち" : "通常計測：計測中";

	public CombatTracker(Configuration configuration, IFramework framework, IDataManager dataManager, IObjectTable objectTable, IPartyList partyList, IDutyState dutyState, ICondition condition, IClientState clientState, IChatGui chatGui, IGameInteropProvider interopProvider, IPluginLog log)
	{
		this.configuration = configuration;
		this.framework = framework;
		this.dataManager = dataManager;
		this.objectTable = objectTable;
		this.condition = condition;
		this.clientState = clientState;
		this.chatGui = chatGui;
		this.dutyState = dutyState;
		this.log = log;
		Aggregator = new CombatAggregator();
		Roster = new PartyRoster(configuration, partyList, objectTable);
		try
		{
			capture = new ActionEffectCapture(interopProvider, log);
			CaptureAvailable = true;
		}
		catch (Exception ex)
		{
			CaptureError = ex.Message;
			log.Error(ex, "ActionEffect フックを初期化できませんでした。");
		}
		framework.Update += OnFrameworkUpdate;
		chatGui.ChatMessage += OnChatMessage;
		clientState.TerritoryChanged += OnTerritoryChanged;
		dutyState.DutyWiped += OnDutyWiped;
		dutyState.DutyCompleted += OnDutyCompleted;
	}

	public void Dispose()
	{
		framework.Update -= OnFrameworkUpdate;
		chatGui.ChatMessage -= OnChatMessage;
		clientState.TerritoryChanged -= OnTerritoryChanged;
		dutyState.DutyWiped -= OnDutyWiped;
		dutyState.DutyCompleted -= OnDutyCompleted;
		capture?.Dispose();
	}

	public void ForceEndCurrentPhase()
	{
		if (Aggregator.EndCurrentPhase(DateTime.UtcNow))
		{
			ResetPhaseDetection();
			if (ActivePreset == PhaseDetectionPreset.FuturesRewrittenUltimate)
			{
				futuresRewrittenController.Reset();
			}
		}
	}

	public void SetPhaseDetectionPreset(PhaseDetectionPreset preset)
	{
		if (configuration.PhaseDetectionPreset == preset)
		{
			return;
		}

		if (Aggregator.Phases.Count > 0)
		{
			ArchiveCurrentCombat(CombatHistoryEndReason.Manual);
		}
		configuration.PhaseDetectionPreset = preset;
		configuration.Save();
		ResetEncounterDetection();
	}

	public void SetReplayMode(bool enabled)
	{
		if (configuration.ReplayMode == enabled)
		{
			return;
		}

		if (Aggregator.Phases.Count > 0)
		{
			ArchiveCurrentCombat(CombatHistoryEndReason.Manual);
		}
		configuration.ReplayMode = enabled;
		configuration.SelectedEntityId = 0;
		configuration.Save();
		ResetEncounterDetection();
	}

	public void ArchiveCurrentForHistory()
	{
		ArchiveCurrentCombat(CombatHistoryEndReason.Manual);
	}

	public void ClearArchivedHistory()
	{
		Aggregator.ClearArchivedHistory();
	}

	private void OnFrameworkUpdate(IFramework frameworkContext)
	{
		if (clientState.IsPvP)
		{
			if (Aggregator.CurrentPhase != null)
			{
				EndPhase(DateTime.UtcNow);
			}
			if (capture != null)
			{
				RawActionEvent actionEvent;
				while (capture.TryDequeue(out actionEvent))
				{
				}
				RawPeriodicEvent periodicEvent;
				while (capture.TryDequeuePeriodic(out periodicEvent))
				{
				}
			}
			return;
		}
		Dictionary<uint, string> currentMembers = Roster.GetCurrentMembers();
		HashSet<uint> memberIds = currentMembers.Keys.ToHashSet();
		DateTime now = DateTime.UtcNow;
		if (ActivePreset == PhaseDetectionPreset.FuturesRewrittenUltimate && condition[ConditionFlag.InCombat])
		{
			ApplyDedicatedTransition(futuresRewrittenController.OnCombatStarted(), now, currentMembers);
		}
		ProcessDialogueEvents(currentMembers);
		if (capture != null)
		{
			RawActionEvent actionEvent2;
			while (capture.TryDequeue(out actionEvent2) && (object)actionEvent2 != null)
			{
				ProcessRawAction(actionEvent2, currentMembers, memberIds);
			}
			RawPeriodicEvent periodicEvent2;
			while (capture.TryDequeuePeriodic(out periodicEvent2) && (object)periodicEvent2 != null)
			{
				ProcessPeriodicEvent(periodicEvent2, currentMembers, memberIds);
			}
		}
		now = DateTime.UtcNow;
		if (ActivePreset == PhaseDetectionPreset.FuturesRewrittenUltimate)
		{
			CheckDedicatedPhaseTriggers(now, currentMembers);
		}
		else
		{
			CheckForPhaseEnd(now);
		}
		if (dutyCompletionPending)
		{
			CompleteDuty(currentMembers, now);
		}
		Aggregator.TrimCurrentPhases(configuration.MaxPhaseHistory);
		Aggregator.TrimArchivedHistory(configuration.MaxEncounterHistory);
	}

	private void ProcessRawAction(RawActionEvent rawAction, IReadOnlyDictionary<uint, string> members, IReadOnlySet<uint> memberIds)
	{
		if (rawAction.ActionType != 1)
		{
			return;
		}
		uint partyOwnerId = Roster.ResolvePartyOwner(rawAction.SourceEntityId, members);
		string playerName = string.Empty;
		bool isPartySource = partyOwnerId != 0 && members.TryGetValue(partyOwnerId, out playerName);
		bool hasOutgoingDamage = isPartySource && rawAction.Effects.Any((EffectSample effect) => effect.Damage != 0 && !memberIds.Contains(effect.TargetEntityId));
		bool hasDirectPartyMemberOutgoingDamage = memberIds.Contains(rawAction.SourceEntityId) && hasOutgoingDamage;
		List<IGrouping<uint, EffectSample>> incomingGroups = (from effect in rawAction.Effects
			where effect.Damage != 0 && memberIds.Contains(effect.TargetEntityId) && !isPartySource
			group effect by effect.TargetEntityId).ToList();
		(string actionName, ActionKind kind, bool isGcd, double gcdDurationSeconds) = ResolveAction(rawAction.ActionId);
		if (isPartySource)
		{
			foreach (StatusApplication statusApplication in rawAction.StatusApplications)
			{
				periodicAttributions[new PeriodicKey(partyOwnerId, statusApplication.TargetEntityId, statusApplication.StatusId)] = new PeriodicAttribution(partyOwnerId, rawAction.ActionId, actionName, kind, rawAction.Timestamp);
			}
		}
		if (Aggregator.CurrentPhase == null)
		{
			if (ActivePreset == PhaseDetectionPreset.FuturesRewrittenUltimate && hasDirectPartyMemberOutgoingDamage)
			{
				uint firstTarget = ChooseTargetableAnchor(rawAction.Effects, memberIds);
				if (firstTarget != 0 && (!nextDedicatedPhaseAttackAfter.HasValue || rawAction.Timestamp >= nextDedicatedPhaseAttackAfter.Value))
				{
					ApplyDedicatedTransition(futuresRewrittenController.OnFirstPartyAttack(), rawAction.Timestamp, members, firstTarget);
				}
			}
			else if (ActivePreset == PhaseDetectionPreset.Normal && hasOutgoingDamage)
			{
				uint firstTarget = ChooseTargetableAnchor(rawAction.Effects, memberIds);
				if (firstTarget != 0)
				{
					BeginPhase(rawAction.Timestamp, members, firstTarget);
				}
			}

			if (Aggregator.CurrentPhase == null)
			{
				return;
			}
		}
		if (isPartySource)
		{
			Aggregator.RecordAction(new CombatActionEvent(rawAction.Timestamp, partyOwnerId, playerName, rawAction.ActionId, actionName, kind, CountsAsUse: true, isGcd, gcdDurationSeconds, rawAction.Effects), memberIds);
			if (hasOutgoingDamage && ActivePreset == PhaseDetectionPreset.FuturesRewrittenUltimate && futuresRewrittenController.Stage == FuturesRewrittenStage.Phase5)
			{
				lastPartyDamageAt = rawAction.Timestamp;
			}
		}
		if (incomingGroups.Count != 0)
		{
			string enemyName = ResolveGameObjectName(rawAction.SourceEntityId, "不明なエネミー");
			string incomingActionName = NormalizeIncomingActionName(rawAction.ActionId, actionName);
			foreach (IGrouping<uint, EffectSample> group in incomingGroups)
			{
				if (!members.TryGetValue(group.Key, out string targetPlayerName))
				{
					continue;
				}
				ulong total = group.Aggregate<EffectSample, ulong>(0, (current, effect) => current + effect.Damage);
				Aggregator.RecordIncomingDamage(new IncomingDamageEvent(
					rawAction.Timestamp,
					group.Key,
					targetPlayerName,
					rawAction.SourceEntityId,
					enemyName,
					rawAction.ActionId,
					incomingActionName,
					(uint)Math.Min(total, uint.MaxValue),
					SnapshotStatuses(group.Key)), memberIds);
			}
		}
		if (ActivePreset == PhaseDetectionPreset.FuturesRewrittenUltimate)
		{
			if (futuresRewrittenController.Stage == FuturesRewrittenStage.Phase4 &&
				IsNamedEnemy(rawAction.SourceEntityId, "ケフカ") &&
				string.Equals(actionName, "どきどきアルテマ", StringComparison.Ordinal))
			{
				ApplyDedicatedTransition(futuresRewrittenController.OnDokiDokiUltimaCompleted(), rawAction.Timestamp, members);
			}
		}
		else
		{
			TryEndPhaseForDefeatedAnchor(rawAction.Timestamp, rawAction.Effects);
		}
	}

	private void ProcessPeriodicEvent(RawPeriodicEvent periodicEvent, IReadOnlyDictionary<uint, string> members, IReadOnlySet<uint> memberIds)
	{
		uint num = Roster.ResolvePartyOwner(periodicEvent.SourceEntityId, members);
		bool targetsParty = memberIds.Contains(periodicEvent.TargetEntityId);
		if (!periodicEvent.IsHealing && targetsParty && num == 0 && members.TryGetValue(periodicEvent.TargetEntityId, out string targetPlayerName))
		{
			if (Aggregator.CurrentPhase == null)
			{
				return;
			}
			string statusName = ResolveStatusName(periodicEvent.StatusId);
			Aggregator.RecordIncomingDamage(new IncomingDamageEvent(
				periodicEvent.Timestamp,
				periodicEvent.TargetEntityId,
				targetPlayerName,
				periodicEvent.SourceEntityId,
				ResolveGameObjectName(periodicEvent.SourceEntityId, "継続ダメージ"),
				0x80000000u | periodicEvent.StatusId,
				$"{statusName} (DoT)",
				periodicEvent.Amount,
				SnapshotStatuses(periodicEvent.TargetEntityId)), memberIds);
			return;
		}
		PeriodicAttribution value = null;
		if (num != 0)
		{
			periodicAttributions.TryGetValue(new PeriodicKey(num, periodicEvent.TargetEntityId, periodicEvent.StatusId), out value);
		}
		if ((object)value == null)
		{
			value = (from entry in periodicAttributions
				where entry.Key.TargetEntityId == periodicEvent.TargetEntityId && entry.Key.StatusId == periodicEvent.StatusId && (periodicEvent.Timestamp - entry.Value.AppliedAt).TotalMinutes <= 5.0
				orderby entry.Value.AppliedAt descending
				select entry.Value).FirstOrDefault();
			num = value?.OwnerEntityId ?? num;
		}
		if (num == 0 || !members.TryGetValue(num, out string value2))
		{
			return;
		}
		bool flag = targetsParty;
		bool flag2 = !periodicEvent.IsHealing && !flag;
		if (Aggregator.CurrentPhase == null)
		{
			if (!flag2)
			{
				return;
			}
			if (ActivePreset == PhaseDetectionPreset.Normal && IsTargetableEnemy(periodicEvent.TargetEntityId))
			{
				BeginPhase(periodicEvent.Timestamp, members, periodicEvent.TargetEntityId);
			}
			else
			{
				return;
			}
		}
		string text = ResolveStatusName(periodicEvent.StatusId) + (periodicEvent.IsHealing ? " (HoT)" : " (DoT)");
		uint actionId = value?.ActionId ?? (0x80000000u | periodicEvent.StatusId);
		string actionName = value?.ActionName ?? text;
		ActionKind kind = value?.Kind ?? ActionKind.Other;
		EffectSample item = new EffectSample(periodicEvent.TargetEntityId, (!periodicEvent.IsHealing) ? periodicEvent.Amount : 0u, periodicEvent.IsHealing ? periodicEvent.Amount : 0u, Critical: false, DirectHit: false);
		Aggregator.RecordAction(new CombatActionEvent(periodicEvent.Timestamp, num, value2, actionId, actionName, kind, CountsAsUse: false, IsGcd: false, 0.0, [item]), memberIds);
		if (ActivePreset == PhaseDetectionPreset.FuturesRewrittenUltimate)
		{
			if (flag2 && futuresRewrittenController.Stage == FuturesRewrittenStage.Phase5)
			{
				lastPartyDamageAt = periodicEvent.Timestamp;
			}
		}
		else
		{
			TryEndPhaseForDefeatedAnchor(periodicEvent.Timestamp, [item]);
		}
	}

	private void TryEndPhaseForDefeatedAnchor(DateTime timestamp, IReadOnlyList<EffectSample> effects)
	{
		if (Aggregator.CurrentPhase == null || anchorTargetEntityId == 0)
		{
			return;
		}
		IGameObject? anchor = objectTable.SearchByEntityId(anchorTargetEntityId);
		bool anchorIsDefeated = anchor is ICharacter character && (character.IsDead || character.CurrentHp == 0);
		if (PhaseEndDetection.IsDefeatingHit(anchorTargetEntityId, effects, anchorIsDefeated))
		{
			EndPhase(timestamp);
		}
	}

	private IReadOnlyList<CombatStatusSnapshot> SnapshotStatuses(uint targetEntityId)
	{
		if (objectTable.SearchByEntityId(targetEntityId) is not IBattleChara battleChara)
		{
			return Array.Empty<CombatStatusSnapshot>();
		}
		List<CombatStatusSnapshot> statuses = new List<CombatStatusSnapshot>();
		foreach (var status in battleChara.StatusList)
		{
			if (status.StatusId == 0)
			{
				continue;
			}
			statuses.Add(new CombatStatusSnapshot(
				status.StatusId,
				ResolveStatusName(status.StatusId),
				(ushort)status.Param,
				Math.Max(0f, status.RemainingTime)));
		}
		return statuses.OrderBy((CombatStatusSnapshot status) => status.Name, StringComparer.CurrentCulture).ToArray();
	}

	private string ResolveGameObjectName(uint entityId, string fallback)
	{
		IGameObject? gameObject = objectTable.SearchByEntityId(entityId);
		if (gameObject == null)
		{
			return fallback;
		}
		string name = gameObject.Name.TextValue;
		return string.IsNullOrWhiteSpace(name) ? fallback : name;
	}

	private static string NormalizeIncomingActionName(uint actionId, string actionName)
	{
		if (actionId == 0 || string.IsNullOrWhiteSpace(actionName) || actionName == $"Action #{actionId}")
		{
			return "オートアタック";
		}
		return actionName;
	}

	private (string Name, ActionKind Kind, bool IsGcd, double GcdDurationSeconds) ResolveAction(uint actionId)
	{
		if (!dataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>().TryGetRow(actionId, out var row))
		{
			return (Name: $"Action #{actionId}", Kind: ActionKind.Other, IsGcd: false, GcdDurationSeconds: 0.0);
		}
		uint rowId = row.ActionCategory.RowId;
		ActionKind item = rowId switch
		{
			2u => ActionKind.Magic, 
			3u => ActionKind.WeaponSkill, 
			4u => ActionKind.Ability, 
			_ => ActionKind.Other, 
		};
		bool flag = rowId - 2 <= 1;
		bool item2 = flag && row.CooldownGroup == 58;
		double item3 = ((row.Recast100ms > 0) ? ((double)(int)row.Recast100ms / 10.0) : 2.5);
		string text = row.Name.ToString();
		return (Name: string.IsNullOrWhiteSpace(text) ? $"Action #{actionId}" : text, Kind: item, IsGcd: item2, GcdDurationSeconds: item3);
	}

	private string ResolveStatusName(uint statusId)
	{
		if (!dataManager.GetExcelSheet<Status>().TryGetRow(statusId, out var row))
		{
			return $"Status #{statusId}";
		}
		string text = row.Name.ToString();
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		return $"Status #{statusId}";
	}

	private uint ChooseTargetableAnchor(IReadOnlyList<EffectSample> effects, IReadOnlySet<uint> memberIds)
	{
		uint selectedEntityId = 0;
		uint selectedMaxHp = 0;
		foreach (uint item in (from effect in effects
			where effect.Damage != 0 && !memberIds.Contains(effect.TargetEntityId)
			select effect.TargetEntityId).Distinct())
		{
			IGameObject? gameObject = objectTable.SearchByEntityId(item);
			if (gameObject is not ICharacter character || !gameObject.IsTargetable)
			{
				continue;
			}

			if (selectedEntityId == 0 || character.MaxHp > selectedMaxHp)
			{
				selectedEntityId = item;
				selectedMaxHp = character.MaxHp;
			}
		}
		return selectedEntityId;
	}

	private void BeginPhase(DateTime timestamp, IReadOnlyDictionary<uint, string> members, uint anchorEntityId)
	{
		anchorTargetEntityId = anchorEntityId;
		Aggregator.BeginPhase(timestamp, members, anchorEntityId);
		anchorWasTargetable = anchorEntityId != 0 && objectTable.SearchByEntityId(anchorEntityId)?.IsTargetable == true;
		combatLostAt = null;
	}

	private bool IsTargetableEnemy(uint entityId) =>
		objectTable.SearchByEntityId(entityId) is ICharacter character && character.IsTargetable && !character.IsDead;

	private void OnChatMessage(IHandleableChatMessage message)
	{
		string text = message.Message.TextValue;
		if (!string.IsNullOrWhiteSpace(text))
		{
			dialogueEvents.Enqueue((DateTime.UtcNow, text));
		}
	}

	private void ProcessDialogueEvents(IReadOnlyDictionary<uint, string> members)
	{
		while (dialogueEvents.TryDequeue(out (DateTime Timestamp, string Message) dialogue))
		{
			if (ActivePreset != PhaseDetectionPreset.FuturesRewrittenUltimate)
			{
				continue;
			}

			ApplyDedicatedTransition(futuresRewrittenController.OnDialogue(dialogue.Message), dialogue.Timestamp, members);
		}
	}

	private void CheckDedicatedPhaseTriggers(DateTime now, IReadOnlyDictionary<uint, string> members)
	{
		if (futuresRewrittenController.Stage == FuturesRewrittenStage.Phase2 && EnemyListReader.TryIsEmpty(out bool enemyListIsEmpty))
		{
			ApplyDedicatedTransition(futuresRewrittenController.OnEnemyListState(enemyListIsEmpty), now, members);
		}

		if (futuresRewrittenController.Stage == FuturesRewrittenStage.WaitingForPhase5Targetable)
		{
			uint kefkaEntityId = FindTargetableEnemy("ケフカ");
			if (kefkaEntityId != 0)
			{
				ApplyDedicatedTransition(futuresRewrittenController.OnKefkaTargetable(), now, members, kefkaEntityId);
			}
		}
	}

	private bool IsNamedEnemy(uint entityId, string expectedName) =>
		objectTable.SearchByEntityId(entityId) is ICharacter character &&
		string.Equals(character.Name.TextValue, expectedName, StringComparison.Ordinal);

	private uint FindTargetableEnemy(string expectedName)
	{
		foreach (IGameObject gameObject in objectTable)
		{
			if (gameObject != null && gameObject.IsTargetable &&
				string.Equals(gameObject.Name.TextValue, expectedName, StringComparison.Ordinal))
			{
				return gameObject.EntityId;
			}
		}

		return 0;
	}

	private void ApplyDedicatedTransition(DedicatedPhaseTransition transition, DateTime timestamp, IReadOnlyDictionary<uint, string> members, uint anchorEntityId = 0)
	{
		if (transition.Command == DedicatedPhaseCommand.None)
		{
			return;
		}

		if (transition.Command == DedicatedPhaseCommand.End)
		{
			EndPhase(timestamp);
			if (transition.PhaseNumber == 3)
			{
				nextDedicatedPhaseAttackAfter = timestamp;
			}
			log.Information("絶妖星乱舞 Phase {PhaseNumber} の計測を終了しました。", transition.PhaseNumber);
			return;
		}

		if (Aggregator.CurrentPhase != null)
		{
			EndPhase(timestamp);
		}
		BeginPhase(timestamp, members, anchorEntityId);
		if (transition.PhaseNumber == 4)
		{
			nextDedicatedPhaseAttackAfter = null;
		}
		log.Information("絶妖星乱舞 Phase {PhaseNumber} の計測を開始しました。", transition.PhaseNumber);
	}

	private void CheckForPhaseEnd(DateTime now)
	{
		if (Aggregator.CurrentPhase == null)
		{
			return;
		}
		DateTime valueOrDefault;
		if (anchorTargetEntityId != 0)
		{
			IGameObject? gameObject = objectTable.SearchByEntityId(anchorTargetEntityId);
			if (gameObject != null && gameObject.IsTargetable)
			{
				anchorWasTargetable = true;
			}
			else if (anchorWasTargetable)
			{
				EndPhase(now);
				return;
			}
		}
		if (condition[ConditionFlag.InCombat])
		{
			combatLostAt = null;
			return;
		}
		valueOrDefault = combatLostAt.GetValueOrDefault();
		if (!combatLostAt.HasValue)
		{
			valueOrDefault = now;
			combatLostAt = valueOrDefault;
		}
		if ((now - combatLostAt.Value).TotalSeconds >= 1.0)
		{
			EndPhase(now);
		}
	}

	private void EndPhase(DateTime now)
	{
		if (Aggregator.EndCurrentPhase(now))
		{
			ResetPhaseDetection();
		}
	}

	private void OnTerritoryChanged(uint _)
	{
		EndPhase(DateTime.UtcNow);
		periodicAttributions.Clear();
		ResetEncounterDetection();
	}

	private void OnDutyWiped(IDutyStateEventArgs _)
	{
		ArchiveCurrentCombat(CombatHistoryEndReason.Wipe);
	}

	private void OnDutyCompleted(IDutyStateEventArgs _)
	{
		dutyCompletionPending = true;
	}

	private void CompleteDuty(IReadOnlyDictionary<uint, string> members, DateTime now)
	{
		dutyCompletionPending = false;
		if (ActivePreset == PhaseDetectionPreset.FuturesRewrittenUltimate)
		{
			DedicatedPhaseTransition transition = futuresRewrittenController.OnDutyCompleted();
			DateTime phaseEnd = lastPartyDamageAt ?? now;
			ApplyDedicatedTransition(transition, phaseEnd, members);
		}

		ArchiveCurrentCombat(CombatHistoryEndReason.DutyCompleted);
	}

	private void ArchiveCurrentCombat(CombatHistoryEndReason endReason)
	{
		CombatHistoryRecord combatHistoryRecord = Aggregator.ArchiveCurrent(DateTime.UtcNow, endReason);
		if (combatHistoryRecord != null)
		{
			Aggregator.TrimArchivedHistory(configuration.MaxEncounterHistory);
			log.Information("戦闘履歴 #{Number} を保存しました（{Reason}、{PhaseCount}フェーズ）。", combatHistoryRecord.Number, endReason, combatHistoryRecord.Phases.Count);
		}
		configuration.SelectedEntityId = 0u;
		configuration.Save();
		periodicAttributions.Clear();
		ResetEncounterDetection();
		DrainPendingCapture();
	}

	private void DrainPendingCapture()
	{
		if (capture != null)
		{
			RawActionEvent actionEvent;
			while (capture.TryDequeue(out actionEvent))
			{
			}
			RawPeriodicEvent periodicEvent;
			while (capture.TryDequeuePeriodic(out periodicEvent))
			{
			}
		}
	}

	private void ResetPhaseDetection()
	{
		anchorTargetEntityId = 0u;
		anchorWasTargetable = false;
		combatLostAt = null;
	}

	private void ResetEncounterDetection()
	{
		ResetPhaseDetection();
		futuresRewrittenController.Reset();
		lastPartyDamageAt = null;
		nextDedicatedPhaseAttackAfter = null;
		dutyCompletionPending = false;
		while (dialogueEvents.TryDequeue(out _))
		{
		}
	}
}
