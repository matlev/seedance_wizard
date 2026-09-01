namespace ReelForge.Core;

public enum GenerationMode { TextToVideo, ImageToVideo, ReferenceToVideo, VideoEdit }
public enum GenerationStatus { Draft, Queued, Running, Succeeded, Failed, Cancelled }
public enum OutputIngestionStatus { NotRequired, Pending, Running, Succeeded, Failed }
public enum GenerationReferenceObjectKind { Asset, FrameAnchor }
public enum GenerationReferenceRole { GeneralReference, StartFrame, EndFrame, Character, Style, Environment, Motion, Audio }
public enum GenerationRelationshipType { RetryOf, VariantOf, ContinueAfter, ContinueBefore, BasedOn }
