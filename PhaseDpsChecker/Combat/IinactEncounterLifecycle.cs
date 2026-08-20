namespace PhaseDpsChecker.Combat;

internal enum IinactEncounterTransition
{
	None,
	Started,
	Ended,
}

internal sealed class IinactEncounterLifecycle
{
	private long lastSequence;
	private bool isActive;

	public IinactEncounterTransition Observe(IinactCombatSnapshot snapshot)
	{
		if (snapshot.Sequence <= lastSequence)
		{
			return IinactEncounterTransition.None;
		}
		lastSequence = snapshot.Sequence;

		if (snapshot.IsActive)
		{
			bool started = !isActive;
			isActive = true;
			return started ? IinactEncounterTransition.Started : IinactEncounterTransition.None;
		}

		if (!isActive)
		{
			return IinactEncounterTransition.None;
		}
		isActive = false;
		return IinactEncounterTransition.Ended;
	}
}
