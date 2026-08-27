Set-StrictMode -Version Latest

# Stage 2A-only semantic helpers. The retained pre-matrix smoke helper remains
# byte-identical because historical evidence binds its exact SHA-256.

function Get-G05Stage2ACombinedGraph([object] $Workload, [object] $Variant) {
    if ($null -eq $Workload -or $null -eq $Variant) { throw 'Workload and variant are required for Stage 2A graph expansion.' }
    $graph = ([string]$Workload.videoFilterGraph) + ';' + ([string]$Workload.audioFilterGraph)
    foreach ($property in @($Variant.PSObject.Properties)) {
        if ($null -eq $property.Value -or [string]::IsNullOrWhiteSpace([string]$property.Value)) { throw "Variant property is blank: $($property.Name)." }
        $graph = $graph.Replace("{variant.$($property.Name)}", [string]$property.Value)
    }
    if ($graph -match '\{variant\.[^}]+\}') { throw "Frozen Stage 2A graph contains an unresolved variant placeholder: $($Matches[0])." }
    $graph
}

function New-G05Stage2AAudioTruth([string] $FixtureRoot, [object] $Workload, [string] $OutputPath, [object] $Descriptor) {
    if (Test-Path -LiteralPath $OutputPath) { throw 'Stage 2A audio truth output already exists.' }
    $parent = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($OutputPath))
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) { throw 'Stage 2A audio truth parent is absent.' }
    if (-not ('ReelForge.Gate0.Stage2AAudioTruth' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.IO;
namespace ReelForge.Gate0 {
  public static class Stage2AAudioTruth {
    public static void Render(string output, string[] paths, int[] channels, int[] starts, double[] gains, double[,] matrix) {
      short[][] sources = new short[paths.Length][];
      for (int i = 0; i < sources.Length; i++) {
        byte[] bytes = File.ReadAllBytes(paths[i]);
        if (bytes.Length == 0 || bytes.Length % (channels[i] * 2) != 0) throw new InvalidOperationException("Invalid Stage 2A audio source closure.");
        sources[i] = new short[bytes.Length / 2];
        Buffer.BlockCopy(bytes, 0, sources[i], 0, bytes.Length);
      }
      using var writer = new BinaryWriter(new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None));
      for (int n = 0; n < 1440000; n++) {
        double left = 0, right = 0;
        for (int i = 0; i < sources.Length; i++) {
          if (n < starts[i]) continue;
          int sourceFrame = ((n - starts[i]) % (sources[i].Length / channels[i])) * channels[i];
          double sourceLeft = sources[i][sourceFrame];
          double sourceRight = channels[i] == 1 ? sourceLeft : sources[i][sourceFrame + 1];
          left += gains[i] * (matrix[i,0] * sourceLeft + matrix[i,1] * sourceRight);
          right += gains[i] * (matrix[i,2] * sourceLeft + matrix[i,3] * sourceRight);
        }
        writer.Write(ToInt16(left)); writer.Write(ToInt16(right));
      }
    }
    private static short ToInt16(double value) => (short)Math.Clamp(Math.Round(value, MidpointRounding.ToEven), short.MinValue, short.MaxValue);
  }
}
'@
    }
    $sources = @{
        'f1-audio' = @('F1/f1-sync-440hz-880hz-48000-stereo.pcm', 2)
        'f8-audio-440' = @('F8/f8-audio-zero-440hz.pcm', 1)
        'f2-audio-660' = @('F2/f2-48000-stereo-660hz.pcm', 2)
        'f8-audio-880' = @('F8/f8-audio-one-880hz.pcm', 1)
        'f4-audio-1000' = @('F4/f4-stereo-48000-1000hz-opposed.pcm', 2)
    }
    $expected = @{
        'baseline-1v1a' = [ordered]@{ descriptor='f1-loop-30s'; tracks=1; size=5760000; sha256='0C8C1E73ADACCB558CA563299A3FF238649A4995599AA49D2A6C37FE95AAC730' }
        'typical-2v4a' = [ordered]@{ descriptor='typical-2v4a-30s'; tracks=4; size=5760000; sha256='81B41CD4DB85568930C15282A7268E2CED2610D27D48C6CB258E1D1C5C1B8C5A' }
        'stress-4v8a' = [ordered]@{ descriptor='stress-4v8a-30s'; tracks=8; size=5760000; sha256='299846E21A0AF6F1416CCA7BF1BF8ACAC4A5EDDA78EFF9BEB392CC7B992B8CF5' }
    }
    $workloadId = [string]$Workload.id
    if (-not $expected.ContainsKey($workloadId)) { throw "Stage 2A audio truth is not defined for workload: $workloadId." }
    $definition = $expected[$workloadId]
    if ([string]$Workload.audioReferenceDescriptor -ne [string]$definition.descriptor -or [string]$Descriptor.id -ne [string]$definition.descriptor -or
        [int64]$Descriptor.referencePcmSize -ne [int64]$definition.size -or [string]$Descriptor.referencePcmSha256 -ne [string]$definition.sha256) {
        throw 'Selected Stage 2A audio descriptor does not match the frozen truth mapping.'
    }
    $tracks = @($Workload.audioTracks)
    if ($tracks.Count -ne [int]$definition.tracks) { throw "Frozen workload requires exactly $($definition.tracks) structured audio tracks." }
    $paths=[Collections.Generic.List[string]]::new();$channels=[Collections.Generic.List[int]]::new();$starts=[Collections.Generic.List[int]]::new();$gains=[Collections.Generic.List[double]]::new();$matrix=New-Object 'double[,]' $tracks.Count,4
    for ($index=0; $index -lt $tracks.Count; $index++) {
        $track=$tracks[$index]
        if (-not $sources.ContainsKey([string]$track.source) -or -not [bool]$track.loop -or [int]$track.startMs -lt 0 -or [double]$track.gain -le 0) { throw "Frozen Stage 2A audio track is invalid: $($track.id)." }
        $source=$sources[[string]$track.source];$sourcePath=Join-Path $FixtureRoot $source[0]
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Stage 2A audio source is absent: $($track.source)." }
        $paths.Add($sourcePath);$channels.Add([int]$source[1]);$starts.Add(48*[int]$track.startMs);$gains.Add([double]$track.gain)
        $values=switch([string]$track.pan){
            'identity-stereo'{@(1.0,0.0,0.0,1.0);break}
            'stereo|c0=c0|c1=0.25*c0'{@(1.0,0.0,0.25,0.0);break}
            'stereo|c0=0.25*c0|c1=c0'{@(0.25,0.0,1.0,0.0);break}
            'stereo|c0=c0|c1=0.20*c0'{@(1.0,0.0,0.20,0.0);break}
            'stereo|c0=0.20*c0|c1=c0'{@(0.20,0.0,1.0,0.0);break}
            'stereo|c0=0.35*c0|c1=c0'{@(0.35,0.0,1.0,0.0);break}
            'stereo|c0=c0|c1=0.35*c1'{@(1.0,0.0,0.0,0.35);break}
            'stereo|c0=c0|c1=0.35*c0'{@(1.0,0.0,0.35,0.0);break}
            'stereo|c0=0.35*c0|c1=c1'{@(0.35,0.0,0.0,1.0);break}
            default{throw "Unknown frozen Stage 2A pan recipe: $($track.pan)"}
        }
        for($column=0;$column-lt4;$column++){$matrix[$index,$column]=[double]$values[$column]}
    }
    [ReelForge.Gate0.Stage2AAudioTruth]::Render($OutputPath,$paths.ToArray(),$channels.ToArray(),$starts.ToArray(),$gains.ToArray(),$matrix)
    if ((Get-Item -LiteralPath $OutputPath).Length -ne [int64]$definition.size -or (Get-G05SmokeHash $OutputPath) -ne [string]$definition.sha256) { throw 'Stage 2A audio truth bytes do not match the frozen descriptor.' }
    $OutputPath
}

