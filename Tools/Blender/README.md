# Low Poly Character Generator

Ce dossier contient un script Blender qui genere automatiquement un personnage low poly et sauvegarde un fichier `.blend`.

## Fichiers

- `generate_lowpoly_character.py` : script de generation.

## Utilisation

1. Installe Blender (si ce n'est pas deja fait).
2. Lance la commande ci-dessous en adaptant les chemins :

```powershell
"C:\Program Files\Blender Foundation\Blender\blender.exe" --background --python "c:\Users\fauve\Documents\GitHub\CSM\Tools\Blender\generate_lowpoly_character.py" -- "c:\Users\fauve\Documents\GitHub\CSM\Assets\Ressources\Prefabs\lowpoly_character.blend"
```

Si tu ne fournis pas le chemin final apres `--`, le script cree `lowpoly_character.blend` dans le dossier courant.

## Option rapide (PowerShell)

Tu peux aussi lancer directement :

```powershell
powershell -ExecutionPolicy Bypass -File "c:\Users\fauve\Documents\GitHub\CSM\Tools\Blender\run_generate_lowpoly_character.ps1"
```

Ce script detecte Blender dans les chemins Windows standards puis cree :
`c:\Users\fauve\Documents\GitHub\CSM\Assets\Ressources\Prefabs\lowpoly_character.blend`
