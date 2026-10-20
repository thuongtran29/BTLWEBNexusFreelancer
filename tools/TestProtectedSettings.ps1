param([switch]$MutationCheck)
$ErrorActionPreference='Stop'
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vsRoot=& $vswhere -latest -products Microsoft.VisualStudio.Product.Community Microsoft.VisualStudio.Product.Professional Microsoft.VisualStudio.Product.Enterprise -property installationPath
$compiler=Join-Path $vsRoot 'MSBuild/Current/Bin/Roslyn/csc.exe'
$tempRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/')
$temp=Join-Path $tempRoot ('NexusProtected-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $temp 'web') -Force | Out-Null
try {
 $helper=Join-Path (Split-Path -Parent $PSScriptRoot) 'NexusFreelance.Web/Services/IntegrationSettings.cs'
 $output=Join-Path $temp 'web/TestProtected.exe'
 $shared=Join-Path $temp 'shared-integrations.dat'
 [IO.File]::WriteAllText(($output+'.config'),('<configuration><appSettings><add key="NexusIntegrationStore" value="'+[Security.SecurityElement]::Escape($shared)+'" /></appSettings></configuration>'))
 $arguments=@('/nologo','/target:exe','/codepage:65001',"/out:$output",'/reference:System.Web.dll','/reference:System.Web.Extensions.dll','/reference:System.Security.dll','/reference:System.Configuration.dll',(Join-Path $PSScriptRoot 'TestProtectedSettings.cs'))
 & $compiler @arguments $helper
 if($LASTEXITCODE -ne 0){throw 'Protected settings harness compilation failed.'}
 & $output
 if($LASTEXITCODE -ne 0){throw 'Protected settings checks failed.'}
 if($MutationCheck){
  $copy=Join-Path $temp 'BrokenSettings.cs'
  $text=[IO.File]::ReadAllText($helper).Replace('cached=ReadProtected(path)','cached=new Dictionary<string,string>()')
  [IO.File]::WriteAllText($copy,$text)
  & $compiler @arguments $copy
  if($LASTEXITCODE -ne 0){throw 'Mutation compilation failed.'}
  # An expected nonzero native exit must not be converted into a terminating
  # PowerShell stderr error before the mutation assertion can inspect it.
  $processInfo=New-Object Diagnostics.ProcessStartInfo
  $processInfo.FileName=$output; $processInfo.UseShellExecute=$false; $processInfo.CreateNoWindow=$true
  $processInfo.RedirectStandardOutput=$true; $processInfo.RedirectStandardError=$true
  $mutationProcess=[Diagnostics.Process]::Start($processInfo)
  $mutationOutput=$mutationProcess.StandardOutput.ReadToEnd()+$mutationProcess.StandardError.ReadToEnd()
  $mutationProcess.WaitForExit(); $mutationExit=$mutationProcess.ExitCode; $mutationProcess.Dispose()
  if($mutationExit -eq 0){throw 'Missing fallback was not detected.'}
  'PASS disabled file fallback mutation rejected'
 }
}finally{
 $resolved=[IO.Path]::GetFullPath($temp)
 if([IO.Path]::GetDirectoryName($resolved) -ne $tempRoot -or [IO.Path]::GetFileName($resolved) -notmatch '^NexusProtected-[0-9a-f]{32}$' -or ((Get-Item -LiteralPath $resolved).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Unexpected test cleanup path.'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
exit 0
