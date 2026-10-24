param([string]$OutputPath = (Join-Path $PSScriptRoot 'PureSmokeResult.json'))
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../../..')).Path
$sourceRoot = Join-Path $projectRoot 'Assets/FitnessSimulator/Production'
$files = @(Get-ChildItem (Join-Path $sourceRoot 'Core/*.cs'); Get-Item (Join-Path $sourceRoot 'Flow/FlowEngine.cs'); Get-Item (Join-Path $sourceRoot 'Persistence/LocalCheckpointStore.cs'))
$parts = $files | ForEach-Object { Get-Content -Raw $_.FullName }
$usingLines = $parts | ForEach-Object { [regex]::Matches($_, '(?m)^using .+;$') | ForEach-Object Value } | Sort-Object -Unique
$bodies = $parts | ForEach-Object { [regex]::Replace($_, '(?m)^using .+;\r?\n', '') }
$harness = @'
namespace PersistenceSmoke {
public sealed class Codec : FitnessSimulator.Game.Core.IStateCodec {
 public string Encode<T>(T value) { return System.Text.Json.JsonSerializer.Serialize(value, new System.Text.Json.JsonSerializerOptions { IncludeFields=true, IgnoreReadOnlyProperties=true }); }
 public T Decode<T>(string json) { return System.Text.Json.JsonSerializer.Deserialize<T>(json, new System.Text.Json.JsonSerializerOptions { IncludeFields=true }); }
}
public sealed class Evidence { public string Point; public string Expected; public string Restored; public string Retry; public bool Passed; }
public static class Runner {
 public static Evidence[] Run() {
  var results=new System.Collections.Generic.List<Evidence>(); var codec=new Codec();
  foreach(FitnessSimulator.Game.Persistence.StorageFaultPoint point in System.Enum.GetValues(typeof(FitnessSimulator.Game.Persistence.StorageFaultPoint))) {
   string path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"FitnessM1PureSmoke",System.Guid.NewGuid().ToString("N"));
   var row=new Evidence {Point=point.ToString()};
   try {
    using(var s=new FitnessSimulator.Game.Persistence.LocalCheckpointStore(path,codec)) {
     var r=new FitnessSimulator.Game.Core.RootEnvelope {RunId="run",ContentVersion="M1Baseline"}; r.SetFragment("fixture","player",1,1,"old");
     var c=s.Commit(0,r,Receipt("base"),System.Array.Empty<FitnessSimulator.Game.Core.LogAppend>());
     if(c.Status!=FitnessSimulator.Game.Core.CommitStatus.Committed)throw new System.Exception("baseline: "+c.Error);
    }
    using(var s=new FitnessSimulator.Game.Persistence.LocalCheckpointStore(path,codec,p=>{if(p==point)throw new System.IO.IOException("injected "+p);})) {
     var r=s.Load().Root;r.SetFragment("fixture","player",1,2,"new");
     var c=s.Commit(r.Generation,r,Receipt("next"),System.Array.Empty<FitnessSimulator.Game.Core.LogAppend>());
     if(c.Status!=FitnessSimulator.Game.Core.CommitStatus.Unknown)throw new System.Exception("expected Unknown: "+c.Error);
    }
    using(var s=new FitnessSimulator.Game.Persistence.LocalCheckpointStore(path,codec)) {
     var loaded=s.Load(); if(loaded.Status!=FitnessSimulator.Game.Core.LoadStatus.Loaded)throw new System.Exception("load: "+loaded.Error);
     row.Expected=(point==FitnessSimulator.Game.Persistence.StorageFaultPoint.AfterCommitReceipt||point==FitnessSimulator.Game.Persistence.StorageFaultPoint.BeforeAcknowledgement)?"new":"old";
     row.Restored=loaded.Root.FindFragment("fixture","player").Json;
     var r=loaded.Root;r.SetFragment("fixture","player",1,2,"new");
     var retry=s.Commit(r.Generation,r,Receipt("next"),System.Array.Empty<FitnessSimulator.Game.Core.LogAppend>());
     row.Retry=retry.Status.ToString();
     row.Passed=row.Expected==row.Restored&&retry.Status==FitnessSimulator.Game.Core.CommitStatus.Committed&&s.Load().Root.Revision==2;
    }
    results.Add(row);
   } finally {if(System.IO.Directory.Exists(path))System.IO.Directory.Delete(path,true); if(System.IO.File.Exists(path+".writer-lock"))System.IO.File.Delete(path+".writer-lock");}
  }
  return results.ToArray();
 }
 private static FitnessSimulator.Game.Core.OperationReceipt Receipt(string id) { return new FitnessSimulator.Game.Core.OperationReceipt {OperationId=id,PayloadHash=id,RunId="run",SemanticKey=id}; }
}}
'@
$source = ($usingLines -join "`n") + "`n" + ($bodies -join "`n") + "`n" + $harness
Add-Type -TypeDefinition $source -ReferencedAssemblies (Get-ChildItem "$PSHOME/ref/*.dll").FullName
$cases = [PersistenceSmoke.Runner]::Run()
$report = [ordered]@{
    generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    scope = 'Pure C# store; System.Text.Json codec; no Unity/NUnit execution; no Android durability claim'
    sourceHashes = @($files | ForEach-Object { @{ path = $_.FullName; sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash } })
    passed = @($cases | Where-Object { -not $_.Passed }).Count -eq 0
    cases = $cases
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding utf8
$report | ConvertTo-Json -Depth 8
if (-not $report.passed) { throw 'Pure persistence smoke failed.' }
