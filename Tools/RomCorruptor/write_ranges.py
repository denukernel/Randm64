#!/usr/bin/env python3
"""Validate and describe literal segments in a separately linked SM64 build."""
import hashlib
import os
import re
import struct
import sys

rom_path, map_path, report_path = sys.argv[1:]
with open(rom_path, 'rb') as f:
    rom = f.read()
if len(rom) > 24 * 1024 * 1024 or rom[:4] != b'\x80\x37\x12\x40':
    raise ValueError('build is not a big endian rom that fits in 24 mib')
markers = {}
pattern = re.compile(r'^\s*0x([0-9a-fA-F]+)\s+_([a-zA-Z0-9_]+)SegmentRom(Start|End)\s*=')
with open(map_path, encoding='utf-8') as f:
    for line in f:
        m = pattern.match(line)
        if m:
            markers.setdefault(m.group(2), {})[m.group(3)] = int(m.group(1), 16)
behavior = markers.get('behavior', {})
if 'Start' not in behavior or 'End' not in behavior or behavior['End'] <= behavior['Start']:
    raise ValueError('behavior segment boundaries missing from build map')
rows = []
for name, bounds in markers.items():
    if not (name.startswith('group') and name.endswith('_mio0') or name.endswith('_segment_7')):
        continue
    start, end = bounds.get('Start', -1), bounds.get('End', -1)
    if start < 0 or end > len(rom) or end <= start or rom[start:start+4] != b'MIO0':
        raise ValueError('invalid mio0 segment: ' + name)
    size, compressed, literal = struct.unpack_from('>III', rom, start+4)
    masklen = (size + 7) // 8
    if literal != 16 + masklen or compressed != literal or start + literal + size > end:
        raise ValueError('segment is not a literal mio0 stream: ' + name)
    if any(x != 255 for x in rom[start+16:start+literal]):
        raise ValueError('segment contains compressed data: ' + name)
    lo, hi = start + literal, start + literal + size
    build_dir = os.path.dirname(map_path)
    if name.startswith('group'):
        data_file = os.path.join(build_dir, 'actors', name[:-5] + '.bin')
    else:
        data_file = os.path.join(build_dir, 'levels', name[:-10], 'leveldata.bin')
    if not os.path.isfile(data_file):
        raise ValueError('missing source segment data: ' + data_file)
    with open(data_file, 'rb') as f:
        if f.read() != rom[lo:hi]:
            raise ValueError('literal payload does not match built segment: ' + name)
    if lo < behavior['End'] and hi > behavior['Start']:
        raise ValueError('segment overlaps behavior data: ' + name)
    if size:
        rows.append((lo, hi - 1, name))
if not rows:
    raise ValueError('no actor or level segments found in build map')
rom += b'\xff' * (24 * 1024 * 1024 - len(rom))
with open(rom_path, 'wb') as f:
    f.write(rom)
rom_name = os.path.basename(rom_path)
if rom_name.endswith('.tmp'):
    rom_name = rom_name[:-4]
lines = ['sm64 build-specific corruption ranges',
         'rom: ' + rom_name,
         'sha256: ' + hashlib.sha256(rom).hexdigest(),
         'offsets are hexadecimal and inclusive. select one range at a time.',
         'these are literal mio0 payloads. do not corrupt their headers or masks.',
         'actor groups contain multiple models; levels may include geometry, display lists and collision.',
         'behavior scripts are excluded, but corruption can still crash the game.',
         'behavior segment excluded: %08X-%08X' % (behavior['Start'], behavior['End']-1), '',
         'start     end       segment']
for lo, hi, name in sorted(rows):
    description = name
    if name.startswith('group') and name.endswith('_mio0'):
        source = os.path.join('actors', name[:-5] + '.c')
        if os.path.isfile(source):
            with open(source, encoding='utf-8') as f:
                actors = list(dict.fromkeys(re.findall(r'^\s*#\s*include\s+"([^"/]+)/model\.inc\.c"', f.read(), re.M)))
            if actors:
                description += ' (' + ', '.join(actors) + ')'
    lines.append('%08X  %08X  %s' % (lo, hi, description))
with open(report_path, 'w', encoding='utf-8', newline='\n') as f:
    f.write('\n'.join(lines) + '\n')
