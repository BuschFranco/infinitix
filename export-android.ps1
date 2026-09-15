# Exporta el APK de Android sin pasar por el dialogo de exportacion del editor.
# El dialogo reporta falsamente "Missing platform-tools/build-tools"; este camino
# corre el mismo pipeline (Gradle + Android SDK) y funciona.
#
#   .\export-android.ps1              -> APK debug (firmado con el keystore de debug de Godot)
#   .\export-android.ps1 -Release     -> APK release (requiere keystore propio configurado)

param(
    [switch]$Release,
    [string]$Out = "$PSScriptRoot\build\ShooterLoop.apk"
)

$ErrorActionPreference = "Stop"

$godot = "C:\Godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe"
if (-not (Test-Path $godot)) { throw "No encuentro Godot en $godot" }

# Godot debe estar cerrado: comparte el cache de importacion y los editor settings.
if (Get-Process Godot* -ErrorAction SilentlyContinue) {
    Write-Warning "Godot esta abierto. Cerralo antes de exportar para evitar conflictos de cache."
}

New-Item -ItemType Directory -Force -Path (Split-Path $Out) | Out-Null

$mode = if ($Release) { "--export-release" } else { "--export-debug" }
Write-Host "Exportando ($mode) -> $Out" -ForegroundColor Cyan

& $godot --headless --path $PSScriptRoot $mode "Android" $Out

if (Test-Path $Out) {
    $mb = [math]::Round((Get-Item $Out).Length / 1MB, 1)
    Write-Host "OK: $Out ($mb MB)" -ForegroundColor Green
} else {
    throw "La exportacion no genero el archivo. Revisa el log de arriba."
}
