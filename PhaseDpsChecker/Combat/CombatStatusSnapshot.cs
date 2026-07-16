namespace PhaseDpsChecker.Combat;

public sealed record CombatStatusSnapshot(
	uint StatusId,
	string Name,
	ushort Stacks,
	float RemainingSeconds);
