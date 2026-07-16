using System.Collections.Generic;
using System.Linq;

namespace PhaseDpsChecker.Combat;

public static class PhaseEndDetection
{
	public static bool IsDefeatingHit(uint anchorTargetEntityId, IReadOnlyList<EffectSample> effects, bool anchorIsDefeated)
	{
		return anchorIsDefeated
			&& anchorTargetEntityId != 0
			&& effects.Any(effect => effect.TargetEntityId == anchorTargetEntityId && effect.Damage != 0);
	}
}