function Initialize-G05Stage2AVisualOracle {
    if ('ReelForge.Gate0.Stage2AVisualOracle' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Security.Cryptography;
namespace ReelForge.Gate0 {
  public sealed class Stage2AVisualResult {
    public double[] FrameMeanAbsoluteErrors { get; set; } = Array.Empty<double>();
    public double MaximumFrameMeanAbsoluteError { get; set; }
    public int Frames { get; set; }
    public bool ImmediateEof { get; set; }
    public string DecodedVideoIdentitySha256 { get; set; } = "";
  }
  public static class Stage2AVisualOracle {
    public static int MapNeighbor(int outputCoordinate, int cropCoordinate, int cropSize, int outputSize) => cropCoordinate + outputCoordinate * cropSize / outputSize;
    public static Stage2AVisualResult Compare(Stream stream, string[] f1Paths, string landscapePath, string portraitPath, string alphaPath, string workload, int width, int height, int pipX, int pipY) {
      if ((width != 1280 || height != 720) && (width != 1920 || height != 1080)) throw new InvalidOperationException("Unknown Stage 2A visual resolution.");
      byte[][] f1 = { ReadPpm(f1Paths[0],320,180), ReadPpm(f1Paths[1],320,180), ReadPpm(f1Paths[2],320,180) };
      byte[] landscape = workload == "stress-4v8a" ? ReadPpm(landscapePath,640,360) : null;
      byte[] portrait = workload == "stress-4v8a" ? ReadPpm(portraitPath,360,640) : null;
      byte[] alpha = workload == "typical-2v4a" || workload == "stress-4v8a" ? File.ReadAllBytes(alphaPath) : null;
      if (alpha != null && alpha.Length != 320*180*4) throw new InvalidOperationException("F3 RGBA geometry mismatch.");
      byte[][] expected = new byte[3][];
      for (int i=0;i<3;i++) expected[i]=Compose(f1[i],landscape,portrait,alpha,workload,width,height,pipX,pipY);
      byte[] actual=new byte[width*height*3];double[] maes=new double[750];double maximum=0;using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
      for(int frame=0;frame<750;frame++){ReadExactly(stream,actual);hash.AppendData(actual);byte[] truth=expected[frame%3];long sum=0;for(int i=0;i<actual.Length;i++)sum+=Math.Abs(actual[i]-truth[i]);double mae=sum/(double)actual.Length;maes[frame]=mae;if(mae>maximum)maximum=mae;}
      bool eof=stream.ReadByte()==-1;if(!eof)throw new InvalidOperationException("RGB stream contained bytes after frame 749.");
      return new Stage2AVisualResult{FrameMeanAbsoluteErrors=maes,MaximumFrameMeanAbsoluteError=maximum,Frames=750,ImmediateEof=eof,DecodedVideoIdentitySha256=Convert.ToHexString(hash.GetHashAndReset())};
    }
    private static byte[] Compose(byte[] f1,byte[] landscape,byte[] portrait,byte[] alpha,string workload,int width,int height,int pipX,int pipY){
      byte[] output=ScaleBilinear(f1,320,180,width,height);
      if(workload=="baseline-1v1a")return output;
      if(workload=="typical-2v4a"){int pw=width/4,ph=height/3;if((width==1280&&(pipX!=880||pipY!=440))||(width==1920&&(pipX!=1320||pipY!=660)))throw new InvalidOperationException("Typical PIP geometry differs from the frozen variant.");OverlayAlphaNeighbor(output,width,alpha,320,0,0,80,60,pw,ph,pipX,pipY);return output;}
      if(workload!="stress-4v8a")throw new InvalidOperationException("Unknown frozen Stage 2A visual workload.");
      int halfWidth=width/2,halfHeight=height/2;OverlayOpaqueBilinear(output,width,landscape,640,360,0,0,640,360,halfWidth,halfHeight,0,0);OverlayOpaqueBilinear(output,width,portrait,360,640,0,140,360,360,halfWidth,halfHeight,halfWidth,0);OverlayAlphaNeighbor(output,width,alpha,320,80,45,160,90,width/4,height/3,3*width/8,2*height/3);return output;
    }
    private static byte[] ScaleBilinear(byte[] source,int sourceWidth,int sourceHeight,int width,int height){byte[] result=new byte[width*height*3];for(int y=0;y<height;y++)for(int x=0;x<width;x++)for(int c=0;c<3;c++)result[(y*width+x)*3+c]=SampleBilinear(source,sourceWidth,sourceHeight,(x+.5)*sourceWidth/width-.5,(y+.5)*sourceHeight/height-.5,c);return result;}
    private static byte SampleBilinear(byte[] source,int width,int height,double x,double y,int channel){int floorX=(int)Math.Floor(x),floorY=(int)Math.Floor(y),x0=Math.Clamp(floorX,0,width-1),y0=Math.Clamp(floorY,0,height-1),x1=Math.Clamp(floorX+1,0,width-1),y1=Math.Clamp(floorY+1,0,height-1);double fx=x-floorX,fy=y-floorY,top=(1-fx)*source[(y0*width+x0)*3+channel]+fx*source[(y0*width+x1)*3+channel],bottom=(1-fx)*source[(y1*width+x0)*3+channel]+fx*source[(y1*width+x1)*3+channel];return(byte)Math.Clamp(Math.Round((1-fy)*top+fy*bottom,MidpointRounding.ToEven),0,255);}
    private static void OverlayOpaqueBilinear(byte[] destination,int destinationWidth,byte[] source,int sourceWidth,int sourceHeight,int cropX,int cropY,int cropWidth,int cropHeight,int outputWidth,int outputHeight,int outputX,int outputY){for(int y=0;y<outputHeight;y++)for(int x=0;x<outputWidth;x++)for(int c=0;c<3;c++)destination[((outputY+y)*destinationWidth+outputX+x)*3+c]=SampleBilinear(source,sourceWidth,sourceHeight,cropX+(x+.5)*cropWidth/outputWidth-.5,cropY+(y+.5)*cropHeight/outputHeight-.5,c);}
    private static void OverlayAlphaNeighbor(byte[] destination,int destinationWidth,byte[] source,int sourceWidth,int cropX,int cropY,int cropWidth,int cropHeight,int outputWidth,int outputHeight,int outputX,int outputY){for(int y=0;y<outputHeight;y++)for(int x=0;x<outputWidth;x++){int sx=MapNeighbor(x,cropX,cropWidth,outputWidth),sy=MapNeighbor(y,cropY,cropHeight,outputHeight),sourceOffset=(sy*sourceWidth+sx)*4,destinationOffset=((outputY+y)*destinationWidth+outputX+x)*3,alpha=source[sourceOffset+3];for(int c=0;c<3;c++)destination[destinationOffset+c]=(byte)((source[sourceOffset+c]*alpha+destination[destinationOffset+c]*(255-alpha)+127)/255);}}
    private static void ReadExactly(Stream stream,byte[] buffer){int offset=0;while(offset<buffer.Length){int read=stream.Read(buffer,offset,buffer.Length-offset);if(read==0)throw new InvalidOperationException("RGB stream ended before frame 749.");offset+=read;}}
    private static byte[] ReadPpm(string path,int width,int height){byte[] bytes=File.ReadAllBytes(path);int offset=0;string magic=Token(bytes,ref offset),w=Token(bytes,ref offset),h=Token(bytes,ref offset),max=Token(bytes,ref offset);if(magic!="P6"||w!=width.ToString()||h!=height.ToString()||max!="255")throw new InvalidOperationException("PPM header mismatch.");while(offset<bytes.Length&&char.IsWhiteSpace((char)bytes[offset]))offset++;byte[] pixels=new byte[width*height*3];if(bytes.Length-offset!=pixels.Length)throw new InvalidOperationException("PPM payload mismatch.");Buffer.BlockCopy(bytes,offset,pixels,0,pixels.Length);return pixels;}
    private static string Token(byte[] bytes,ref int offset){while(true){while(offset<bytes.Length&&char.IsWhiteSpace((char)bytes[offset]))offset++;if(offset<bytes.Length&&bytes[offset]=='#'){while(offset<bytes.Length&&bytes[offset]!='\n')offset++;continue;}break;}int start=offset;while(offset<bytes.Length&&!char.IsWhiteSpace((char)bytes[offset]))offset++;return System.Text.Encoding.ASCII.GetString(bytes,start,offset-start);}
  }
}
'@
}

function Test-G05Stage2AVisual([string]$Ffmpeg,[string]$Demuxer,[string]$VideoDecoder,[string]$Output,[string]$FixtureRoot,[string]$LogPath,[string]$MetricsPath,[object]$Workload,[object]$Variant) {
    Initialize-G05Stage2AVisualOracle
    $startInfo=[Diagnostics.ProcessStartInfo]::new($Ffmpeg);$startInfo.UseShellExecute=$false;$startInfo.RedirectStandardOutput=$true;$startInfo.RedirectStandardError=$true;$startInfo.CreateNoWindow=$true
    foreach($token in @('-v','error','-xerror','-err_detect','explode','-f',$Demuxer,'-c:v',$VideoDecoder,'-i',$Output,'-map','0:v:0','-an','-fps_mode','passthrough','-c:v','rawvideo','-pix_fmt','rgb24','-f','rawvideo','pipe:1')){[void]$startInfo.ArgumentList.Add($token)}
    $process=[Diagnostics.Process]::Start($startInfo);$stderrTask=$process.StandardError.ReadToEndAsync();$failure=$null
    $pipX=if($null-ne$Variant.PSObject.Properties['pipX']){[int]$Variant.pipX}else{0};$pipY=if($null-ne$Variant.PSObject.Properties['pipY']){[int]$Variant.pipY}else{0}
    try{$result=[ReelForge.Gate0.Stage2AVisualOracle]::Compare($process.StandardOutput.BaseStream,@((Join-Path $FixtureRoot 'F1/f1-pattern-000.ppm'),(Join-Path $FixtureRoot 'F1/f1-pattern-001.ppm'),(Join-Path $FixtureRoot 'F1/f1-pattern-002.ppm')),(Join-Path $FixtureRoot 'F2/f2-landscape-640x360-25fps.ppm'),(Join-Path $FixtureRoot 'F2/f2-portrait-360x640-30000_1001fps.ppm'),(Join-Path $FixtureRoot 'F3/f3-alpha-magenta-50pct.rgba'),[string]$Workload.id,[int]$Variant.width,[int]$Variant.height,$pipX,$pipY)}catch{$failure=$_}finally{if(-not$process.HasExited){try{$process.Kill($true);$process.WaitForExit(10000)|Out-Null}catch{}};$stderr=$stderrTask.GetAwaiter().GetResult();[IO.File]::WriteAllText($LogPath,$stderr,[Text.UTF8Encoding]::new($false))}
    if($null-ne$failure){throw$failure};if($process.ExitCode-ne0){throw'Strict Stage 2A streaming RGB decode failed.'}
    $records=for($index=0;$index-lt$result.FrameMeanAbsoluteErrors.Length;$index++){[ordered]@{frameIndex=$index;meanAbsoluteError=$result.FrameMeanAbsoluteErrors[$index];maximumPermitted=18;passed=$result.FrameMeanAbsoluteErrors[$index]-le18}}
    [IO.File]::WriteAllLines($MetricsPath,@($records|ForEach-Object{$_|ConvertTo-Json -Compress}),[Text.UTF8Encoding]::new($false))
    [ordered]@{passed=@($records|Where-Object{-not$_.passed}).Count-eq0;frames=$result.Frames;immediateEof=$result.ImmediateEof;maximumFrameMeanAbsoluteError=$result.MaximumFrameMeanAbsoluteError;decodedVideoIdentitySha256=$result.DecodedVideoIdentitySha256;threshold=18;perFrameMetrics=[IO.Path]::GetFileName($MetricsPath);rawFramesRetained=$false;processTree=[ordered]@{rootExited=$process.HasExited;orphanFree=$process.HasExited}}
}

Export-ModuleMember -Function Get-G05Stage2ACombinedGraph,New-G05Stage2AAudioTruth,Initialize-G05Stage2AVisualOracle,Test-G05Stage2AVisual
