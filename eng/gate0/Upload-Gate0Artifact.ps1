[CmdletBinding(DefaultParameterSetName = 'One')]
param(
    [Parameter(Mandatory, ParameterSetName = 'One')] [string] $ArtifactId,
    [Parameter(Mandatory, ParameterSetName = 'All')] [switch] $AllPending,
    [string] $ArtifactRoot,
    [string] $SourceManifestPath,
    [string] $ManifestPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Gate0ArtifactTools.psm1') -Force

if ([string]::IsNullOrWhiteSpace($SourceManifestPath)) { $SourceManifestPath = Join-Path $PSScriptRoot 'artifact-retention-manifest.json' }
if ([string]::IsNullOrWhiteSpace($ManifestPath)) { $ManifestPath = Join-Path $PSScriptRoot 'artifact-manifest.json' }
$source = Read-Gate0SourceInventory ([IO.Path]::GetFullPath($SourceManifestPath))
$remote = Read-Gate0RemoteManifest ([IO.Path]::GetFullPath($ManifestPath))
Assert-Gate0ManifestPair $source $remote
$root = Resolve-Gate0ArtifactRoot $ArtifactRoot

$verifiedById = @{}
foreach ($record in @($remote.Manifest.artifacts)) { $verifiedById[[string] $record.logicalArtifactId] = $record }
$targets = if ($PSCmdlet.ParameterSetName -eq 'One') {
    @($source.Entries | Where-Object ArtifactId -eq $ArtifactId)
}
else {
    @($source.Entries | Where-Object { -not $verifiedById.ContainsKey($_.ArtifactId) })
}
if ($targets.Count -eq 0 -and $PSCmdlet.ParameterSetName -eq 'One') { throw "Unknown Gate 0 artifact ID: $ArtifactId" }
if ($targets.Count -eq 0) {
    [pscustomobject]@{ status = 'nothing-pending'; logicalArtifactCount = 0; bucketName = 'reelforge-artifacts' }
    return
}

$bundle = New-Gate0R2ClientBundle
$verifiedKeys = @{}
try {
    foreach ($entry in $targets) {
        $localPath = Get-Gate0LocalArtifactPath $entry $root
        $disposition = $null
        if ($verifiedKeys.ContainsKey($entry.ObjectKey)) {
            $disposition = 'deduplicated-object-verified-in-this-run'
        }
        else {
            $metadata = $bundle.Client.HeadObjectAsync(
                $bundle.BucketName,
                $entry.ObjectKey).GetAwaiter().GetResult()
            if ($null -ne $metadata -and $null -ne $metadata.ContentLength -and [int64] $metadata.ContentLength -ne $entry.Size) {
                throw "Existing content-addressed R2 object has the wrong size; refusing replacement: $($entry.ObjectKey)"
            }
            if ($null -eq $metadata) {
                $created = $bundle.Client.PutObjectIfAbsentAsync(
                    $bundle.BucketName,
                    $entry.ObjectKey,
                    $localPath,
                    $entry.Sha256).GetAwaiter().GetResult()
                $disposition = if ($created) { 'uploaded-and-verified' } else { 'concurrent-create-verified' }
            }
            else {
                $disposition = 'existing-object-verified'
            }
            Invoke-Gate0RemoteByteVerification $bundle $entry
            $verifiedKeys[$entry.ObjectKey] = $true
        }

        $verifiedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        $remote = Save-Gate0RemoteVerifiedReceipt $source $remote.Path $entry $disposition $verifiedUtc
        [pscustomobject]@{
            logicalArtifactId = $entry.ArtifactId
            byteSize = $entry.Size
            sha256 = $entry.Sha256
            r2ObjectKey = $entry.ObjectKey
            retentionStatus = 'remote-verified'
            transferDisposition = $disposition
            remotelyVerifiedUtc = $verifiedUtc
        }
    }
}
finally {
    $bundle.HttpClient.Dispose()
}
