using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NetInspector.Core;

public sealed record StorageMediaInfo(string Kind, bool CanOverwrite, bool TrimSupported, string Evidence);

public static class StorageMedia
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);

    public static StorageMediaInfo Classify(int? bus, bool? seek, bool? trim)
    {
        // RAID, Storage Spaces, virtual, network and unknown controllers cannot establish physical media safely.
        if (bus is not (1 or 3 or 10 or 11 or 17 or 19) || seek == null)
            return new("Não identificado", false, trim == true, "Controlador virtual, composto ou sem identificação física confiável.");
        if (seek == true && bus != 17)
            return new("HDD", true, trim == true, "Dispositivo físico informa penalidade de busca mecânica.");
        if (seek == false)
            return new("SSD / memória flash", false, trim == true, "Dispositivo físico sem penalidade de busca; TRIM " + (trim == true ? "suportado." : "não confirmado."));
        return new("Não identificado", false, false, "Informações do controlador são inconsistentes.");
    }

    public static StorageMediaInfo Detect(string volume)
    {
        if (volume.Length != 2 || !char.IsAsciiLetter(volume[0]) || volume[1] != ':') throw new ArgumentException("Unidade inválida.");
        using var handle = CreateFile(@"\\.\" + volume, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        byte[]? Query(int property)
        {
            if (handle.IsInvalid) return null;
            var input = new byte[12]; BitConverter.GetBytes(property).CopyTo(input, 0);
            var output = new byte[1024];
            return DeviceIoControl(handle, 0x2D1400, input, input.Length, output, output.Length, out var size, IntPtr.Zero) && size >= (property == 0 ? 32 : 9) ? output : null;
        }
        var descriptor = Query(0); var seek = Query(7); var trim = Query(8); var provisioning = Query(11);
        var extents = new byte[1024];
        if (handle.IsInvalid || !DeviceIoControl(handle, 0x560000, [], 0, extents, extents.Length, out var extentSize, IntPtr.Zero) || extentSize < 4 || BitConverter.ToInt32(extents, 0) != 1 || (provisioning != null && (provisioning[8] & 1) != 0))
            return new("Não identificado", false, trim != null && trim[8] != 0, "Volume composto, provisionado ou sem mapeamento físico confirmado.");
        return Classify(descriptor == null ? null : BitConverter.ToInt32(descriptor, 28), seek == null ? null : seek[8] != 0, trim == null ? null : trim[8] != 0);
    }
}
