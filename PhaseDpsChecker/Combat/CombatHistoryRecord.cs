using System;
using System.Collections.Generic;
using System.Linq;

namespace PhaseDpsChecker.Combat;

public sealed class CombatHistoryRecord
{
	public int Number { get; }

	public DateTime ArchivedAt { get; }

	public CombatHistoryEndReason EndReason { get; }

	public IReadOnlyList<PhaseRecord> Phases { get; }

	public DateTime StartedAt
	{
		get
		{
			if (Phases.Count != 0)
			{
				return Phases.Min((PhaseRecord phase) => phase.StartedAt);
			}
			return ArchivedAt;
		}
	}

	public DateTime EndedAt
	{
		get
		{
			if (Phases.Count != 0)
			{
				return Phases.Max((PhaseRecord phase) => phase.EndedAt ?? ArchivedAt);
			}
			return ArchivedAt;
		}
	}

	public CombatHistoryRecord(int number, DateTime archivedAt, CombatHistoryEndReason endReason, IReadOnlyList<PhaseRecord> phases)
	{
		Number = number;
		ArchivedAt = archivedAt;
		EndReason = endReason;
		Phases = phases;
	}
}
