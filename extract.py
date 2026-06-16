import sys

with open('extracted.txt', 'w', encoding='utf-8') as out_f:
    with open(r'C:\Users\rimmy\.gemini\antigravity\brain\0fd663f8-00a7-4f69-ab9b-183dd4de85e7\.system_generated\logs\transcript_full.jsonl', 'r', encoding='utf-8') as f:
        for line in f:
            if '멱살잡기' in line:
                idx = line.find('멱살잡기')
                out_f.write(line[max(0, idx - 50):min(len(line), idx + 200)] + '\n')
