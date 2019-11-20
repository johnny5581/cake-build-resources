# ================================================
# Script : Init-Cake.ps1
# Author : NagiLin <nagilin@cgmh.org.tw>
# Date   : 2019-11-20
# Desc   : init cake build.ps1
# ================================================

Param (
    [string]$Path = '', 
    [switch]$Force = $False,
    [string]$Resource = 'default',
    [switch]$CakeScript
)

If($Path -eq '' -and $Force -eq $False) {
    $Path = $(Read-Host 'Project Path')
    If($Path -eq '') {
        throw 'missing project path'
    }
}
$Path = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)


$Resource = Switch($Resource.ToLower()) {
    'default' {         
        'https://raw.githubusercontent.com/johnny5581/cake-build-resources/master/build.ps1'; 
        break 
    }
    'master' { 'https://raw.githubusercontent.com/cake-build/resources/master/build.ps1'; break }
    'official' { 'https://cakebuild.net/download/bootstrapper/windows'; break }
    Default { $Resource ; break}
}
$outputFile = "$Path\build.ps1"
Write-Host "Download build.ps1 from '$Resource' to '$outputFile'"
Invoke-WebRequest $Resource -OutFile "$outputFile"

If($CakeScript) {
    $cakeScriptUrl = 'https://raw.githubusercontent.com/johnny5581/cake-build-resources/master/build.cake'
    $outputFile = "$Path\build.cake"
    Write-Host "Download build.cake from '$cakeScriptUrl' to '$outputFile'"
    Invoke-WebRequest $cakeScriptUrl -OutFile "$outputFile"
}
