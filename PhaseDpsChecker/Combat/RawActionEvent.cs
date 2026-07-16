using System;
using System.Collections.Generic;

namespace PhaseDpsChecker.Combat;

public sealed record RawActionEvent(DateTime Timestamp, uint SourceEntityId, uint ActionId, byte ActionType, IReadOnlyList<EffectSample> Effects, IReadOnlyList<StatusApplication> StatusApplications);
