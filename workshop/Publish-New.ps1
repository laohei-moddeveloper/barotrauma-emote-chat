#Requires -Version 7.2
param([Parameter(Mandatory=$true)][string]$GameDir)
$ErrorActionPreference='Stop'
if(-not(Test-Path -LiteralPath (Join-Path $GameDir 'Facepunch.Steamworks.Win64.dll'))){throw 'Select your installed Barotrauma game folder; Steam must be running and logged in'}
if(-not(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'publish-state.json'))){
    & (Join-Path $PSScriptRoot 'Publish-Workshop.ps1') -GameDir $GameDir -Action Create
}
& (Join-Path $PSScriptRoot 'Publish-Workshop.ps1') -GameDir $GameDir -Action Publish
& (Join-Path $PSScriptRoot 'Update-Localizations.ps1') -GameDir $GameDir
$state=Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'publish-state.json') | ConvertFrom-Json
Write-Output ('Published: https://steamcommunity.com/sharedfiles/filedetails/?id='+$state.file_id)
