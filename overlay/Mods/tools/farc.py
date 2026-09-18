#!/usr/bin/env python3
"""FARC container reader/writer for FGO Arcade master data.

Validated byte-exact against all 57 `mst_data` archives that use this layout
(both the Sega originals in `App\\rom` and the platform's own repacks in
`App\\zh\\rom`), single- and multi-member, 1.4 KB to 711 KB.

    +0x00  4   'FARc'
    +0x04  28  header, big-endian
               +0x04  dir_end - 8   (offset of the last entry's usize field)
               +0x08  64            constant in every archive observed
               +0x0c  0             constant
               +0x10  16            constant
               +0x14  1             constant
               +0x18  member count for named archives (NOT reliable - two
                      tables carry something else there, so it is not parsed)
               +0x1c  16            constant
    +0x20  ..  directory, contiguous, one entry per member, ending exactly
               where the first member starts:
                   name\\0 + 4 * big-endian uint32 (off, csize, usize, field4)
    at each member's `off`, when field4 == 48 (zstd):
               chunk table, little-endian: uint32 blockSize, then
               ceil(usize / blockSize) uint32 frame sizes
               then that many zstd frames, each single-segment with an
               explicit content size where one is needed (frame header bytes
               0x60, 0xa0 and 0x20 all occur; the last chunk is short)
    at each member's `off`, when field4 == 0 (stored):
               the member's bytes verbatim, csize == usize, no chunk table

Padding is not reproducible from a rule: csize carries 0-3 bytes past the last
frame in some archives and nothing in others, and the platform's repacks also
leave 0-15 bytes between members (every member after the first starts on a
16-byte boundary, and the file is padded to 16 too). So repacking does not
model padding at all - an unedited member's container bytes are copied out of
the source verbatim, padding included, and only an edited member is rebuilt.
A no-edit repack is therefore byte-identical to its source, and a real edit
changes one member plus the directory offsets of those after it.

Repacking also reuses the source archive's header bytes, member order and
field4 verbatim, so every constant this format has that we have not proven
keeps its original value.

Not every `.farc` under `mst_data` uses this layout: `arms_mst_master_exp_table`
and `arms_mst_stack` carry a header variant with no name table at 0x20. They are
rejected with a clear error rather than mis-parsed.

Interpreter: the server's own Python already ships `zstandard`:
    <install>\\Server\\python\\python.exe
"""

import argparse
import struct
import sys

import zstandard as zstd

MAGIC = b'FARc'
HEADER_SIZE = 32
FIELD4_ZSTD = 48
FIELD4_STORED = 0
ZSTD_MAGIC = b'\x28\xb5\x2f\xfd'
MEMBER_ALIGN = 16
CSIZE_ALIGN = 4
COMPRESSION_LEVEL = 19


class FarcError(Exception):
    pass


class Member:
    def __init__(self, name, off, csize, usize, field4, block_size, frames, stored,
                 blob, gap_bytes):
        self.name = name
        self.off = off
        self.csize = csize
        self.usize = usize
        self.field4 = field4
        self.block_size = block_size
        self.frames = frames            # [] for stored members
        self.stored = stored            # bytes for stored members, None for zstd
        self.blob = blob                # this member's container bytes, padding included
        self.gap_bytes = gap_bytes      # bytes between this member and the next

    def __repr__(self):
        return ("Member(%r off=%d csize=%d usize=%d field4=%d block_size=%d "
                "frames=%d stored=%s gap=%d)"
                % (self.name, self.off, self.csize, self.usize, self.field4,
                   self.block_size, len(self.frames), self.stored is not None,
                   len(self.gap_bytes)))


def _align_up(value, modulus):
    return value + (-value % modulus)


