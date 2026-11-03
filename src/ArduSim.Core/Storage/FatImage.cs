using System.Buffers.Binary;
using System.Text;

namespace ArduSim.Core.Storage;

/// <summary>A file or folder on a simulated card: the path uses '/'; a folder has no content.</summary>
public sealed record CardFile(string Path, byte[]? Content)
{
    public bool IsFolder => Content is null;
}

/// <summary>
/// The blocks of a simulated SD card (ADR-012), kept sparse in memory: 64 MB, one primary partition
/// at block 2048 formatted FAT16 with 2 KB clusters, as a small card comes from the shop. Files are
/// written with VFAT long names where the 8.3 name does not fit, and read back from FAT16 or FAT32
/// (a sketch may format the card), so the card's content can live in a folder of the project.
/// </summary>
public sealed class FatImage
{
    public const int BlockSize = 512;
    public const uint Blocks = 131072;

    private const uint PartitionStart = 2048;
    private const int SectorsPerCluster = 4;
    private const int RootEntries = 512;
    private const int ReservedSectors = 1;

    private readonly Dictionary<uint, byte[]> _blocks = [];

    /// <summary>A freshly formatted, empty card.</summary>
    public FatImage()
    {
        Format();
    }

    public bool IsDirty { get; private set; }

    public static long Capacity => (long)Blocks * BlockSize;

    /// <summary>A card holding <paramref name="files"/>; returns the paths that did not fit or have names FAT cannot hold.</summary>
    public static (FatImage Image, IReadOnlyList<string> Skipped) Create(IEnumerable<CardFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var image = new FatImage();
        var writer = new Writer(image);
        var skipped = new List<string>();
        foreach (CardFile file in files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (!writer.Add(file))
            {
                skipped.Add(file.Path);
            }
        }

        writer.Finish();
        image.IsDirty = false;
        return (image, skipped);
    }

    public void Read(uint block, Span<byte> destination)
    {
        if (_blocks.TryGetValue(block, out byte[]? data))
        {
            data.CopyTo(destination);
        }
        else
        {
            destination[..BlockSize].Clear();
        }
    }

    public void Write(uint block, ReadOnlySpan<byte> source)
    {
        if (block >= Blocks)
        {
            return;
        }

        if (!_blocks.TryGetValue(block, out byte[]? data))
        {
            data = new byte[BlockSize];
            _blocks[block] = data;
        }

        source[..BlockSize].CopyTo(data);
        IsDirty = true;
    }

    /// <summary>Every file and folder on the card, read through its file system (FAT16 or FAT32).</summary>
    public IReadOnlyList<CardFile> Files()
    {
        var files = new List<CardFile>();
        if (Volume.Open(this) is { } volume)
        {
            volume.Walk(volume.Root, "", files, 0);
        }

        return files;
    }

    private Span<byte> Block(uint block)
    {
        if (!_blocks.TryGetValue(block, out byte[]? data))
        {
            data = new byte[BlockSize];
            _blocks[block] = data;
        }

        return data;
    }

