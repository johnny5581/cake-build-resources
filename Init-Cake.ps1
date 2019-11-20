# ================================================
# Script : Init-Cake.ps1
# Author : NagiLin <nagilin@cgmh.org.tw>
# Date   : 2019-11-20
# Desc   : init cake build.ps1
# ================================================
[CmdletBinding()]
Param (
    [string]$Path = '', 
    [switch]$Force = $False,
    [string]$Resource = 'default',
    [switch]$Constants
)

Function Download-File {
    [CmdletBinding()]
    Param (
        [string]$Url,
        [string]$OutputFile
    )    
    Write-Host "Download build.ps1 from '$Url' to '$OutputFile'"
    Invoke-WebRequest $Url -OutFile "$OutputFile"
}

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
Download-File -Url "$Resource" -OutputFile "$Path\build.ps1"
Download-File -Url 'https://raw.githubusercontent.com/johnny5581/cake-build-resources/master/build.cake' -OutputFile "$Path\build.cake"
if($Constants) {
    Download-File -Url 'https://raw.githubusercontent.com/johnny5581/cake-build-resources/master/variables.cake' -OutputFile "$Path\variables.cake"
}

