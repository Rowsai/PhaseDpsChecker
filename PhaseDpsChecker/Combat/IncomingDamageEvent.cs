using System;
using System.Collections.Generic;

namespace PhaseDpsChecker.Combat;

public sealed record IncomingDamageEvent(
	DateTime Timestamp,
	uint PlayerEntityId,
	string PlayerName,
	uint SourceEntityId,
	string EnemyName,
	uint ActionId,
	string ActionName,
	uint Amount,
	IReadOnlyList<CombatStatusSnapshot> Statuses);
