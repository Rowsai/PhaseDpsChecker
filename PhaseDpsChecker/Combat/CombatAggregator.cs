using System;
using System.Collections.Generic;
using System.Linq;

namespace PhaseDpsChecker.Combat;

public sealed class CombatAggregator
{
	private readonly List<PhaseRecord> phases = new List<PhaseRecord>();

	private readonly List<CombatHistoryRecord> histories = new List<CombatHistoryRecord>();

	private int nextPhaseNumber = 1;

	private int nextHistoryNumber = 1;

	public IReadOnlyList<PhaseRecord> Phases => phases;

	public IReadOnlyList<CombatHistoryRecord> Histories => histories;

	public PhaseRecord? CurrentPhase
	{
		get
		{
			if (phases.Count > 0)
			{
				List<PhaseRecord> list = phases;
				if (list[list.Count - 1].IsActive)
				{
					List<PhaseRecord> list2 = phases;
					return list2[list2.Count - 1];
				}
			}
			return null;
		}
	}

	public PhaseRecord BeginPhase(DateTime timestamp, IReadOnlyDictionary<uint, string> partyMembers, uint anchorTargetId)
	{
		if (CurrentPhase != null)
		{
			return CurrentPhase;
		}
		PhaseRecord phaseRecord = new PhaseRecord(nextPhaseNumber++, timestamp, anchorTargetId);
		foreach (KeyValuePair<uint, string> partyMember in partyMembers)
		{
			phaseRecord.EnsurePlayer(partyMember.Key, partyMember.Value);
		}
		phases.Add(phaseRecord);
		return phaseRecord;
	}

	public void RecordAction(CombatActionEvent actionEvent, IReadOnlySet<uint> currentPartyEntityIds)
	{
		PhaseRecord currentPhase = CurrentPhase;
		if (currentPhase == null || !currentPartyEntityIds.Contains(actionEvent.SourceEntityId))
		{
			return;
		}
		PlayerPhaseStatistics playerPhaseStatistics = currentPhase.EnsurePlayer(actionEvent.SourceEntityId, actionEvent.PlayerName);
		ActionStatistics action = playerPhaseStatistics.GetAction(actionEvent.ActionId, actionEvent.ActionName, actionEvent.Kind, actionEvent.CountsAsUse);
		bool hasDamage = actionEvent.IsOffensiveGcd;
		bool hasHealing = false;
		foreach (EffectSample effect in actionEvent.Effects)
		{
			bool targetsParty = currentPartyEntityIds.Contains(effect.TargetEntityId);
			hasDamage |= effect.Damage != 0 && !targetsParty;
			hasHealing |= effect.Healing != 0 && targetsParty;
		}
		if (actionEvent.IsGcd)
		{
			playerPhaseStatistics.AddGcdInterval(actionEvent.Timestamp, actionEvent.GcdDurationSeconds, hasDamage, hasHealing);
		}
		foreach (EffectSample effect in actionEvent.Effects)
		{
			bool flag = currentPartyEntityIds.Contains(effect.TargetEntityId);
			if (effect.Damage != 0 && !flag)
			{
				playerPhaseStatistics.AddDamage(actionEvent.ActionName, action, effect);
			}
			if (effect.Healing != 0 && flag)
			{
				playerPhaseStatistics.AddHealing(action, effect);
			}
		}
	}

	public void RecordIncomingDamage(IncomingDamageEvent damageEvent, IReadOnlySet<uint> currentPartyEntityIds)
	{
		PhaseRecord currentPhase = CurrentPhase;
		if (currentPhase == null || damageEvent.Amount == 0 || !currentPartyEntityIds.Contains(damageEvent.PlayerEntityId))
		{
			return;
		}
		currentPhase.EnsurePlayer(damageEvent.PlayerEntityId, damageEvent.PlayerName);
		currentPhase.AddIncomingDamage(damageEvent);
	}

	public bool EndCurrentPhase(DateTime timestamp)
	{
		PhaseRecord currentPhase = CurrentPhase;
		if (currentPhase == null)
		{
			return false;
		}
		currentPhase.EndedAt = ((timestamp < currentPhase.StartedAt) ? currentPhase.StartedAt : timestamp);
		return true;
	}

	public CombatHistoryRecord? ArchiveCurrent(DateTime timestamp, CombatHistoryEndReason endReason)
	{
		EndCurrentPhase(timestamp);
		if (phases.Count == 0)
		{
			return null;
		}
		CombatHistoryRecord combatHistoryRecord = new CombatHistoryRecord(nextHistoryNumber++, timestamp, endReason, phases.ToArray());
		histories.Add(combatHistoryRecord);
		phases.Clear();
		nextPhaseNumber = 1;
		return combatHistoryRecord;
	}

	public void TrimCurrentPhases(int maximumPhases)
	{
		maximumPhases = Math.Max(1, maximumPhases);
		while (phases.Count > maximumPhases && !phases[0].IsActive)
		{
			phases.RemoveAt(0);
		}
	}

	public bool TrimArchivedHistory(int maximumEncounters)
	{
		bool changed = false;
		maximumEncounters = Math.Max(1, maximumEncounters);
		while (histories.Count > maximumEncounters)
		{
			histories.RemoveAt(0);
			changed = true;
		}
		return changed;
	}

	internal void RestoreArchivedHistory(IEnumerable<CombatHistoryRecord> restoredHistories)
	{
		histories.Clear();
		histories.AddRange(restoredHistories.OrderBy(history => history.Number));
		nextHistoryNumber = histories.Count == 0 ? 1 : histories.Max(history => history.Number) + 1;
	}

	public void ClearCurrent()
	{
		phases.Clear();
		nextPhaseNumber = 1;
	}

	public void ClearArchivedHistory()
	{
		histories.Clear();
		nextHistoryNumber = 1;
	}
}
