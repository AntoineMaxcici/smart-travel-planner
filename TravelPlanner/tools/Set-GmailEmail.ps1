[CmdletBinding()]
param([string]$SenderAddress)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($SenderAddress)) {
    $SenderAddress = Read-Host 'Gmail address TravelPlanner should send from'
}
$SenderAddress = $SenderAddress.Trim()
$mailAddress = [System.Net.Mail.MailAddress]::new($SenderAddress)
if ($mailAddress.Address -ne $SenderAddress) { throw 'Enter the sender email address without a display name.' }
$apiProject = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\TravelPlanner.Api\TravelPlanner.Api.csproj')).Path
$securePassword = Read-Host 'Paste the Google app password for this sender (hidden)' -AsSecureString
$passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
try {
    $passwordValue = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer).Replace(' ', '').Trim()
    if ($passwordValue.Length -ne 16) { throw 'A Google app password contains 16 characters. Enter the app password created for TravelPlanner.' }
    $smtpSecrets = @{
        'Email:Smtp:Host' = 'smtp.gmail.com'
        'Email:Smtp:Port' = '587'
        'Email:Smtp:EnableSsl' = 'true'
        'Email:Smtp:Username' = $SenderAddress
        'Email:Smtp:FromAddress' = $SenderAddress
        'Email:Smtp:Password' = $passwordValue
    }
    $smtpSecrets | ConvertTo-Json -Compress | dotnet user-secrets set --project $apiProject
    if ($LASTEXITCODE -ne 0) { throw 'Could not save the Gmail settings.' }
    Write-Output 'Gmail settings saved to API user-secrets. Restart the API to activate email delivery.'
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
    $passwordValue = $null
    if ($null -ne $smtpSecrets) { $smtpSecrets.Clear() }
    $securePassword.Dispose()
}
