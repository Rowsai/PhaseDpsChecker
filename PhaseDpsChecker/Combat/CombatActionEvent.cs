using System;
using System.Collections.Generic;

namespace PhaseDpsChecker.Combat;

public sealed record CombatActionEvent(DateTime Timestamp, uint SourceEntityId, string PlayerName, uint ActionId, string ActionName, ActionKind Kind, bool CountsAsUse, bool IsGcd, double GcdDurationSeconds, IReadOnlyList<EffectSample> Effects, bool IsOffensiveGcd = false);
