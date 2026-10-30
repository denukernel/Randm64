#!/usr/bin/env python3
"""Build MIO0 streams whose literal payload can be edited directly."""
import struct
import subprocess
import sys

source, destination = sys.argv[1:]
normalized = source.replace('\\', '/')
if '/actors/' not in normalized and '/levels/' not in normalized:
    subprocess.run(['tools/sm64tools/mio0', source, destination], check=True)
    sys.exit(0)
with open(source, 'rb') as f:
    data = f.read()
mask = b'\xff' * ((len(data) + 7) // 8)
offset = 16 + len(mask)
with open(destination, 'wb') as f:
    f.write(b'MIO0' + struct.pack('>III', len(data), offset, offset))
    f.write(mask)
    f.write(data)
