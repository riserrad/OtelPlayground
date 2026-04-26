namespace SpaceStationMonitor.Achievements;

/// <summary>
/// A session-scoped achievement defined by a predicate over <see cref="Station"/> state.
/// </summary>
/// <param name="Name">Stable identifier used as the achievement key and emitted as the <c>achievement.name</c> attribute.</param>
/// <param name="Description">Player-facing description shown on unlock.</param>
/// <param name="Predicate">Returns true when the achievement should fire for the given station state.</param>
public sealed record Achievement(string Name, string Description, Func<Station, bool> Predicate);
