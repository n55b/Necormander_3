import os
import re
from pathlib import Path

project_root = Path(__file__).resolve().parents[2]
data_dir = Path(__file__).resolve().parent / "Data"

strings = set()
for root, dirs, files in os.walk(project_root / 'Assets/SOData'):
    if 'Deprecated' in root:
        continue
    for file in files:
        if file.endswith('.asset'):
            path = os.path.join(root, file)
            with open(path, 'r', encoding='utf-8') as f:
                content = f.read()
                # LocText 필드는 다음 줄의 ko: 값을 읽는다(옛 평문 필드는 같은 줄).
                names = re.findall(r'itemName:[ \t]*(?:\n[ \t]+ko:[ \t]*)?(\S.*)', content)
                descs = re.findall(r'description:[ \t]*(?:\n[ \t]+ko:[ \t]*)?(\S.*)', content)
                for n in names:
                    strings.add(n.strip())
                for d in descs:
                    strings.add(d.strip())

with open(data_dir / 'gem_strings.txt', 'w', encoding='utf-8') as f:
    for s in sorted(strings):
        f.write(s + '\n')
