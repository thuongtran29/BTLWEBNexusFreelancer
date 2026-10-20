param([switch]$MutationCheck)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$scratch=Join-Path $PSScriptRoot ('_runtime/RecruitmentPolicy-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $scratch
$source=Join-Path $root 'NexusFreelance.Web/Services/RecruitmentPolicy.cs'
$test=Join-Path $PSScriptRoot 'TestRecruitmentPolicy.cs'
$exe=Join-Path $scratch 'RecruitmentPolicy.exe'
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vsRoot=& $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
if(!$vsRoot){throw 'Visual Studio MSBuild with Roslyn is required'}
$csc=Join-Path $vsRoot 'MSBuild/Current/Bin/Roslyn/csc.exe'
& $csc /nologo /target:exe /codepage:65001 "/out:$exe" $source $test
if($LASTEXITCODE -ne 0){throw 'Policy compilation failed'}
& $exe
if($LASTEXITCODE -ne 0){throw 'Policy behavior regression'}
if($MutationCheck){
 $original=[IO.File]::ReadAllText($source)
 $mutated=$original.Replace('.Date >= nowUtc.Date','.Date > nowUtc.Date')
 if($mutated -eq $original){throw 'Mutation target missing'}
 $copy=Join-Path $scratch 'MutatedPolicy.cs'
 [IO.File]::WriteAllText($copy,$mutated,[Text.Encoding]::UTF8)
 $mutant=Join-Path $scratch 'MutatedPolicy.exe'
 & $csc /nologo /target:exe /codepage:65001 "/out:$mutant" $copy $test
 if($LASTEXITCODE -ne 0){throw 'Mutation compilation failed'}
 $output=& $mutant
 if($LASTEXITCODE -eq 0){throw 'Missing coverage: off-by-one mutation survived'}
 Write-Host 'PASS Mutation rejected: excluding final calendar day is detected'
}

