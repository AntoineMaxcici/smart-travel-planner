$ErrorActionPreference = 'Stop'
$apiProject = Join-Path $PSScriptRoot '..\TravelPlanner.Api\TravelPlanner.Api.csproj'
$apiProject = (Resolve-Path -LiteralPath $apiProject).Path
$secureKey = Read-Host 'Paste your Google AI Studio Gemini API key (hidden)' -AsSecureString
$keyPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureKey)
try {
    $keyValue = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($keyPointer).Trim()
    if ([string]::IsNullOrWhiteSpace($keyValue)) { throw 'No API key was entered.' }
    # Passing JSON over stdin keeps the key out of the command line and shell history.
    @{ 'Gemini:ApiKey' = $keyValue } | ConvertTo-Json -Compress | dotnet user-secrets set --project $apiProject
    if ($LASTEXITCODE -ne 0) { throw 'Could not save the Gemini API key.' }
    Write-Output 'Gemini key saved to the API user-secrets store.'
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($keyPointer)
    $keyValue = $null
    $secureKey.Dispose()
}
