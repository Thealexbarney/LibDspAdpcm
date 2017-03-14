﻿properties {
    $baseDir = Resolve-Path ..
    $buildDir = "$baseDir\build"
    $sourceDir = "$baseDir\src"
    $toolsDir = "$baseDir\tools"
    $publishDir = "$baseDir\publish"

    $libraryDir = "$sourceDir\VGAudio"
    $cliDir = "$sourceDir\VGAudio.Cli"
    $uwpDir = "$sourceDir\VGAudio.Uwp"
    $testsDir = "$sourceDir\VGAudio.Tests"

    $libraryPublishDir = "$publishDir\NuGet"
    $cliPublishDir = "$publishDir\cli"
    $uwpPublishDir = "$publishDir\uwp"

    $storeCertThumbprint = "EB553661E803DE06AA93B72183F93CA767804F1E"
    $releaseCertThumbprint = "2043012AE523F7FA0F77A537387633BEB7A9F4DD"

    $dotnetToolsDir = Join-Path $toolsDir dotnet
    $dotnetSdkDir = Join-Path $dotnetToolsDir sdk
    $dotnetCliVersion = "1.0.0-rc4-004913"

    $libraryBuilds = @(
        @{ Name = "netstandard1.1"; LibSuccess = $null; CliFramework = "netcoreapp1.0"; CliSuccess = $null; TestFramework = "netcoreapp1.0"; TestSuccess = $null },
        @{ Name = "netstandard1.0"; LibSuccess = $null },
        @{ Name = "net45"; LibSuccess = $null; CliFramework = "net45"; CliSuccess = $null; TestFramework = "net46"; TestSuccess = $null },
        @{ Name = "net40"; LibSuccess = $null; CliFramework = "net40"; CliSuccess = $null },
        @{ Name = "net35"; LibSuccess = $null; CliFramework = "net35"; CliSuccess = $null },
        @{ Name = "net20"; LibSuccess = $null; CliFramework = "net20"; CliSuccess = $null }
    )

    $otherBuilds = @{
        "Uwp" = @{ Name = "UWP App"; Success = $null }
    }

    $signReleaseBuild = $false
}

framework '4.6'

task default -depends RebuildAll

task Clean -depends CleanBuild, CleanPublish

task CleanBuild {
    $toDelete = "bin", "obj", "*.lock.json", "*.nuget.targets", "AppPackages", "BundleArtifacts", "*.appx", "_pkginfo.txt"

    Get-ChildItem $sourceDir | ? { $_.PSIsContainer } |
    ForEach-Object {
        foreach ($file in $toDelete)
        {
            $path = $_.FullName + "\" + $file
            RemovePath -Path $path -Verbose
        }
    }
}

task CleanPublish {
    RemovePath -Path $publishDir -Verbose
}

task BuildLib { BuildLib }
task BuildCli -depends BuildLib { BuildCli }
task BuildUwp { BuildUwp }

task PublishLib -depends BuildLib { PublishLib }
task PublishCli -depends BuildCli { PublishCli }
task PublishUwp -depends BuildUwp { PublishUwp }

task TestLib -depends BuildLib { TestLib }

task RebuildAll -depends Clean, PublishLib, PublishCli, PublishUwp, TestLib { WriteReport }
task RebuildNonUwp -depends Clean, PublishLib, PublishCli, TestLib { WriteReport }
task BuildAll -depends CleanPublish, PublishLib, PublishCli, PublishUwp { WriteReport }

task Appveyor -depends RebuildAll { VerifyBuildSuccess }

function BuildLib() {
    SetupDotnetCli
    
    NetCliRestore -Path $libraryDir

    foreach ($build in $libraryBuilds)
    {
        Write-Host -ForegroundColor Green Building $libraryDir $build.Name
        try {
            NetCliBuild $libraryDir $build.Name
        }
        catch [Exception] {
            PrintException -Ex $_.Exception
            $build.LibSuccess = $false
            continue
        }
        $build.LibSuccess = $true
    }
}

function BuildCli() {
    SetupDotnetCli

    NetCliRestore -Path $cliDir
    foreach ($build in $libraryBuilds | Where { $_.LibSuccess -ne $false -and $_.CliFramework })
    {
        Write-Host -ForegroundColor Green Building $cliDir $build.CliFramework
        try {
            NetCliBuild $cliDir $build.CliFramework
        }
        catch [Exception] {
            PrintException -Ex $_.Exception
            $build.CliSuccess = $false
            continue
        }
        $build.CliSuccess = $true
    }
}