class Archive:
    def __init__(self, path, data, header, members, aligned):
        self.path = path
        self.data = data
        self.header = header
        self.members = members
        self.aligned = aligned

    @property
    def dir_end(self):
        return HEADER_SIZE + sum(len(m.name.encode('utf-8')) + 1 + 16 for m in self.members)

    @property
    def trailing(self):
        return len(self.data) - (self.members[-1].off + self.members[-1].csize)

    def member(self, name):
        for m in self.members:
            if m.name == name:
                return m
        raise FarcError("no member named %r in %s (have: %s)"
                        % (name, self.path, ', '.join(m.name for m in self.members)))

    def decode(self, name):
        m = self.member(name)
        if m.stored is not None:
            return m.stored
        out = bytearray()
        for frame in m.frames:
            if frame[:4] != ZSTD_MAGIC:
                raise FarcError("member %r has a non-zstd frame" % name)
            out += zstd.ZstdDecompressor().decompressobj().decompress(frame)
        if len(out) != m.usize:
            raise FarcError("member %r decoded to %d bytes, header says %d"
                            % (name, len(out), m.usize))
        return bytes(out)

    def decode_all(self):
        return {m.name: self.decode(m.name) for m in self.members}

    def text(self, name):
        return self.decode(name).decode('utf-8')

    def repack(self, edits=None):
        """Return new container bytes with `edits` (name -> bytes) applied.

        An unedited member contributes its source container bytes verbatim, so
        a repack with no edits reproduces the source file exactly. An edited
        member is rebuilt from its new payload, reusing the source member's
        block size and field4.
        """
        edits = edits or {}
        unknown = set(edits) - {m.name for m in self.members}
        if unknown:
            raise FarcError("edits name members that do not exist: %s" % sorted(unknown))

        # (member, region bytes, gap bytes)
        built = []
        for m in self.members:
            if m.name not in edits:
                built.append((m, m.blob, m.gap_bytes))
                continue
            payload = edits[m.name]
            if not payload:
                raise FarcError("refusing to write empty member %r" % m.name)
            if m.stored is not None:
                built.append((m, payload, _synthetic_gap(m.off, len(payload), self.aligned)))
                continue
            frames, sizes = _compress_frames(payload, m.block_size)
            region = bytearray()
            region += struct.pack('<I', m.block_size)
            region += struct.pack('<%dI' % len(sizes), *sizes)
            for frame in frames:
                region += frame
            csize = _chunked_csize(len(sizes), sizes, self.aligned)
            region += b'\x00' * (csize - len(region))
            built.append((m, bytes(region),
                          _synthetic_gap(m.off, csize, self.aligned)))

        off = self.dir_end
        placed = []
        for (m, region, gap) in built:
            csize = len(region)
            usize = len(edits[m.name]) if m.name in edits else m.usize
            placed.append((m, region, gap, csize, usize, off))
            off += csize + len(gap)

        directory = bytearray()
        for (m, region, gap, csize, usize, member_off) in placed:
            directory += m.name.encode('utf-8') + b'\x00'
            directory += struct.pack('>IIII', member_off, csize, usize, m.field4)
        if len(directory) + HEADER_SIZE != self.dir_end:
            raise FarcError("directory size changed; member names must not change")

        out = bytearray(self.header)
        out += directory
        for (m, region, gap, csize, usize, member_off) in placed:
            if len(out) != member_off:
                raise FarcError("internal error: expected offset %d, writing at %d"
                                % (member_off, len(out)))
            out += region
            out += gap
        return bytes(out)

    def info(self):
        lines = ["%s  size=%d  members=%d  trailing=%d  aligned=%s"
                 % (self.path, len(self.data), len(self.members),
                    self.trailing, self.aligned)]
        lines.append("  header@04=%d (dir_end-8=%d)  header@08=%d  header@18=%d  dir_end=%d"
                     % (struct.unpack('>I', self.header[4:8])[0], self.dir_end - 8,
                        struct.unpack('>I', self.header[8:12])[0],
                        struct.unpack('>I', self.header[24:28])[0], self.dir_end))
        for m in self.members:
            if m.stored is not None:
                lines.append("  %-44s off=%-8d csize=%-8d usize=%-8d stored"
                             % (m.name, m.off, m.csize, m.usize))
            else:
                lines.append("  %-44s off=%-8d csize=%-8d usize=%-9d field4=%-3d "
                             "block=%-7d frames=%d"
                             % (m.name, m.off, m.csize, m.usize, m.field4,
                                m.block_size, len(m.frames)))
        return "\n".join(lines)


