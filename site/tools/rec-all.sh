#!/bin/zsh
# Records the film at desktop (1440×900) and phone (390×844) size into build/previews/website-form/.
# Serve first: cd site && python3 -m http.server 48780 --bind 127.0.0.1
# Frames go to a scratch folder ($1), never into the project.
cd "${0:A:h}/.."
scratch=${1:-/tmp/dragshift-frames}
out=../build/previews/website-form
mkdir -p $out
for size in ${=2:-"1440 900 desktop" "390 844 phone"}; do
  parts=(${=size})
  rm -rf $scratch/form-${parts[3]}
  python3 tools/rec.py "http://127.0.0.1:${PORT:-48780}/?rec" ${parts[1]} ${parts[2]} $scratch/form-${parts[3]} $out/form-3d-${parts[3]}.mp4 || echo "FAILED ${parts[3]}"
  rm -rf $scratch/form-${parts[3]}
done
