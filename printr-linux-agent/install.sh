#!/usr/bin/env bash
set -euo pipefail
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
install_dir="${XDG_DATA_HOME:-$HOME/.local/share}/printr/bin"
unit_dir="${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user"
if [[ "$install_dir" == *$'\n'* || "$install_dir" == *'"'* || "$install_dir" == *'%'* || "$install_dir" == *'\'* || "$install_dir" == *'&'* || "$install_dir" == *'|'* ]]; then
    printf '%s\n' 'Unsupported installation path.' >&2
    exit 1
fi
mkdir -p -- "$install_dir" "$unit_dir"
install -m 755 -- "$script_dir/PrintR.Agent.Linux" "$install_dir/PrintR.Agent.Linux"
sed "s|@EXECUTABLE@|$install_dir/PrintR.Agent.Linux|g" "$script_dir/printr-agent.service" > "$unit_dir/printr-agent.service"
systemctl --user daemon-reload
printf '%s\n' 'Installed for the current user.' 'Start: systemctl --user enable --now printr-agent' 'Logs: journalctl --user -u printr-agent' "Pair: \"$install_dir/PrintR.Agent.Linux\" --pairing"
