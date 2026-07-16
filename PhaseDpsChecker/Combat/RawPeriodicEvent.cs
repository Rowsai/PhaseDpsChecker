using System;

namespace PhaseDpsChecker.Combat;

public sealed record RawPeriodicEvent(DateTime Timestamp, uint TargetEntityId, uint StatusId, uint Amount, uint SourceEntityId, bool IsHealing);
