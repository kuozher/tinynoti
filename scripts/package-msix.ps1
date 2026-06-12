param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$CertSubject = "CN=TinyNoti",
    [switch]$RegisterLoose,
    [switch]$InstallCertificate,
    [switch]$InstallPackage
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$appProject = Join-Path $root "TinyNoti.App\TinyNoti.App.csproj"
$manifest = Join-Path $root "TinyNoti.App\Package.appxmanifest"
$outDir = Join-Path $root "artifacts\msix"
$publishDir = Join-Path $outDir "publish"
$stageDir = Join-Path $outDir "stage"
$certDir = Join-Path $outDir "cert"
$manifestXml = [xml](Get-Content -Path $manifest)
$packageVersion = $manifestXml.Package.Identity.Version
$packagePath = Join-Path $outDir "TinyNoti_${packageVersion}_$Runtime.msix"
$cerPath = Join-Path $certDir "TinyNoti.cer"

function Find-WindowsKitTool([string]$toolName) {
    $kitRoot = "C:\Program Files (x86)\Windows Kits\10\bin"
    $tool = Get-ChildItem $kitRoot -Recurse -Filter $toolName -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like "*\x64\$toolName" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1

    if (-not $tool) {
        throw "$toolName was not found under $kitRoot. Install the Windows 10/11 SDK."
    }

    return $tool.FullName
}

function Reset-Directory([string]$path) {
    $resolvedRoot = (Resolve-Path $root).Path
    $resolved = Resolve-Path $path -ErrorAction SilentlyContinue
    if ($resolved -and -not $resolved.Path.StartsWith($resolvedRoot)) {
        throw "Refusing to remove path outside workspace: $($resolved.Path)"
    }

    if ($resolved) {
        Remove-Item -LiteralPath $resolved.Path -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $path | Out-Null
}

function Invoke-Native([string]$filePath, [string[]]$arguments) {
    & $filePath @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$filePath failed with exit code $LASTEXITCODE"
    }
}

$makeAppx = Find-WindowsKitTool "makeappx.exe"
$signTool = Find-WindowsKitTool "signtool.exe"

New-Item -ItemType Directory -Force -Path $outDir, $certDir | Out-Null
Reset-Directory $publishDir
Reset-Directory $stageDir

dotnet publish $appProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained false `
    -o $publishDir

Copy-Item -Path (Join-Path $publishDir "*") -Destination $stageDir -Recurse -Force
Copy-Item -Path $manifest -Destination (Join-Path $stageDir "AppxManifest.xml") -Force
$stageAssetsDir = Join-Path $stageDir "Assets"
if (Test-Path $stageAssetsDir) {
    Remove-Item -LiteralPath $stageAssetsDir -Recurse -Force
}

Copy-Item -Path (Join-Path $root "TinyNoti.App\Assets") -Destination $stageAssetsDir -Recurse -Force

$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $CertSubject -and $_.HasPrivateKey } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if (-not $cert) {
    $cert = New-SelfSignedCertificate `
        -Type Custom `
        -Subject $CertSubject `
        -KeyUsage DigitalSignature `
        -FriendlyName "TinyNoti MSIX Test Certificate" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3")
}

Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null

if (Test-Path $packagePath) {
    Remove-Item -LiteralPath $packagePath -Force
}

Invoke-Native $makeAppx @("pack", "/d", $stageDir, "/p", $packagePath, "/overwrite")
Invoke-Native $signTool @("sign", "/fd", "SHA256", "/sha1", $cert.Thumbprint, $packagePath)

if ($InstallCertificate) {
    Import-Certificate -FilePath $cerPath -CertStoreLocation Cert:\CurrentUser\TrustedPeople | Out-Null
    Import-Certificate -FilePath $cerPath -CertStoreLocation Cert:\CurrentUser\Root | Out-Null
}

if ($InstallPackage) {
    Add-AppxPackage -Path $packagePath -ForceApplicationShutdown
}

if ($RegisterLoose) {
    Add-AppxPackage -Register (Join-Path $stageDir "AppxManifest.xml") -ForceApplicationShutdown
}

[PSCustomObject]@{
    Package = $packagePath
    Certificate = $cerPath
    Thumbprint = $cert.Thumbprint
    RegisteredLoosePackage = [bool]$RegisterLoose
    InstalledCertificate = [bool]$InstallCertificate
    InstalledPackage = [bool]$InstallPackage
}
