$ErrorActionPreference = "Stop"

$projectRoot = "c:\Users\fauve\Documents\GitHub\CSM"
$scriptPath = Join-Path $projectRoot "Tools\Blender\generate_lowpoly_character.py"
$outputPath = Join-Path $projectRoot "Assets\Ressources\Prefabs\lowpoly_character.blend"

$blenderCandidates = @(
    "C:\Program Files\Blender Foundation\Blender\blender.exe",
    "C:\Program Files (x86)\Blender Foundation\Blender\blender.exe",
    (Join-Path $env:LOCALAPPDATA "Programs\Blender Foundation\Blender\blender.exe")
)

$blenderExe = $blenderCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $blenderExe) {
    throw "Blender executable introuvable. Installe Blender puis relance ce script."
}

& $blenderExe --background --python $scriptPath -- $outputPath

if (Test-Path $outputPath) {
    Write-Host "Fichier genere: $outputPath"
} else {
    throw "Generation echouee: le fichier .blend n'a pas ete cree."
}