    private void Format()
    {
        uint sectors = Blocks - PartitionStart;
        int fatSectors = FatSectors(sectors);

        // MBR: one FAT16 partition (type 0x06).
        Span<byte> mbr = Block(0);
        Span<byte> entry = mbr.Slice(446, 16);
        entry[0] = 0x00;
        entry[1] = 0xFE;
        entry[2] = 0xFF;
        entry[3] = 0xFF;
        entry[4] = 0x06;
        entry[5] = 0xFE;
        entry[6] = 0xFF;
        entry[7] = 0xFF;
        BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], PartitionStart);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], sectors);
        mbr[510] = 0x55;
        mbr[511] = 0xAA;

        // Boot sector with the FAT16 BPB.
        Span<byte> boot = Block(PartitionStart);
        boot[0] = 0xEB;
        boot[1] = 0x3C;
        boot[2] = 0x90;
        Encoding.ASCII.GetBytes("ARDUSIM ").CopyTo(boot[3..]);
        BinaryPrimitives.WriteUInt16LittleEndian(boot[11..], BlockSize);
        boot[13] = SectorsPerCluster;
        BinaryPrimitives.WriteUInt16LittleEndian(boot[14..], ReservedSectors);
        boot[16] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(boot[17..], RootEntries);
        BinaryPrimitives.WriteUInt16LittleEndian(boot[19..], 0);
        boot[21] = 0xF8;
        BinaryPrimitives.WriteUInt16LittleEndian(boot[22..], (ushort)fatSectors);
        BinaryPrimitives.WriteUInt16LittleEndian(boot[24..], 63);
        BinaryPrimitives.WriteUInt16LittleEndian(boot[26..], 255);
        BinaryPrimitives.WriteUInt32LittleEndian(boot[28..], PartitionStart);
        BinaryPrimitives.WriteUInt32LittleEndian(boot[32..], sectors);
        boot[36] = 0x80;
        boot[38] = 0x29;
        BinaryPrimitives.WriteUInt32LittleEndian(boot[39..], 0x41534D44);
        Encoding.ASCII.GetBytes("ARDUSIM SD FAT16   ").CopyTo(boot[43..]);
        boot[510] = 0x55;
        boot[511] = 0xAA;

        // Both FATs: media byte and end-of-chain for the two reserved entries.
        for (int fat = 0; fat < 2; fat++)
        {
            Span<byte> first = Block(PartitionStart + ReservedSectors + (uint)(fat * fatSectors));
            BinaryPrimitives.WriteUInt16LittleEndian(first, 0xFFF8);
            BinaryPrimitives.WriteUInt16LittleEndian(first[2..], 0xFFFF);
        }
    }

    // The FAT size for a FAT16 volume of this many sectors (each entry covers one cluster).
    private static int FatSectors(uint sectors)
    {
        int rootSectors = RootEntries * 32 / BlockSize;
        int fat = 1;
        while (true)
        {
            long clusters = (sectors - ReservedSectors - (2 * fat) - rootSectors) / SectorsPerCluster;
            int needed = (int)(((clusters + 2) * 2 + BlockSize - 1) / BlockSize);
            if (needed <= fat)
            {
                return fat;
            }

            fat = needed;
        }
    }

    private static DateTime Stamp => new(2026, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);

    /// <summary>Lays files into a freshly formatted FAT16 volume: clusters in order, folders as cluster chains.</summary>
    private sealed class Writer
    {
        private readonly FatImage _image;
        private readonly Volume _volume;
        private readonly Dictionary<string, Folder> _folders = new(StringComparer.OrdinalIgnoreCase);
        private uint _nextCluster = 2;

        public Writer(FatImage image)
        {
            _image = image;
            _volume = Volume.Open(image)!;
            _folders[""] = new Folder(0);
        }

        public bool Add(CardFile file)
        {
            string[] parts = file.Path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts.Any(p => !ValidName(p)))
            {
                return false;
            }

            Folder? parent = Parent(parts);
            if (parent is null)
            {
                return false;
            }

            string key = string.Join('/', parts);
            if (file.IsFolder)
            {
                return Ensure(key, parent, parts[^1]) is not null;
            }

            byte[] content = file.Content!;
            uint clusters = (uint)((content.Length + _volume.ClusterBytes - 1) / _volume.ClusterBytes);
            if (_nextCluster + clusters > _volume.Clusters + 2)
            {
                return false;
            }

            uint first = content.Length == 0 ? 0 : Allocate(clusters);
            for (uint i = 0; i < clusters; i++)
            {
                int offset = (int)(i * _volume.ClusterBytes);
                WriteCluster(first + i, content.AsSpan(offset, Math.Min(_volume.ClusterBytes, content.Length - offset)));
            }

            return parent.Entries.Add(parts[^1], first, (uint)content.Length, folder: false);
        }

        public void Finish()
        {
            foreach ((string _, Folder folder) in _folders.OrderBy(f => f.Key.Length))
            {
                folder.Entries.WriteTo(_volume, folder.Cluster);
            }
        }

        private Folder? Parent(string[] parts)
        {
            Folder parent = _folders[""];
            for (int i = 0; i < parts.Length - 1; i++)
            {
                string key = string.Join('/', parts[..(i + 1)]);
                if (Ensure(key, parent, parts[i]) is not { } next)
                {
                    return null;
                }

                parent = next;
            }

            return parent;
        }

        private Folder? Ensure(string key, Folder parent, string name)
        {
            if (_folders.TryGetValue(key, out Folder? known))
            {
                return known;
            }

            if (_nextCluster + 8 > _volume.Clusters + 2)
            {
                return null;
            }

            // A folder gets 8 clusters (512 entries) up front.
            uint cluster = Allocate(8);
            var folder = new Folder(cluster);
            folder.Entries.AddDots(cluster, parent.Cluster);
            if (!parent.Entries.Add(name, cluster, 0, folder: true))
            {
                return null;
            }

            _folders[key] = folder;
            return folder;
        }

        private uint Allocate(uint count)
        {
            uint first = _nextCluster;
            for (uint i = 0; i < count; i++)
            {
                _volume.SetFat(first + i, i + 1 < count ? first + i + 1 : 0xFFFF);
            }

            _nextCluster += count;
            return first;
        }

        private void WriteCluster(uint cluster, ReadOnlySpan<byte> data)
        {
            uint sector = _volume.ClusterSector(cluster);
            Span<byte> block = stackalloc byte[BlockSize];
            for (int i = 0; i < SectorsPerCluster; i++)
            {
                block.Clear();
                int offset = i * BlockSize;
                if (offset < data.Length)
                {
                    data.Slice(offset, Math.Min(BlockSize, data.Length - offset)).CopyTo(block);
                }

                _image.Write(sector + (uint)i, block);
            }
        }
    }

    private sealed record Folder(uint Cluster)
    {
        public EntryList Entries { get; } = new();
    }

    /// <summary>Directory entries of one folder, as raw 32-byte records (long-name entries first).</summary>
    private sealed class EntryList
    {
        /// <summary>Records per folder: the FAT16 root's 512, and 8 clusters of 2 KB for the others.</summary>
        private const int Capacity = 512;

        private readonly List<byte[]> _records = [];
        private readonly HashSet<string> _shortNames = new(StringComparer.Ordinal);
        private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

        public void AddDots(uint self, uint parent)
        {
            _records.Add(Record(".          "u8.ToArray(), 0x10, self, 0, 0));
            _records.Add(Record("..         "u8.ToArray(), 0x10, parent, 0, 0));
        }

        public bool Add(string name, uint cluster, uint size, bool folder)
        {
            if (!_names.Add(name))
            {
                return false;
            }

            (byte[] shortName, byte caseFlags, bool needsLong) = ShortName(name);
            if (_records.Count + 1 + (needsLong ? (name.Length + 12) / 13 : 0) > Capacity)
            {
                return false;
            }

            if (needsLong)
            {
                byte checksum = Checksum(shortName);
                char[] chars = [.. name, '\0'];
                int parts = (name.Length + 12) / 13;
                for (int part = parts; part >= 1; part--)
                {
                    var record = new byte[32];
                    record[0] = (byte)(part | (part == parts ? 0x40 : 0));
                    record[11] = 0x0F;
                    record[13] = checksum;
                    int[] offsets = [1, 3, 5, 7, 9, 14, 16, 18, 20, 22, 24, 28, 30];
                    for (int i = 0; i < 13; i++)
                    {
                        int index = ((part - 1) * 13) + i;
                        ushort value = index < chars.Length ? chars[index] : (ushort)0xFFFF;
                        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(offsets[i]), value);
                    }

                    _records.Add(record);
                }
            }

            _records.Add(Record(shortName, (byte)(folder ? 0x10 : 0x20), cluster, size, caseFlags));
            return true;
        }

        public void WriteTo(Volume volume, uint cluster)
        {
            uint first = cluster == 0 ? volume.RootSector : volume.ClusterSector(cluster);
            Span<byte> block = stackalloc byte[BlockSize];
            for (int sector = 0; sector * 16 < Capacity; sector++)
            {
                block.Clear();
                for (int i = 0; i < 16 && (sector * 16) + i < _records.Count; i++)
                {
                    _records[(sector * 16) + i].CopyTo(block[(i * 32)..]);
                }

                volume.Image.Write(first + (uint)sector, block);
            }
        }

        // The 8.3 name: the name itself when it fits (lower case kept by the NT case flags), else BASIS~N.EXT with a long name.
        private (byte[] Name, byte CaseFlags, bool NeedsLong) ShortName(string name)
        {
            int dot = name.LastIndexOf('.');
            string stem = dot > 0 ? name[..dot] : name;
            string extension = dot > 0 ? name[(dot + 1)..] : "";
            bool fits = stem.Length is > 0 and <= 8 && extension.Length <= 3 && name.Count(c => c == '.') <= 1 && (stem + extension).All(ShortChar);
            bool lowerStem = stem.Any(char.IsLower) && !stem.Any(char.IsUpper);
            bool lowerExtension = extension.Any(char.IsLower) && !extension.Any(char.IsUpper);
            bool mixed = (stem.Any(char.IsLower) && stem.Any(char.IsUpper)) || (extension.Any(char.IsLower) && extension.Any(char.IsUpper));
            if (fits && !mixed)
            {
                string candidate = stem.ToUpperInvariant().PadRight(8) + extension.ToUpperInvariant().PadRight(3);
                if (_shortNames.Add(candidate))
                {
                    return (Encoding.ASCII.GetBytes(candidate), (byte)((lowerStem ? 0x08 : 0) | (lowerExtension ? 0x10 : 0)), false);
                }
            }

            string basis = new([.. stem.ToUpperInvariant().Where(ShortChar).Take(6)]);
            string ext = new([.. extension.ToUpperInvariant().Where(ShortChar).Take(3)]);
            basis = basis.Length == 0 ? "FILE" : basis;
            for (int n = 1; n < 1_000_000; n++)
            {
                string tail = "~" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string candidate = (basis[..Math.Min(basis.Length, 8 - tail.Length)] + tail).PadRight(8) + ext.PadRight(3);
                if (_shortNames.Add(candidate))
                {
                    return (Encoding.ASCII.GetBytes(candidate), 0, true);
                }
            }

            throw new InvalidOperationException("No free short name.");
        }

        private static bool ShortChar(char c) => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') || "$%'-_@~`!(){}^#&".Contains(c, StringComparison.Ordinal);

        private static byte[] Record(byte[] shortName, byte attributes, uint cluster, uint size, byte caseFlags)
        {
            var record = new byte[32];
            shortName.CopyTo(record, 0);
            record[11] = attributes;
            record[12] = caseFlags;
            ushort date = (ushort)(((Stamp.Year - 1980) << 9) | (Stamp.Month << 5) | Stamp.Day);
            ushort time = (ushort)((Stamp.Hour << 11) | (Stamp.Minute << 5) | (Stamp.Second / 2));
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(14), time);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(16), date);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(18), date);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(20), (ushort)(cluster >> 16));
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(22), time);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(24), date);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(26), (ushort)cluster);
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(28), size);
            return record;
        }
    }

    internal static byte Checksum(ReadOnlySpan<byte> shortName)
    {
        byte sum = 0;
        foreach (byte b in shortName[..11])
        {
            sum = (byte)(((sum & 1) << 7) + (sum >> 1) + b);
        }

        return sum;
    }

    private static bool ValidName(string name) =>
        name.Length is > 0 and <= 255 && name != "." && name != ".." && !name.Any(c => c < 0x20 || "\"*/:<>?\\|".Contains(c, StringComparison.Ordinal));

    /// <summary>A FAT16 or FAT32 volume on the image, found through the MBR or at block 0.</summary>
    private sealed class Volume
    {
        private Volume(FatImage image, uint start, int sectorsPerCluster, int reserved, int fats, int rootEntries, uint fatSectors, uint totalSectors, uint rootCluster)
        {
            Image = image;
            Start = start;
            SectorsPerCluster = sectorsPerCluster;
            FatStart = start + (uint)reserved;
            FatSectorCount = fatSectors;
            Fats = fats;
            RootSector = FatStart + ((uint)fats * fatSectors);
            uint rootSectors = (uint)((rootEntries * 32) + BlockSize - 1) / BlockSize;
            DataStart = RootSector + rootSectors;
            Clusters = (totalSectors - (DataStart - start)) / (uint)sectorsPerCluster;
            IsFat32 = rootEntries == 0;
            Root = IsFat32 ? rootCluster : 0;
        }

        public FatImage Image { get; }

        public uint Start { get; }

        public int SectorsPerCluster { get; }

        public uint FatStart { get; }

        public uint FatSectorCount { get; }

        public int Fats { get; }

        public uint RootSector { get; }

        public uint DataStart { get; }

        public uint Clusters { get; }

        public bool IsFat32 { get; }

        /// <summary>The root folder: cluster 0 stands for FAT16's fixed root area.</summary>
        public uint Root { get; }

        public int ClusterBytes => SectorsPerCluster * BlockSize;

        public static Volume? Open(FatImage image)
        {
            Span<byte> block = stackalloc byte[BlockSize];
            image.Read(0, block);
            uint start = 0;
            if (block[510] == 0x55 && block[511] == 0xAA && block[446 + 4] is 0x01 or 0x04 or 0x06 or 0x0B or 0x0C or 0x0E && !IsBoot(block))
            {
                start = BinaryPrimitives.ReadUInt32LittleEndian(block[(446 + 8)..]);
                image.Read(start, block);
            }

            if (!IsBoot(block))
            {
                return null;
            }

            int sectorsPerCluster = block[13];
            int reserved = BinaryPrimitives.ReadUInt16LittleEndian(block[14..]);
            int fats = block[16];
            int rootEntries = BinaryPrimitives.ReadUInt16LittleEndian(block[17..]);
            uint total = BinaryPrimitives.ReadUInt16LittleEndian(block[19..]);
            if (total == 0)
            {
                total = BinaryPrimitives.ReadUInt32LittleEndian(block[32..]);
            }

            uint fatSectors = BinaryPrimitives.ReadUInt16LittleEndian(block[22..]);
            uint rootCluster = 0;
            if (fatSectors == 0)
            {
                fatSectors = BinaryPrimitives.ReadUInt32LittleEndian(block[36..]);
                rootCluster = BinaryPrimitives.ReadUInt32LittleEndian(block[44..]);
            }

            return sectorsPerCluster == 0 || fats == 0 ? null : new Volume(image, start, sectorsPerCluster, reserved, fats, rootEntries, fatSectors, total, rootCluster);
        }

        private static bool IsBoot(ReadOnlySpan<byte> block) =>
            block[510] == 0x55 && block[511] == 0xAA && BinaryPrimitives.ReadUInt16LittleEndian(block[11..]) == BlockSize && block[13] != 0 && (block[0] is 0xEB or 0xE9);

        public uint ClusterSector(uint cluster) => DataStart + ((cluster - 2) * (uint)SectorsPerCluster);

        public uint Fat(uint cluster)
        {
            Span<byte> block = stackalloc byte[BlockSize];
            uint offset = IsFat32 ? cluster * 4 : cluster * 2;
            Image.Read(FatStart + (offset / BlockSize), block);
            int at = (int)(offset % BlockSize);
            return IsFat32 ? BinaryPrimitives.ReadUInt32LittleEndian(block[at..]) & 0x0FFFFFFF : BinaryPrimitives.ReadUInt16LittleEndian(block[at..]);
        }

        public void SetFat(uint cluster, uint value)
        {
            for (int fat = 0; fat < Fats; fat++)
            {
                uint offset = cluster * 2;
                uint sector = FatStart + ((uint)fat * FatSectorCount) + (offset / BlockSize);
                Span<byte> block = Image.Block(sector);
                BinaryPrimitives.WriteUInt16LittleEndian(block[(int)(offset % BlockSize)..], (ushort)value);
            }
        }

        private bool IsEnd(uint cluster) => cluster < 2 || cluster >= (IsFat32 ? 0x0FFFFFF8u : 0xFFF8u) || cluster > Clusters + 1;

        private IEnumerable<uint> Chain(uint cluster)
        {
            for (int guard = 0; !IsEnd(cluster) && guard <= Clusters; guard++)
            {
                yield return cluster;
                cluster = Fat(cluster);
            }
        }

        private byte[] ReadChain(uint cluster, long length)
        {
            var data = new byte[length];
            Span<byte> block = stackalloc byte[BlockSize];
            long done = 0;
            foreach (uint c in Chain(cluster))
            {
                for (int s = 0; s < SectorsPerCluster && done < length; s++)
                {
                    Image.Read(ClusterSector(c) + (uint)s, block);
                    int count = (int)Math.Min(BlockSize, length - done);
                    block[..count].CopyTo(data.AsSpan((int)done));
                    done += count;
                }

                if (done >= length)
                {
                    break;
                }
            }

            return data;
        }

        private IEnumerable<byte[]> Records(uint folder)
        {
            var block = new byte[BlockSize];
            IEnumerable<uint> sectors = folder == 0 && !IsFat32
                ? Enumerable.Range(0, (int)(DataStart - RootSector)).Select(i => RootSector + (uint)i)
                : Chain(folder).SelectMany(c => Enumerable.Range(0, SectorsPerCluster).Select(s => ClusterSector(c) + (uint)s));
            foreach (uint sector in sectors)
            {
                Image.Read(sector, block);
                for (int i = 0; i < BlockSize; i += 32)
                {
                    if (block[i] == 0)
                    {
                        yield break;
                    }

                    yield return block[i..(i + 32)];
                }
            }
        }

        public void Walk(uint folder, string path, List<CardFile> files, int depth)
        {
            if (depth > 32)
            {
                return;
            }

            var longName = new SortedDictionary<int, string>();
            byte checksum = 0;
            foreach (byte[] record in Records(folder))
            {
                if (record[0] == 0xE5)
                {
                    longName.Clear();
                    continue;
                }

                if (record[11] == 0x0F)
                {
                    int order = record[0] & 0x1F;
                    checksum = record[13];
                    longName[order] = LongPart(record);
                    continue;
                }

                string shortName = ShortText(record);
                string name = longName.Count > 0 && checksum == Checksum(record) ? string.Concat(longName.Values) : shortName;
                longName.Clear();
                if ((record[11] & 0x08) != 0 || shortName is "." or "..")
                {
                    continue;
                }

                uint cluster = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(26)) | ((uint)BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(20)) << 16);
                string full = path.Length == 0 ? name : path + "/" + name;
                if ((record[11] & 0x10) != 0)
                {
                    files.Add(new CardFile(full, null));
                    Walk(cluster, full, files, depth + 1);
                }
                else
                {
                    uint size = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(28));
                    files.Add(new CardFile(full, size == 0 ? [] : ReadChain(cluster, Math.Min(size, Capacity))));
                }
            }
        }

        private static string LongPart(byte[] record)
        {
            int[] offsets = [1, 3, 5, 7, 9, 14, 16, 18, 20, 22, 24, 28, 30];
            var text = new StringBuilder();
            foreach (int offset in offsets)
            {
                char c = (char)BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(offset));
                if (c is '\0' or '￿')
                {
                    break;
                }

                text.Append(c);
            }

            return text.ToString();
        }

        private static string ShortText(byte[] record)
        {
            string stem = Encoding.ASCII.GetString(record, 0, 8).TrimEnd();
            string extension = Encoding.ASCII.GetString(record, 8, 3).TrimEnd();
            if ((record[12] & 0x08) != 0)
            {
                stem = stem.ToLowerInvariant();
            }

            if ((record[12] & 0x10) != 0)
            {
                extension = extension.ToLowerInvariant();
            }

            return extension.Length == 0 ? stem : stem + "." + extension;
        }
    }
}
