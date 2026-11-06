using System;
using System.IO;
using System.Runtime.InteropServices;
using Com = System.Runtime.InteropServices.ComTypes;

namespace ScreenLine
{
    // Read only media explicitly supported by the provider. WPF's custom-format path
    // requests HGLOBAL even when a WinRT DataPackage offers the image as IStream.
    internal static class ClipboardTransfer
    {
        const int Limit = 64 * 1024 * 1024;
        [DllImport("ole32.dll")] static extern int OleGetClipboard(out Com.IDataObject data);
        [DllImport("ole32.dll")] static extern void ReleaseStgMedium(ref Com.STGMEDIUM medium);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormat(string name);
        [DllImport("kernel32.dll")] static extern UIntPtr GlobalSize(IntPtr memory);
        [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr memory);
        [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr memory);

        internal static readonly Com.TYMED[] Media = { Com.TYMED.TYMED_ISTREAM, Com.TYMED.TYMED_HGLOBAL };

        internal static byte[] ReadEncoded(uint sequence, out string extension, Action<Exception> log)
        {
            extension = null;
            Com.IDataObject data = null;
            try
            {
                Marshal.ThrowExceptionForHR(OleGetClipboard(out data));
                foreach (var format in ClipboardRules.EncodedFormats)
                {
                    foreach (var kind in Media)
                    {
                        if (WindowsNative.GetClipboardSequenceNumber() != sequence) return null;
                        var request = new Com.FORMATETC { cfFormat = unchecked((short)RegisterClipboardFormat(format.Key)),
                            dwAspect = Com.DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = kind };
                        var medium = new Com.STGMEDIUM();
                        bool obtained = false;
                        try
                        {
                            if (data.QueryGetData(ref request) != 0) continue;
                            data.GetData(ref request, out medium); obtained = true;
                            byte[] bytes = ReadMedium(medium);
                            if (bytes == null || bytes.Length == 0) continue;
                            ValidateEncoded(bytes, format.Value);
                            extension = format.Value; return bytes;
                        }
                        catch (ExternalException error) { if (log != null) log(new IOException("Clipboard format=" + format.Key + "; medium=" + kind + "; fallback", error)); }
                        catch (InvalidDataException error) { if (log != null) log(new IOException("Clipboard image is incomplete or corrupt; trying alternate representation", error)); }
                        catch (IOException error) { if (log != null) log(new IOException("Clipboard format=" + format.Key + "; medium=" + kind + "; fallback", error)); }
                        finally { if (obtained) ReleaseStgMedium(ref medium); }
                    }
                }
            }
            catch (ExternalException error) { if (log != null) log(new IOException("Clipboard native transfer failed; trying bitmap", error)); }
            finally { if (data != null && Marshal.IsComObject(data)) Marshal.ReleaseComObject(data); }
            return null;
        }

        // WIC can decode a damaged PNG partially without throwing. Reject corrupt chunks
        // before either association or persistence, and try another clipboard representation.
        internal static void ValidateEncoded(byte[] bytes, string extension)
        {
            if (extension != ".png") return;
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            if (bytes == null || bytes.Length < 33) throw new InvalidDataException("Incomplete clipboard PNG");
            for (int i = 0; i < 8; i++) if (bytes[i] != signature[i]) throw new InvalidDataException("Invalid clipboard PNG signature");
            bool header = false, image = false;
            int position = 8;
            while (position <= bytes.Length - 12)
            {
                uint size = BigEndian(bytes, position);
                if (size > (uint)(bytes.Length - position - 12)) throw new InvalidDataException("Truncated clipboard PNG chunk");
                int length = (int)size;
                uint crc = 0xffffffff;
                for (int i = position + 4; i < position + 8 + length; i++) {
                    crc = (crc >> 8) ^ CrcTable[(crc ^ bytes[i]) & 255];
                }
                if ((crc ^ 0xffffffff) != BigEndian(bytes, position + 8 + length)) throw new InvalidDataException("Clipboard PNG chunk checksum failed");
                uint type = BigEndian(bytes, position + 4);
                if (!header) { if (type != 0x49484452 || length != 13) throw new InvalidDataException("Missing PNG header"); header = true; }
                if (type == 0x49444154) image = true;
                if (type == 0x49454e44) {
                    if (length != 0 || !image) throw new InvalidDataException("Invalid PNG ending");
                    return; // HGLOBAL allocation may have padding after IEND.
                }
                position += length + 12;
            }
            throw new InvalidDataException("Missing clipboard PNG ending");
        }
        static readonly uint[] CrcTable = MakeCrcTable();
        static uint[] MakeCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++) {
                uint value = i;
                for (int bit = 0; bit < 8; bit++) value = (value >> 1) ^ ((value & 1) != 0 ? 0xedb88320u : 0u);
                table[i] = value;
            }
            return table;
        }
        static uint BigEndian(byte[] bytes, int index)
        {
            return ((uint)bytes[index] << 24) | ((uint)bytes[index + 1] << 16) | ((uint)bytes[index + 2] << 8) | bytes[index + 3];
        }

        internal static byte[] ReadMedium(Com.STGMEDIUM medium)
        {
            if (medium.unionmember == IntPtr.Zero) return null;
            if (medium.tymed == Com.TYMED.TYMED_HGLOBAL)
            {
                ulong size = GlobalSize(medium.unionmember).ToUInt64();
                if (size == 0 || size > Limit) throw new InvalidDataException("Clipboard image exceeds size limit");
                IntPtr address = GlobalLock(medium.unionmember);
                if (address == IntPtr.Zero) throw new IOException("Cannot lock clipboard image");
                try { var bytes = new byte[(int)size]; Marshal.Copy(address, bytes, 0, bytes.Length); return bytes; }
                finally { GlobalUnlock(medium.unionmember); }
            }
            if (medium.tymed != Com.TYMED.TYMED_ISTREAM) return null;
            object wrapper = Marshal.GetObjectForIUnknown(medium.unionmember);
            IntPtr count = Marshal.AllocCoTaskMem(sizeof(int));
            try
            {
                var stream = (Com.IStream)wrapper;
                stream.Seek(0, 0, IntPtr.Zero);
                using (var memory = new MemoryStream())
                {
                    var buffer = new byte[81920];
                    while (true)
                    {
                        Marshal.WriteInt32(count, 0); stream.Read(buffer, buffer.Length, count);
                        int read = Marshal.ReadInt32(count);
                        if (read == 0) break;
                        if (read < 0 || read > buffer.Length || memory.Length + read > Limit) throw new InvalidDataException("Invalid clipboard stream length");
                        memory.Write(buffer, 0, read);
                    }
                    return memory.ToArray();
                }
            }
            finally { Marshal.FreeCoTaskMem(count); if (Marshal.IsComObject(wrapper)) Marshal.ReleaseComObject(wrapper); }
        }
    }
}
