#!/bin/zsh
# How many times each DragShift file was downloaded, per release. GitHub counts this itself, so the site needs no
# trackers. Needs the GitHub CLI (gh). Without it, open https://api.github.com/repos/c0ldsheep/DragShift/releases
# in any browser and search the page for "download_count".
set -euo pipefail
gh api --paginate repos/c0ldsheep/DragShift/releases --jq '.[] | .tag_name as $t | .assets[] | "\($t)\t\(.name)\t\(.download_count)"' |
  awk -F'\t' '{ printf "%-10s %-22s %8d\n", $1, $2, $3; sum += $3 } END { printf "%-33s %8d\n", "All downloads", sum }'
