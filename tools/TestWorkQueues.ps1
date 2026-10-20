param([switch]$MutationCheck)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$bin=Join-Path $root 'NexusFreelance.Web\bin'
$compiler=Join-Path $env:windir 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$output=Join-Path $bin 'TestWorkQueues.exe'
$references=@("/reference:$(Join-Path $bin 'NexusFreelance.Web.dll')","/reference:$(Join-Path $bin 'EntityFramework.dll')")
& $compiler /nologo /target:exe "/out:$output" @references (Join-Path $PSScriptRoot 'TestWorkQueues.cs')
if($LASTEXITCODE -ne 0){throw 'Work queue harness compilation failed'}
& $output
if($LASTEXITCODE -ne 0){throw 'Work queue tests failed'}
if($MutationCheck){
    $mutationFile=[IO.Path]::GetTempFileName()
    $mutationExe=Join-Path $bin 'TestWorkQueuesMutation.exe'
    try {
        $source=Get-Content -LiteralPath (Join-Path $root 'NexusFreelance.Web\Services\ContractInbox.cs') -Raw
        $mutated=$source.Replace('c.Status == "Active" && db.EscrowMilestones','c.Status != "Active" && db.EscrowMilestones')
        if($mutated -eq $source){throw 'Mutation did not change review condition'}
        [IO.File]::WriteAllText($mutationFile,$mutated)
        & $compiler /nologo /target:exe "/out:$mutationExe" @references $mutationFile (Join-Path $PSScriptRoot 'TestWorkQueues.cs') *> $null
        if($LASTEXITCODE -ne 0){throw 'Mutation compilation failed'}
        & $mutationExe *> $null
        if($LASTEXITCODE -eq 0){throw 'Mutation survived; regression coverage missing'}
        Write-Output 'PASS review-status condition mutation rejected'
    } finally { Remove-Item -LiteralPath $mutationFile -Force; if(Test-Path -LiteralPath $mutationExe){Remove-Item -LiteralPath $mutationExe -Force} }
}
exit 0
