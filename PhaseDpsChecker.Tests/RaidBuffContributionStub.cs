using System.Collections.Generic;

namespace PhaseDpsChecker.Combat;

public sealed record RaidBuffContribution(
	double ExternalDamageReceived,
	IReadOnlyDictionary<uint, double> DamageGrantedByProvider,
	bool HasExternalCriticalBuff,
	bool HasExternalDirectHitBuff);
