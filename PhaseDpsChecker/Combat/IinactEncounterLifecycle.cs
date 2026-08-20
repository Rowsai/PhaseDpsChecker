namespace PhaseDpsChecker.Combat;

internal enum IinactEncounterTransition
{
	None,
	Started,
	Ended,
}

internal sealed class IinactEncounterLifecycle
{
	private sealed class SourceState
	{
		public long LastSequence { get; set; }
		public bool IsActive { get; set; }
	}

	private readonly System.Collections.Generic.Dictionary<string, SourceState> sources = new(System.StringComparer.Ordinal);
	private bool encounterActive;

	public IinactEncounterTransition Observe(string source, IinactCombatSnapshot snapshot)
	{
		if (!sources.TryGetValue(source, out SourceState? state))
		{
			state = new SourceState();
			sources[source] = state;
		}
		if (snapshot.Sequence <= state.LastSequence)
		{
			return IinactEncounterTransition.None;
		}
		state.LastSequence = snapshot.Sequence;

		if (snapshot.IsActive)
		{
			bool sourceStarted = !state.IsActive;
			state.IsActive = true;
			if (sourceStarted && !encounterActive)
			{
				encounterActive = true;
				return IinactEncounterTransition.Started;
			}
			return IinactEncounterTransition.None;
		}

		if (!state.IsActive)
		{
			return IinactEncounterTransition.None;
		}
		state.IsActive = false;
		if (encounterActive)
		{
			encounterActive = false;
			return IinactEncounterTransition.Ended;
		}
		return IinactEncounterTransition.None;
	}

	public IinactEncounterTransition Observe(IinactCombatSnapshot snapshot) => Observe("default", snapshot);
}
