<#
Copies an icon group out of a Windows executable and writes it as a .ico with every size intact.

The launcher wears the game's own icon, so platform.ico is taken straight from App\ago.exe.
ExtractAssociatedIcon would give back a single 32x32 frame; this reads the RT_GROUP_ICON directory
and the RT_ICON images it points at, which is the whole set.

  .\extract-icon.ps1 -Source D:\FGOA\App\ago.exe -Out ..\platform.ico
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Out,
    [int]$GroupIndex = 0
)

$ErrorActionPreference = 'Stop'

Add-Type @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

public static class IconGroups
{
    private const int RT_ICON = 3;
    private const int RT_GROUP_ICON = 14;
    private const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumResNameProc callback, IntPtr parameter);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LockResource(IntPtr data);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr module, IntPtr resource);

    private delegate bool EnumResNameProc(IntPtr module, IntPtr type, IntPtr name, IntPtr parameter);

    private static byte[] Read(IntPtr module, IntPtr name, int type)
    {
        IntPtr found = FindResource(module, name, (IntPtr)type);
        if (found == IntPtr.Zero) throw new IOException("resource not found");
        uint size = SizeofResource(module, found);
        IntPtr handle = LoadResource(module, found);
        IntPtr address = LockResource(handle);
        byte[] bytes = new byte[size];
        Marshal.Copy(address, bytes, 0, (int)size);
        return bytes;
    }

    /// <summary>Writes icon group number <paramref name="index"/> of <paramref name="source"/> to an .ico file.</summary>
    public static int Write(string source, string destination, int index)
    {
        IntPtr module = LoadLibraryEx(source, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE);
        if (module == IntPtr.Zero) throw new IOException("could not open " + source);
        try
        {
            List<IntPtr> groups = new List<IntPtr>();
            EnumResourceNames(module, (IntPtr)RT_GROUP_ICON, delegate(IntPtr m, IntPtr t, IntPtr n, IntPtr p)
            {
                groups.Add(n);
                return true;
            }, IntPtr.Zero);
            if (groups.Count <= index) throw new IOException(source + " has " + groups.Count + " icon groups");

            byte[] directory = Read(module, groups[index], RT_GROUP_ICON);
            int count = BitConverter.ToUInt16(directory, 4);
            byte[][] images = new byte[count][];
            for (int i = 0; i < count; i++)
            {
                int id = BitConverter.ToUInt16(directory, 6 + i * 14 + 12);
                images[i] = Read(module, (IntPtr)id, RT_ICON);
            }

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write((short)0);
                writer.Write((short)1);
                writer.Write((short)count);
                int offset = 6 + 16 * count;
                for (int i = 0; i < count; i++)
                {
                    // The group entry is 14 bytes and the file entry 16: the first 8 are the same
                    // (width, height, colours, reserved, planes, bit depth), then the group carries
                    // a size and a resource id where the file carries a size and a byte offset.
                    writer.Write(directory, 6 + i * 14, 8);
                    writer.Write(images[i].Length);
                    writer.Write(offset);
                    offset += images[i].Length;
                }
                for (int i = 0; i < count; i++) writer.Write(images[i]);
                writer.Flush();
                File.WriteAllBytes(destination, stream.ToArray());
            }
            return count;
        }
        finally
        {
            FreeLibrary(module);
        }
    }
}
'@

$target = [IO.Path]::GetFullPath((Join-Path (Get-Location) $Out))
$written = [IconGroups]::Write((Resolve-Path -LiteralPath $Source).Path, $target, $GroupIndex)
Write-Host "Wrote $target ($written sizes, $([IO.FileInfo]::new($target).Length) bytes) from $Source"
