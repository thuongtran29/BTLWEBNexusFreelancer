# Checks exact configured provider credentials without printing their values.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Distribution)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'IntegrationStore.ps1')
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root=(Resolve-Path -LiteralPath $Distribution).ProviderPath
if(!(Test-Path -LiteralPath (Join-Path $root '.nexus-share.json')) -or !(Test-Path -LiteralPath (Join-Path $root 'NexusFreelance.sln'))){throw 'Not a generated NexusFreelance source distribution.'}
$settings=Read-NexusIntegrationStore (Get-NexusIntegrationStorePath (Split-Path -Parent $PSScriptRoot))
$sensitive=@{}
foreach($name in @('NEXUS_SMTP_PASSWORD','NEXUS_GOOGLE_CLIENT_SECRET','NEXUS_GEMINI_API_KEY','NEXUS_OPENAI_API_KEY','NEXUS_VNPAY_HASH_SECRET')){
 $value=[Environment]::GetEnvironmentVariable($name);if(!$value){$value=$settings[$name]}
 if($value -and $value.Length -ge 8){$sensitive[$name]=$value}
}
$findings=New-Object 'Collections.Generic.List[object]'
$count=0
function Check-Bytes([byte[]]$Bytes,[string]$Name){
 $text=[Text.Encoding]::UTF8.GetString($Bytes)
 foreach($key in $sensitive.Keys){if($text.Contains($sensitive[$key])){$findings.Add([pscustomobject]@{File=$Name;Category=$key})}}
}
try{
 foreach($file in Get-ChildItem -LiteralPath $root -Recurse -File -Force){
  if($file.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Distribution includes a filesystem link.'}
  $relative=$file.FullName.Substring($root.Length+1)
  if($relative -match '(^|[\\/])(_local|App_Data|\.vs|bin|obj|_runtime|_offline)([\\/]|$)' -or $file.Extension -in @('.dat','.eml','.bak','.bkp','.pfx','.user','.suo','.log','.tmp') -or $file.Name -like '*client_secret*'){
   $findings.Add([pscustomobject]@{File=$relative;Category='private-or-generated-path'})
  }
  $bytes=[IO.File]::ReadAllBytes($file.FullName);Check-Bytes $bytes $relative;$count++
  if($file.Extension -in @('.docx','.xlsx','.pptx')){
   $zip=[IO.Compression.ZipFile]::OpenRead($file.FullName)
   try{foreach($entry in $zip.Entries | Where-Object {$_.FullName -like '*.xml' -or $_.FullName -like '*.rels'}){
    if($entry.Length -gt 10000000){throw 'Oversize Office XML in distribution.'}
    $stream=$entry.Open();$memory=New-Object IO.MemoryStream
    try{$stream.CopyTo($memory);Check-Bytes $memory.ToArray() ($relative+'::'+$entry.FullName)}finally{$stream.Dispose();$memory.Dispose()}
   }}finally{$zip.Dispose()}
  }
 }
 if($findings.Count){$findings.ToArray()|ConvertTo-Json -Depth 3;throw 'Distribution contains private/generated data; do not share.'}
 [pscustomobject]@{FilesScanned=$count;ConfiguredCredentialTypesChecked=$sensitive.Count;ExactCredentialMatches=0;PrivateOrGeneratedPaths=0;OfficeXmlInspected=$true}|ConvertTo-Json -Compress
}finally{$settings.Clear();$sensitive.Clear();$bytes=$null;$value=$null}
