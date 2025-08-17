using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace EGF_Patcher
{
    class GFXFile: IDisposable
    {
        private const int ERROR_BAD_EXE_FORMAT = 0xC1;
        private const uint RT_BITMAP = 0x00000002;
        private const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;

        private readonly string path;
        private IntPtr handle;

        public GFXFile(String path)
        {
            this.path = path;
            handle = NativeMethods.BeginUpdateResource(path, false);
 
            if (handle == IntPtr.Zero)
                throw new Exception("Failed to open GFX file: " + OSErrorString());
        }

        public void Update(int id, Bitmap bitmap)
        {
            EnumerateAndDeleteResourceLanguages(id);

            byte[] bytes;
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Bmp);
                bytes = stream.ToArray();
            }

            GCHandle gcHandle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            IntPtr data = Marshal.UnsafeAddrOfPinnedArrayElement(bytes, 14);

            Boolean error = !NativeMethods.UpdateResource(
                handle,
                (IntPtr)RT_BITMAP,
                (IntPtr)id,
                0,
                data,
                (uint)(bytes.Length - 14)
            );

            gcHandle.Free();

            if (error)
            {
                throw new Exception("Failed to update resource: " + OSErrorString());
            }
        }

        private void EnumerateAndDeleteResourceLanguages(int id)
        {
            IntPtr hModule = NativeMethods.LoadLibraryEx(
                path,
                IntPtr.Zero,
                LOAD_LIBRARY_AS_DATAFILE
            );

            if (hModule == IntPtr.Zero)
                return;

            var langs = new List<ushort>();
            bool EnumLangProc(IntPtr _, IntPtr __, IntPtr ___, ushort lang, IntPtr ____)
            {
                if (lang != 0)
                    langs.Add(lang);
                return true;
            }

            NativeMethods.EnumResourceLanguages(
                hModule,
                (IntPtr)RT_BITMAP,
                (IntPtr)id,
                new NativeMethods.EnumResLangProc(EnumLangProc),
                IntPtr.Zero
            );

            NativeMethods.FreeLibrary(hModule);

            foreach (var lang in langs)
            {
                NativeMethods.UpdateResource(
                    handle,
                    (IntPtr)RT_BITMAP,
                    (IntPtr)id,
                    lang,
                    IntPtr.Zero,
                    0
                );
            }

        }

        public void Commit()
        {
            if (handle != IntPtr.Zero)
            {
                BackupOriginalFile();

                if (!NativeMethods.EndUpdateResource(handle, false))
                {
                    throw new Exception("Failed to save GFX file: " + OSErrorString());
                }

                handle = IntPtr.Zero;
            }
        }

        private void BackupOriginalFile()
        {
            String backupPath = Path.GetDirectoryName(path) +
                                Path.DirectorySeparatorChar +
                                Path.GetFileNameWithoutExtension(path) +
                                "_original" +
                                Path.GetExtension(path);

            try
            {
                File.Copy(path, backupPath);
            }
            catch
            {
                // Oh well, guess we can't backup the EGF file.
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (handle != IntPtr.Zero)
            {
                NativeMethods.EndUpdateResource(handle, false);
                handle = IntPtr.Zero;

                if (disposing)
                {
                    GC.SuppressFinalize(this);
                }
            }
        }

        public void Dispose()
        {
            Dispose(false);
        }

        ~GFXFile()
        {
            Dispose(false);
        }

        private String OSErrorString()
        {
            int errorCode = Marshal.GetLastWin32Error();
            if (errorCode == ERROR_BAD_EXE_FORMAT)
            {
                return $"{Path.GetFileName(path)} is not a valid EGF file.";
            }

            return new Win32Exception(errorCode).Message;
        }
    }
}