def _chunked_csize(count, sizes, aligned):
    base = 4 + 4 * count + sum(sizes)
    return base if aligned else _align_up(base, CSIZE_ALIGN)


def _synthetic_gap(off, csize, aligned):
    if not aligned:
        return b''
    return b'\x00' * (_align_up(off + csize, MEMBER_ALIGN) - (off + csize))


def _compress_frames(payload, block_size):
    compressor = zstd.ZstdCompressor(level=COMPRESSION_LEVEL, write_content_size=True,
                                     write_checksum=False, write_dict_id=False)
    frames, sizes = [], []
    for start in range(0, len(payload), block_size):
        frame = compressor.compress(payload[start:start + block_size])
        frames.append(frame)
        sizes.append(len(frame))
    return frames, sizes


def read_bytes(data, path='<bytes>'):
    from_here = path
    if data[:4] != MAGIC:
        raise FarcError("%s: bad magic %r" % (from_here, data[:4]))
    header = data[:HEADER_SIZE]
    # The directory ends where the first member begins, so walk it to there.
    # The header field at 0x18 looks like a member count for most archives but
    # two master tables carry something else, so it is never trusted.
    cursor = HEADER_SIZE
    members = []
    first_off = None
    while first_off is None or cursor < first_off:
        if len(members) >= 4096:
            raise FarcError("%s: directory walk did not terminate" % from_here)
        end = data.find(b'\x00', cursor)
        if end < 0:
            raise FarcError("%s: member %d name is not terminated"
                            % (from_here, len(members)))
        name = data[cursor:end]
        if not name or not all(0x20 <= byte < 0x7f for byte in name):
            raise FarcError("%s: unrecognised FARC variant (no name table at 0x20; "
                            "entry %d reads %r)"
                            % (from_here, len(members), bytes(name[:16])))
        off, csize, usize, field4 = struct.unpack('>IIII', data[end + 1:end + 17])
        cursor = end + 17
        label = name.decode('utf-8')
        if first_off is None:
            first_off = off
        elif cursor > first_off:
            raise FarcError("%s: directory overruns the first member at %d"
                            % (from_here, first_off))
        if off + csize > len(data):
            raise FarcError("%s: member %r runs past end of file" % (from_here, label))
        if members:
            previous_end = members[-1].off + members[-1].csize
            if off < previous_end:
                raise FarcError("%s: member %r starts at %d, previous ended at %d"
                                % (from_here, label, off, previous_end))
            if off - previous_end >= MEMBER_ALIGN:
                raise FarcError("%s: member %r starts %d bytes past the previous "
                                "member" % (from_here, label, off - previous_end))
        if field4 not in (FIELD4_ZSTD, FIELD4_STORED):
            raise FarcError("%s: member %r has unknown field4 value %d"
                            % (from_here, label, field4))
        if field4 == FIELD4_STORED:
            if csize != usize:
                raise FarcError("%s: stored member %r has csize %d but usize %d"
                                % (from_here, label, csize, usize))
            members.append(Member(label, off, csize, usize, field4, 0, [],
                                  bytes(data[off:off + usize]), b'', b''))
            continue
        block_size = struct.unpack('<I', data[off:off + 4])[0]
        if block_size == 0:
            raise FarcError("%s: member %r has blockSize 0" % (from_here, label))
        count = (usize + block_size - 1) // block_size
        sizes = struct.unpack('<%dI' % count, data[off + 4:off + 4 + 4 * count])
        spare = csize - (4 + 4 * count + sum(sizes))
        if spare < 0 or spare >= CSIZE_ALIGN + MEMBER_ALIGN:
            raise FarcError("%s: member %r has %d bytes unaccounted for inside csize"
                            % (from_here, label, spare))
        p = off + 4 + 4 * count
        frames = []
        for size in sizes:
            frames.append(bytes(data[p:p + size]))
            p += size
        members.append(Member(label, off, csize, usize, field4, block_size, frames,
                              None, b'', b''))
    if cursor != members[0].off:
        raise FarcError("%s: directory ends at %d but the first member starts at %d"
                        % (from_here, cursor, members[0].off))
    trailing = len(data) - (members[-1].off + members[-1].csize)
    if trailing >= MEMBER_ALIGN:
        raise FarcError("%s: %d bytes follow the last member" % (from_here, trailing))
    ends = [m.off + m.csize for m in members]
    starts = [m.off for m in members[1:]] + [len(data)]
    aligned = all(start == _align_up(end, MEMBER_ALIGN)
                  for start, end in zip(starts, ends))
    for index, m in enumerate(members):
        m.blob = bytes(data[m.off:m.off + m.csize])
        m.gap_bytes = bytes(data[m.off + m.csize:starts[index]])
    return Archive(path, data, header, members, aligned)


