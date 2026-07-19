namespace PhaseDpsChecker.Combat;

public enum CombatStatusSide
{
	Self,
	Enemy,
}

public enum CombatStatusKind
{
	Buff,
	Debuff,
}

public sealed record CombatStatusSnapshot(
	uint StatusId,
	string Name,
	ushort Stacks,
	float RemainingSeconds,
	CombatStatusSide Side = CombatStatusSide.Self,
	CombatStatusKind Kind = CombatStatusKind.Buff);
