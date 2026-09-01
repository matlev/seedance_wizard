namespace ReelForge.Infrastructure;

/// <summary>
/// Operational envelope for a project-local recovery candidate. Its payload is the normal
/// project DTO so recovery cannot become a parallel editable project format.
/// </summary>
internal sealed class ProjectRecoveryFileDto
{
    public const int CurrentRecoveryFormatVersion = 1;

    public int RecoveryFormatVersion { get; set; } = CurrentRecoveryFormatVersion;
    public string ProjectPayloadSha256 { get; set; } = string.Empty;
    public string BaseProjectSha256 { get; set; } = string.Empty;
    public ProjectFileDto? Project { get; set; }
}
