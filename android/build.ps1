# SPDX-License-Identifier: MPL-2.0

param(
    [string]$AndroidSdk = $env:ANDROID_HOME,
    [string]$Jdk = $env:JAVA_HOME,
    [string]$Dotnet = "dotnet",
    [string]$CMake = "cmake",
    [string]$Ninja = "ninja",
    [ValidateSet("arm64-v8a", "x86_64")]
    [string]$Abi = "arm64-v8a"
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$work = Join-Path $root ".android-build"
$native = Join-Path $work "native"
$target = Join-Path $native $Abi
$package = Join-Path $work "apk-$Abi"
$output = Join-Path $root "outputs"
if (!$AndroidSdk -or !$Jdk) { throw "Set ANDROID_HOME and JAVA_HOME, or pass -AndroidSdk and -Jdk." }
$toolchain = Join-Path $AndroidSdk "ndk/28.2.13676358/build/cmake/android.toolchain.cmake"
if (!(Test-Path -LiteralPath $toolchain)) { throw "Android NDK 28.2.13676358 is required." }

New-Item -ItemType Directory -Force -Path $work, $output | Out-Null
& $CMake -S (Join-Path $root "native") -B $target -G Ninja `
    "-DCMAKE_MAKE_PROGRAM=$Ninja" "-DCMAKE_TOOLCHAIN_FILE=$toolchain" `
    "-DANDROID_ABI=$Abi" "-DANDROID_PLATFORM=android-26" "-DANDROID_STL=c++_static" `
    "-DCMAKE_BUILD_TYPE=Release" -DGDS_GBA=ON -DGDS_DS=ON
if ($LASTEXITCODE -ne 0) { throw "Native configuration failed." }
& $CMake --build $target --parallel 8
if ($LASTEXITCODE -ne 0) { throw "Native build failed." }

$key = Join-Path $work "debug.keystore"
if (!(Test-Path -LiteralPath $key)) {
    & (Join-Path $Jdk "bin/keytool.exe") -genkeypair -noprompt -keystore $key -storetype JKS `
        -storepass android -keypass android -alias androiddebugkey -keyalg RSA -keysize 2048 `
        -validity 10000 -dname "CN=Android Debug,O=Android,C=US"
    if ($LASTEXITCODE -ne 0) { throw "Could not create the local test signing key." }
}

& $Dotnet build (Join-Path $PSScriptRoot "Boksol.Android.csproj") -c Release -o $package `
    "-p:AndroidSdkDirectory=$AndroidSdk" "-p:JavaSdkDirectory=$Jdk" `
    "-p:BoksolNativeRoot=$native" "-p:BoksolAbi=$Abi" `
    "-p:AndroidKeyStore=true" "-p:AndroidSigningKeyStore=$key" "-p:AndroidSigningKeyAlias=androiddebugkey" `
    "-p:AndroidSigningKeyPass=android" "-p:AndroidSigningStorePass=android"
if ($LASTEXITCODE -ne 0) { throw "Android build failed." }

$signed = Get-ChildItem -LiteralPath $package -Filter "*-Signed.apk" -File | Select-Object -First 1
if (!$signed) { throw "No signed APK was produced." }
$destination = Join-Path $output "boksol-0.5.4-android-$Abi.apk"
Copy-Item -LiteralPath $signed.FullName -Destination $destination -Force
Get-Item -LiteralPath $destination
