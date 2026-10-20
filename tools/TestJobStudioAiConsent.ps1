# Checks generator selection without sending any HTTP requests or using real secrets.
# Run after BuildFromSource.ps1, from any working directory.
$ErrorActionPreference = 'Stop'
$assemblyPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'NexusFreelance.Web\bin\NexusFreelance.Web.dll'
if (!(Test-Path $assemblyPath)) { throw 'Build the web project before checking AI consent.' }

$keys = @('NEXUS_AI_PROVIDER', 'NEXUS_AI_ENABLED', 'NEXUS_OPENAI_API_KEY', 'NEXUS_OPENAI_MODEL')
$previous = @{}
foreach ($key in $keys) { $previous[$key] = [Environment]::GetEnvironmentVariable($key, 'Process') }
try {
    [void][Reflection.Assembly]::LoadFrom($assemblyPath)
    $factory = [NexusFreelance.Web.Services.JobDraftGeneratorFactory]
    $env:NEXUS_AI_PROVIDER = 'OpenAI'
    $env:NEXUS_AI_ENABLED = 'true'
    $env:NEXUS_OPENAI_API_KEY = 'test-only-placeholder'
    $env:NEXUS_OPENAI_MODEL = 'test-model'

    $withoutConsent = [NexusFreelance.Web.Services.JobDraftGeneratorFactory]::Create($false)
    $withConsent = [NexusFreelance.Web.Services.JobDraftGeneratorFactory]::Create($true)
    $env:NEXUS_AI_ENABLED = 'false'
    $serverDisabled = [NexusFreelance.Web.Services.JobDraftGeneratorFactory]::Create($true)

    if ($withoutConsent.GetType().Name -ne 'RuleBasedJobDraftGenerator' -or
        $withConsent.GetType().Name -ne 'OpenAiJobDraftGenerator' -or
        $serverDisabled.GetType().Name -ne 'RuleBasedJobDraftGenerator') {
        throw 'Job Studio consent protection is not functioning correctly.'
    }
    Write-Host 'Job Studio AI consent branch checks passed (3/3, no network).'
}
finally {
    foreach ($key in $keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process') }
}
