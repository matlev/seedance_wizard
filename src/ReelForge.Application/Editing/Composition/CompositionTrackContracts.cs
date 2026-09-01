using ReelForge.Core;

namespace ReelForge.Application;

/// <summary>The media domain of a persisted Working Composition track.</summary>
public enum CompositionTrackKind
{
    Video,
    Audio
}

/// <summary>Result of one track-management command against the committed Working Composition.</summary>
public sealed record CompositionTrackCommandResult(
    RecipeRevision Revision,
    Guid TrackId,
    bool Changed);
