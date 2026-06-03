import os
import re

strings = set()
for root, dirs, files in os.walk('c:/Users/rimmy/Desktop/Studies/indie/indie/Necormander_3/Assets/SOData'):
    if 'Deprecated' in root:
        continue
    for file in files:
        if file.endswith('.asset'):
            path = os.path.join(root, file)
            with open(path, 'r', encoding='utf-8') as f:
                content = f.read()
                names = re.findall(r'itemName:\s*(.+)', content)
                descs = re.findall(r'description:\s*(.+)', content)
                for n in names:
                    strings.add(n.strip())
                for d in descs:
                    strings.add(d.strip())

with open('gem_strings.txt', 'w', encoding='utf-8') as f:
    for s in sorted(strings):
        f.write(s + '\n')
