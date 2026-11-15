$PSScriptRootParent = Split-Path -Parent $PSScriptRoot
& "$PSScriptRoot\build.ps1" -Target win
