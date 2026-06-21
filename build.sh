#!/usr/bin/env bash
# Recompila el mod y lo copia a la carpeta de plugins de Rogue Tower.
#
# GameDir (la carpeta de Rogue Tower, la que contiene "Rogue Tower_Data/Managed"
# y "BepInEx/core") se resuelve asi:
#   1. La variable de entorno ROGUE_TOWER_DIR, si esta definida.
#   2. Si no, el default de Steam en Linux bajo $HOME.
#
# Para otra ruta:
#   export ROGUE_TOWER_DIR="/ruta/a/steamapps/common/Rogue Tower"
#   ./build.sh
set -e

export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

HERE="$(cd "$(dirname "$0")" && pwd)"

# Resolver GAME desde ROGUE_TOWER_DIR con fallback al default conocido.
GAME="${ROGUE_TOWER_DIR:-$HOME/.steam/debian-installation/steamapps/common/Rogue Tower}"
# Exportar para que MSBuild lea el mismo valor via $(ROGUE_TOWER_DIR).
export ROGUE_TOWER_DIR="$GAME"

if [ ! -d "$GAME/Rogue Tower_Data/Managed" ] || [ ! -d "$GAME/BepInEx/core" ]; then
  echo "ERROR: '$GAME' no parece la carpeta de Rogue Tower." >&2
  echo "       Falta 'Rogue Tower_Data/Managed' o 'BepInEx/core'." >&2
  echo "       Define ROGUE_TOWER_DIR con la ruta correcta y reintenta." >&2
  exit 1
fi

dotnet build "$HERE/RangeMultiplier.csproj" -c Release -o "$HERE/out"
cp -v "$HERE/out/RangeMultiplier.dll" "$GAME/BepInEx/plugins/"
echo "Listo: RangeMultiplier.dll actualizado en plugins/. Reinicia el juego para cargarlo."