def read(path):
    with open(path, 'rb') as handle:
        return read_bytes(handle.read(), path)


def _cmd_list(args):
    for path in args.farc:
        print(read(path).info())
    return 0


def _cmd_extract(args):
    archive = read(args.farc)
    payload = archive.decode(args.member)
    with open(args.out, 'wb') as handle:
        handle.write(payload)
    print("wrote %d bytes to %s" % (len(payload), args.out))
    return 0


def _cmd_repack(args):
    archive = read(args.farc)
    edits = {}
    for spec in args.set:
        name, _, source = spec.partition('=')
        with open(source, 'rb') as handle:
            edits[name] = handle.read()
    out = archive.repack(edits)
    with open(args.out, 'wb') as handle:
        handle.write(out)
    print("source %d bytes -> %s %d bytes (delta %+d)"
          % (len(archive.data), args.out, len(out), len(out) - len(archive.data)))
    return 0


def _cmd_check(args):
    """A no-edit repack must reproduce the source bytes exactly."""
    failures = 0
    unsupported = []
    identical = 0
    checked = 0
    for path in args.farc:
        try:
            archive = read(path)
        except FarcError as error:
            unsupported.append(str(error))
            continue
        checked += 1
        rebuilt = read_bytes(archive.repack(), path + ' (repacked)')
        exact = rebuilt.data == archive.data
        identical += 1 if exact else 0
        print("%s  members=%d  aligned=%s  size %d -> %d  byte-identical=%s"
              % (path, len(archive.members), archive.aligned,
                 len(archive.data), len(rebuilt.data), exact))
        names_match = [m.name for m in archive.members] == [m.name for m in rebuilt.members]
        if not names_match:
            failures += 1
            continue
        for original, new in zip(archive.members, rebuilt.members):
            before = archive.decode(original.name)
            after = rebuilt.decode(new.name)
            if before != after:
                failures += 1
                print("    MISMATCH %s: %d -> %d bytes"
                      % (original.name, len(before), len(after)))
    print("\nchecked %d archives, %d byte-identical, %d unsupported, FAILURES: %d"
          % (checked, identical, len(unsupported), failures))
    for message in unsupported:
        print("  unsupported: %s" % message)
    return 1 if failures or identical != checked else 0


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest='command', required=True)

    p = sub.add_parser('list', help='describe one or more archives')
    p.add_argument('farc', nargs='+')
    p.set_defaults(func=_cmd_list)

    p = sub.add_parser('extract', help='write one member to a file')
    p.add_argument('farc')
    p.add_argument('member')
    p.add_argument('out')
    p.set_defaults(func=_cmd_extract)

    p = sub.add_parser('repack', help='write a new archive, optionally replacing members')
    p.add_argument('farc')
    p.add_argument('out')
    p.add_argument('set', nargs='*', metavar='member=file',
                   help='replace the named member with the file contents')
    p.set_defaults(func=_cmd_repack)

    p = sub.add_parser('check', help='verify that a no-edit repack is byte-exact')
    p.add_argument('farc', nargs='+')
    p.set_defaults(func=_cmd_check)

    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == '__main__':
    sys.exit(main())