function BuildUwp() {
    if (-not (Test-Path "HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots")) {
        Write-Host "Windows 10 SDK not detected. Skipping UWP build."
        return
    }

    SetupDotnetCli

    try {
        $thumbprint = SetupUwpSigningCertificate
        if ($thumbprint) {
            $thumbprint = "/p:PackageCertificateThumbprint=" + $thumbprint
        }

        NetCliRestore -Path $libraryDir
        MsbuildRestore -Path $uwpDir

        $csproj = "$uwpDir\VGAudio.Uwp.csproj"
        exec { msbuild $csproj /p:AppxBundle=Always`;AppxBundlePlatforms=x86`|x64`|ARM`;UapAppxPackageBuildMode=StoreUpload`;Configuration=Release /v:m $thumbprint }
    }
    catch [Exception] {
        PrintException -Ex $_.Exception
        $otherBuilds.Uwp.Success = $false
        return
    }
    $otherBuilds.Uwp.Success = $true
}

function PublishLib() {
    SignLib
    $frameworks = ($libraryBuilds | Where { $_.LibSuccess -ne $false } | ForEach-Object { $_.Name }) -join ';'
    dotnet pack --no-build $libraryDir -c release -o "$publishDir\NuGet" /p:TargetFrameworks=`\`"$frameworks`\`"
}

function PublishCli() {
    SignLib
    SignCli

    foreach ($build in $libraryBuilds | Where { $_.CliSuccess -ne $false -and $_.LibSuccess -ne $false -and $_.CliFramework })
    {
        $framework = $build.CliFramework
        Write-Host -ForegroundColor Green "Publishing CLI project $framework"
        NetCliPublish $cliDir "$publishDir\cli\$framework" $framework
    }
}

function PublishUwp() {
    if (-not (Test-Path "HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots")) {
        return
    }

    if ($otherBuilds.Uwp.Success -eq $false) {
        Write-Host -ForegroundColor Red "UWP project was not successfully built. Skipping..."
        return
    }

    $version = GetVersionFromAppxManifest $uwpDir\Package.appxmanifest
    $buildDir = Join-Path $uwpDir AppPackages
    $appxName = "VGAudio.Uwp_$version`_x86_x64_ARM"
    $bundleDir = Join-Path $buildDir "VGAudio.Uwp_$version`_Test"

    $appxUpload = Join-Path $buildDir "$appxName`_bundle.appxupload"
    $appxBundle = Join-Path $bundleDir "$appxName`.appxbundle"
    $appxCer = Join-Path $bundleDir "$appxName`.cer"

    CopyItemToDirectory -Path $appxUpload,$appxBundle -Destination $uwpPublishdir

    if (($signReleaseBuild -eq $true) -and (Get-ChildItem -Path cert: -Recurse -CodeSigningCert | Where { $_.Thumbprint -eq $releaseCertThumbprint }).Count -gt 0)
    {
        $publisher = (Get-ChildItem -Path cert: -Recurse -CodeSigningCert | Where { $_.Thumbprint -eq $releaseCertThumbprint}).Subject

        $appxBundlePublish = Join-Path $uwpPublishdir "$appxName`.appxbundle"

        ChangeAppxBundlePublisher -Path $appxBundlePublish -Publisher $publisher
        SignExecutable -Path $appxBundlePublish -Thumbprint $releaseCertThumbprint
    } else {
        CopyItemToDirectory -Path $appxCer -Destination $uwpPublishdir
    }
}

function TestLib() {
    NetCliRestore -Path $testsDir
    foreach ($build in $libraryBuilds | Where { $_.LibSuccess -ne $false -and $_.TestFramework })
    {
        try {
            $path = "$sourceDir\" + $build.TestDir
            exec { dotnet test $testsDir -c release -f $build.TestFramework }
        }
        catch [Exception] {
            PrintException -Ex $_.Exception
            $build.TestSuccess = $false
            continue
        }
        $build.TestSuccess = $true
    }
}

function SignLib()
{
    if (($signReleaseBuild -eq $true) -and (CertificateExists -Thumbprint $releaseCertThumbprint))
    {
        $projectName = [System.IO.Path]::GetFileName($libraryDir)
        foreach ($build in $libraryBuilds | Where { $_.LibSuccess -ne $false })
        {
            $framework = $build.Name
            $signPath = Join-Path $libraryDir "bin\Release\$framework\$projectName.dll"
            SignExecutablePS -Path $signPath -Thumbprint $releaseCertThumbprint
        }
    }
}

function SignCli()
{
     if (($signReleaseBuild -eq $true) -and (CertificateExists -Thumbprint $releaseCertThumbprint))
    {
        $rid = "win7-x64"
        $projectName = [System.IO.Path]::GetFileName($cliDir)

        foreach ($build in $libraryBuilds | Where { $_.CliSuccess -ne $false -and $_.CliFramework })
        {
            $framework = $build.CliFramework
            $paths =
            (Join-Path $cliDir "bin\Release\$framework\$projectName.dll"),
            (Join-Path $cliDir "bin\Release\$framework\$rid\$projectName.dll"),
            (Join-Path $cliDir "bin\Release\$framework\$rid\$projectName.exe")

            foreach ($path in $paths | Where { Test-Path $_ })
            {
                SignExecutablePS -Path $path -Thumbprint $releaseCertThumbprint
            }
        }
    }
}

function SetupDotnetCli()
{
    CreateBuildGlobalJson -Path (Join-Path $buildDir global.json) -Version $dotnetCliVersion
    if ((Get-Command "dotnet" -errorAction SilentlyContinue) -and (& dotnet --version) -eq $dotnetCliVersion)
    {
        return
    }

    Write-Host "Searching for Dotnet CLI version $dotnetCliVersion..."    
    Write-Host "Checking Local AppData default install path..."
    $appDataInstallPath = Join-Path $env:LocalAppData "Microsoft\dotnet"
    $path = Join-Path $appDataInstallPath dotnet.exe
    if ((Test-Path $path) -and (& $path --version) -eq $dotnetCliVersion)
    {
        $env:Path = "$appDataInstallPath;" + $env:path
        Write-Host "Found Dotnet CLI version $dotnetCliVersion"        
        return
    }
    
    Write-Host "Checking project tools path..."
    $path = Join-Path $dotnetSdkDir dotnet.exe
    if ((Test-Path $path) -and (& $path --version) -eq $dotnetCliVersion)
    {
        $env:Path = "$dotnetSdkDir;" + $env:path
        Write-Host "Found Dotnet CLI version $dotnetCliVersion"        
        return
    }
    
    Write-Host "Downloading Dotnet CLI..."
    try {
        & (Join-Path $dotnetToolsDir dotnet-install.ps1) -InstallDir $dotnetSdkDir -Version $dotnetCliVersion -NoPath
    } catch { }
    if ((Test-Path $path) -and (& $path --version) -eq $dotnetCliVersion)
    {
        $env:Path = "$dotnetSdkDir;" + $env:path
        Write-Host "Found Dotnet CLI version $dotnetCliVersion"        
        return
    }

    Write-Host -ForegroundColor Red "Unable to find Dotnet CLI version $dotnetCliVersion"
    exit 1
}

function CreateBuildGlobalJson([string] $Path, [string]$Version)
{
    $json = "{`"projects`":[],`"sdk`":{`"version`":`"$Version`"}}"
    Out-File -FilePath $Path -Encoding utf8 -InputObject $json
}

function NetCliBuild([string]$path, [string]$framework)
{
    exec { dotnet build $path -f $framework -c Release }
}

function NetCliPublish([string]$srcPath, [string]$outPath, [string]$framework)
{
    exec { dotnet publish $srcPath -f $framework -c Release -o $outPath }
}

function NetCliRestore([string[]]$Path)
{
    foreach ($singlePath in $Path)
    {
        Write-Host -ForegroundColor Green "Restoring $singlePath"
        exec { dotnet restore $singlePath | Out-Default }
    }
}

function MsbuildRestore([string[]]$Path)
{
    foreach ($singlePath in $Path)
    {
        Write-Host -ForegroundColor Green "Restoring $singlePath"
        exec { msbuild /t:restore $singlePath | Out-Default }
    }
}

function GetVersionFromAppxManifest([string]$manifestPath)
{
    [xml]$manifestXml = Get-Content -Path $manifestPath
    $manifestXml.Package.Identity.Version
}

function SetupUwpSigningCertificate()
{
    $csprojPath = "$uwpDir\VGAudio.Uwp.csproj"
    [xml]$csprojXml = Get-Content -Path $csprojPath
    $thumbprint = $csprojXml.Project.PropertyGroup[0].PackageCertificateThumbprint
    $keyFile = "$uwpDir\" + $csprojXml.Project.PropertyGroup[0].PackageCertificateKeyFile

    if ((Get-ChildItem -Path cert: -Recurse -CodeSigningCert | Where { $_.Thumbprint -eq $storeCertThumbprint }).Count -gt 0)
    {
        Write-Host "Using store code signing certificate with thumbprint $storeCertThumbprint in certificate store."
        return $storeCertThumbprint
    }

    if ((Get-ChildItem -Path cert: -Recurse -CodeSigningCert | Where { $_.Thumbprint -eq $releaseCertThumbprint }).Count -gt 0)
    {
        Write-Host "Using release code signing certificate with thumbprint $releaseCertThumbprint in certificate store."
        return $releaseCertThumbprint
    }

    if (Test-Path $keyFile)
    {
        Write-Host "Using code signing certificate at $keyFile"
        return
    }

    CreateSelfSignedCertificate -Path $keyFile

    Write-Host "Created self-signed test certificate at $keyFile"
}

function CreateSelfSignedCertificate([string]$Path)
{
    $subject = "CN=$env:username"
    try {
        $cert = New-SelfSignedCertificate -Subject $subject -Type CodeSigningCert -TextExtension @("2.5.29.19={text}") -CertStoreLocation cert:\currentuser\my
    }
    catch {
        $date = Get-Date (Get-Date).AddYears(1) -format MM/dd/yyyy
        exec { MakeCert /n $subject /r /pe /h 0 /eku 1.3.6.1.5.5.7.3.3,1.3.6.1.4.1.311.10.3.13 /e $date /ss My }
        $cert = Get-ChildItem -Path cert: -Recurse -CodeSigningCert | Where { $_.Subject -eq $subject }
    }
    
    Remove-Item $cert.PSPath
    Export-PfxCertificate -Cert $cert -FilePath $keyFile -Password (New-Object System.Security.SecureString) | Out-Null    
}

function ChangeAppxBundlePublisher([string]$Path, [string]$Publisher)
{
    Write-Host Changing publisher of $Path
    $dirBundle = Join-Path ([System.IO.Path]::GetDirectoryName($Path)) ([System.IO.Path]::GetFileNameWithoutExtension($Path))

    RemovePath $dirBundle
    exec { makeappx unbundle /p $Path /d $dirBundle | Out-Null }

    Get-ChildItem $dirBundle -Filter *.appx | ForEach-Object { ChangeAppxPublisher -Path $_.FullName -Publisher $Publisher }    

    $manifestPath = Join-Path $dirBundle "AppxMetadata\AppxBundleManifest.xml"
    [xml]$manifestXml = Get-Content -Path $manifestPath
        
    $manifestXml.Bundle.Identity.Publisher = $Publisher
    $manifestXml.Save($manifestPath)

    RemovePath $Path
    exec { makeappx bundle /d $dirBundle /p $Path | Out-Null }

    RemovePath $dirBundle
}

function ChangeAppxPublisher([string]$Path, [string]$Publisher)
{
    Write-Host Changing publisher of $Path
    $dirAppx = Join-Path ([System.IO.Path]::GetDirectoryName($Path)) ([System.IO.Path]::GetFileNameWithoutExtension($Path))

    RemovePath $dirAppx
    exec { makeappx unpack /l /p $Path /d $dirAppx | Out-Null }      

    $manifestPath = Join-Path $dirAppx AppxManifest.xml
    [xml]$manifestXml = Get-Content -Path $manifestPath
        
    $manifestXml.Package.Identity.Publisher = $Publisher
    $manifestXml.Save($manifestPath)

    RemovePath $Path
    exec { makeappx pack /l /d $dirAppx /p $Path | Out-Null }    

    RemovePath $dirAppx
}

function SignExecutable([string]$Path, [string]$Thumbprint)
{
    $timestampServers =
    "http://sha256timestamp.ws.symantec.com/sha256/timestamp",
    "http://timestamp.globalsign.com/?signature=sha2",
    "http://time.certum.pl"

    foreach($server in $timestampServers)
    {
        for($i = 1; $i -le 4; $i++)
        {
            Write-Host "Signing $Path"
            $global:lastexitcode = 0
            signtool sign /fd SHA256 /a /sha1 $Thumbprint /tr $server $Path
            if ($lastexitcode -eq 0)
            {
                return
            }
            Write-Host -ForegroundColor Red "Failed. Retrying..."
            Start-Sleep -Seconds 3
        }
    }
}

function SignExecutablePS([string]$Path, [string]$Thumbprint)
{
    $timestampServers =
    "http://timestamp.globalsign.com/?signature=sha2",
    "http://time.certum.pl"

    $signature = Get-AuthenticodeSignature -FilePath $Path
    if (($signature.SignerCertificate.Thumbprint -eq $Thumbprint) -and ($signature.Status -eq "Valid"))
    {
        return
    }

    if ((CertificateExists -Thumbprint $Thumbprint) -eq $false)
    {
        throw ("SignExecutablePS: Could not find code signing certificate with thumbprint $Thumbprint")
    }

    $cert = Get-ChildItem -Path cert: -Recurse -CodeSigningCert | Where { $_.Thumbprint -eq $Thumbprint }

    foreach($server in $timestampServers)
    {
        for($i = 1; $i -le 4; $i++)
        {
            Write-Host "Signing $Path"

            $result = Set-AuthenticodeSignature -Certificate $cert -TimestampServer $server -HashAlgorithm SHA256 -FilePath $Path
            if ($result.Status -eq "Valid")
            {
                return
            }
            Write-Host -ForegroundColor Red "Failed. Retrying..."
            Start-Sleep -Seconds 3
        }
    }
}

function RemovePath([string]$Path, [switch]$Verbose)
{
    if (Test-Path $Path)
    {
        if ($Verbose) {
            Write-Host Cleaning $Path
        }
        Remove-Item $Path -Recurse -Force
    }
}

function CopyItemToDirectory([string[]]$Path, [string]$Destination)
{
    if (-not (Test-Path $Destination))
    {
        mkdir -Path $Destination | Out-Null
    }
    Copy-Item -Path $Path -Destination $Destination
}

function CertificateExists([string]$Thumbprint)
{
    if ((Get-ChildItem -Path cert: -Recurse -CodeSigningCert | Where { $_.Thumbprint -eq $Thumbprint }).Count -gt 0)
    {
        return $true
    }
    return $false
}

function PrintException([Exception]$Ex)
{
    Write-Output "Exception thrown: " + $Ex.Message + "`n"
}

function WriteReport()
{
        Write-Host $("-" * 70)
    Write-Host "Build Report"
    Write-Host $("-" * 70)

    Write-Host `n
    Write-Host "Library Builds"
    Write-Host $("-" * 35)
    $list = @()

    foreach ($build in $libraryBuilds) {
        $status = ""
        switch ($build.LibSuccess)
        {
            $true { $status = "Success" }
            $false { $status = "Failure" }
            $null { $status = "Not Built" }
        }

        $list += new-object PSObject -property @{
            Name = $build.Name;
            Status = $status
        }
    }
    $list | format-table -autoSize -property Name,Status | out-string -stream | where-object { $_ }
    
    Write-Host `n
    Write-Host "CLI Builds"
    Write-Host $("-" * 35)
    $list = @()

    foreach ($build in $libraryBuilds | Where { $_.CliFramework }) {
        $status = ""
        switch ($build.CliSuccess)
        {
            $true { $status = "Success" }
            $false { $status = "Failure" }
            $null { $status = "Not Built" }
        }

        $list += new-object PSObject -property @{
            Name = $build.CliFramework;
            Status = $status
        }
    }
    $list | format-table -autoSize -property Name,Status | out-string -stream | where-object { $_ }

    Write-Host `n
    Write-Host "Other Builds"
    Write-Host $("-" * 35)
    $list = @()

    foreach ($build in $otherBuilds.Values) {
        $status = ""
        switch ($build.Success)
        {
            $true { $status = "Success" }
            $false { $status = "Failure" }
            $null { $status = "Not Built" }
        }

        $list += new-object PSObject -property @{
            Name = $build.Name;
            Status = $status
        }
    }
    $list | format-table -autoSize -property Name,Status | out-string -stream | where-object { $_ }

    Write-Host `n
    Write-Host "Tests"
    Write-Host $("-" * 35)
    $list = @()

    foreach ($build in $libraryBuilds | Where { $_.TestFramework }) {
        $status = ""
        switch ($build.TestSuccess)
        {
            $true { $status = "Success" }
            $false { $status = "Failure" }
            $null { $status = "Not Tested" }
        }

        $list += new-object PSObject -property @{
            Name = $build.CliFramework;
            Status = $status
        }
    }
    $list | format-table -autoSize -property Name,Status | out-string -stream | where-object { $_ }
}

function VerifyBuildSuccess()
{
    foreach ($build in $libraryBuilds) {
        if ($build.LibSuccess -ne $true)
        {
            throw "Library build failed"
        }
    }

    foreach ($build in $libraryBuilds | Where { $_.CliFramework }) {
        if ($build.CliSuccess -ne $true)
        {
            throw "CLI build failed"
        }
    }

    foreach ($build in $otherBuilds.Values) {
        if ($build.Success -ne $true)
        {
            throw $build.Name + " build failed"
        }
    }

    foreach ($build in $libraryBuilds | Where { $_.TestFramework }) {
        if ($build.TestSuccess -ne $true)
        {
            throw "Tests failed"
        }
    }
}
