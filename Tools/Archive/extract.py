import sys
from pathlib import Path

if len(sys.argv) != 2:
    raise SystemExit("Usage: python Tools/Archive/extract.py <transcript.jsonl>")

output_dir = Path(__file__).resolve().parents[2] / "Logs"
output_dir.mkdir(exist_ok=True)

with open(output_dir / 'extracted.txt', 'w', encoding='utf-8') as out_f:
    with open(sys.argv[1], 'r', encoding='utf-8') as f:
        for line in f:
            if '멱살잡기' in line:
                idx = line.find('멱살잡기')
                out_f.write(line[max(0, idx - 50):min(len(line), idx + 200)] + '\n')
