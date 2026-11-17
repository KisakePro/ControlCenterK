using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ControlCenterK
{
    /// <summary>
    /// Destination des images (pixels BGRA de haut en bas) et du son (PCM 16 bits stéréo 48 kHz).
    /// Horodatages en unités de 100 ns depuis le début de l'enregistrement.
    /// </summary>
    interface IVideoSink : IDisposable
    {
        void Write(byte[] bgra, long time);
        void WriteAudio(short[] pcm, int frames, long time);
    }

    /// <summary>
    /// MP4 (H.264 + AAC) et WMV (WMV + WMA) par Media Foundation, intégré à Windows : aucun codec à installer,
    /// encodage par la carte graphique quand elle le permet. Tous les appels se font sur le même thread.
    /// </summary>
    sealed class MfWriter : IVideoSink
    {
        #region Interop Media Foundation

        // Seul l'ordre des méthodes compte (table virtuelle COM) : les emplacements inutilisés sont des bouche-trous.
        [ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFAttributes
        {
            void GetItem(); void GetItemType(); void CompareItem(); void Compare();
            [PreserveSig] int GetUINT32([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, out int value);
            void GetUINT64(); void GetDouble();
            void GetGUID(); void GetStringLength(); void GetString(); void GetAllocatedString(); void GetBlobSize(); void GetBlob();
            void GetAllocatedBlob(); void GetUnknown(); void SetItem(); void DeleteItem(); void DeleteAllItems();
            void SetUINT32([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, int value);
            void SetUINT64([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, long value);
            void SetDouble();
            void SetGUID([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, [In, MarshalAs(UnmanagedType.LPStruct)] Guid value);
            void SetString(); void SetBlob(); void SetUnknown(); void LockStore(); void UnlockStore(); void GetCount(); void GetItemByIndex(); void CopyAllItems();
        }

        [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFSample
        {
            void GetItem(); void GetItemType(); void CompareItem(); void Compare(); void GetUINT32(); void GetUINT64(); void GetDouble();
            void GetGUID(); void GetStringLength(); void GetString(); void GetAllocatedString(); void GetBlobSize(); void GetBlob();
            void GetAllocatedBlob(); void GetUnknown(); void SetItem(); void DeleteItem(); void DeleteAllItems(); void SetUINT32(); void SetUINT64();
            void SetDouble(); void SetGUID(); void SetString(); void SetBlob(); void SetUnknown(); void LockStore(); void UnlockStore();
            void GetCount(); void GetItemByIndex(); void CopyAllItems();
            void GetSampleFlags(); void SetSampleFlags();
            void GetSampleTime();
            void SetSampleTime(long time);
            void GetSampleDuration();
            void SetSampleDuration(long duration);
            void GetBufferCount(); void GetBufferByIndex(); void ConvertToContiguousBuffer();
            void AddBuffer(IMFMediaBuffer buffer);
        }

        [ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFMediaBuffer
        {
            void Lock(out IntPtr data, out int max, out int current);
            void Unlock();
            void GetCurrentLength();
            void SetCurrentLength(int length);
        }

        [ComImport, Guid("5BC8A76B-869A-46a3-9B03-FA218A66AEBE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFCollection
        {
            void GetElementCount(out int count);
            void GetElement(int index, [MarshalAs(UnmanagedType.IUnknown)] out object element);
        }

        [ComImport, Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFSinkWriter
        {
            void AddStream(IMFAttributes type, out int stream);
            void SetInputMediaType(int stream, IMFAttributes type, IMFAttributes encodingParameters);
            void BeginWriting();
            void WriteSample(int stream, IMFSample sample);
            void SendStreamTick(); void PlaceMarker(); void NotifyEndOfSegment(); void Flush();
            void Finish(); // Finalize
        }

        [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)] static extern void MFStartup(int version, int flags);
        [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)] static extern void MFShutdown();
        [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)] static extern void MFCreateMediaType(out IMFAttributes type);
        [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)] static extern void MFCreateAttributes(out IMFAttributes attrs, int size);
        [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)] static extern void MFCreateSample(out IMFSample sample);
        [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)] static extern void MFCreateMemoryBuffer(int length, out IMFMediaBuffer buffer);
        [DllImport("mfreadwrite.dll", ExactSpelling = true, PreserveSig = false)]
        static extern void MFCreateSinkWriterFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, IntPtr byteStream, IMFAttributes attrs, out IMFSinkWriter writer);
        [DllImport("mf.dll", ExactSpelling = true, PreserveSig = false)]
        static extern void MFTranscodeGetAudioOutputAvailableTypes([In, MarshalAs(UnmanagedType.LPStruct)] Guid subtype, int flags, IMFAttributes config, out IMFCollection types);

        static readonly Guid MajorType = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f"), SubType = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5"),
            Video = new Guid("73646976-0000-0010-8000-00AA00389B71"), H264 = new Guid("34363248-0000-0010-8000-00AA00389B71"),
            Wmv3 = new Guid("33564D57-0000-0010-8000-00AA00389B71"), Rgb32 = new Guid("00000016-0000-0010-8000-00AA00389B71"),
            Bitrate = new Guid("20332624-fb0d-4d9e-bd0d-cbf6786c102e"), Interlace = new Guid("e2724bb8-e676-4806-b4b2-a8d6efb44ccd"),
            FrameSize = new Guid("1652c33d-d6b2-4012-b834-72030849a37d"), FrameRate = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0"),
            Aspect = new Guid("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6"), Stride = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6"),
            HardwareTransforms = new Guid("a634a91c-822b-41b9-a494-4de4643612b0"),
            Audio = new Guid("73647561-0000-0010-8000-00AA00389B71"), Pcm = new Guid("00000001-0000-0010-8000-00AA00389B71"),
            Aac = new Guid("00001610-0000-0010-8000-00AA00389B71"), Wma = new Guid("00000161-0000-0010-8000-00AA00389B71"),
            Channels = new Guid("37e48bf5-645e-4c5b-89de-ada9e29b696a"), SampleRate = new Guid("5faeeae7-0290-4c31-9e8a-c534f68d9dba"),
            BytesPerSecond = new Guid("1aab75c8-cfef-451c-ab95-ac034b8e1731"), BlockAlign = new Guid("322de230-9eeb-43bd-ab7a-ff412251541d"),
            BitsPerSample = new Guid("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");

        #endregion

        readonly IMFSinkWriter writer;
        readonly int stream, length, audioStream = -1;
        readonly long duration;

        /// <summary>Son impossible à encoder (aucun format adapté) : la vidéo est enregistrée sans son.</summary>
        public string AudioError;

        public MfWriter(string path, int w, int h, int fps, int bitrate, bool wmv, bool audio)
        {
            MFStartup(0x20070, 0);
            IMFAttributes attrs, output, input;
            MFCreateAttributes(out attrs, 1);
            attrs.SetUINT32(HardwareTransforms, 1);
            MFCreateSinkWriterFromURL(path, IntPtr.Zero, attrs, out writer);
            long size = (long)w << 32 | (uint)h, rate = (long)fps << 32 | 1, square = 1L << 32 | 1;

            MFCreateMediaType(out output);
            output.SetGUID(MajorType, Video);
            output.SetGUID(SubType, wmv ? Wmv3 : H264);
            output.SetUINT32(Bitrate, bitrate);
            output.SetUINT32(Interlace, 2); // progressif
            output.SetUINT64(FrameSize, size);
            output.SetUINT64(FrameRate, rate);
            output.SetUINT64(Aspect, square);
            writer.AddStream(output, out stream);

            MFCreateMediaType(out input);
            input.SetGUID(MajorType, Video);
            input.SetGUID(SubType, Rgb32);
            input.SetUINT32(Interlace, 2);
            input.SetUINT32(Stride, w * 4); // positif : image de haut en bas
            input.SetUINT64(FrameSize, size);
            input.SetUINT64(FrameRate, rate);
            input.SetUINT64(Aspect, square);
            writer.SetInputMediaType(stream, input, null);
            Marshal.ReleaseComObject(attrs);
            Marshal.ReleaseComObject(output);
            Marshal.ReleaseComObject(input);

            if (audio)
            {
                try { audioStream = AddAudio(wmv ? Wma : Aac); }
                catch (Exception e) { AudioError = "son non enregistré : " + e.Message; }
            }
            writer.BeginWriting();
            length = w * h * 4;
            duration = 10000000L / fps;
        }

        /// <summary>
        /// L'encodeur n'accepte que les formats de sa liste : on y prend un format 48 kHz stéréo à environ 192 kbit/s,
        /// puis on lui donne du PCM 16 bits.
        /// </summary>
        int AddAudio(Guid codec)
        {
            IMFCollection types;
            MFTranscodeGetAudioOutputAvailableTypes(codec, 0x2F /* tous sauf « champ d'utilisation » */, null, out types);
            IMFAttributes best = null;
            int bestRate = -1;
            try
            {
                int n;
                types.GetElementCount(out n);
                for (int i = 0; i < n; i++)
                {
                    object o;
                    types.GetElement(i, out o);
                    var t = (IMFAttributes)o;
                    int hz, ch, bps;
                    if (t.GetUINT32(SampleRate, out hz) != 0 || t.GetUINT32(Channels, out ch) != 0 || t.GetUINT32(BytesPerSecond, out bps) != 0
                        || hz != AudioSource.Rate || ch != 2)
                    { Marshal.ReleaseComObject(t); continue; }
                    // le plus haut débit ne dépassant pas 24 000 octets/s (192 kbit/s), sinon le plus bas au-dessus
                    bool better = best == null || (bps <= 24000 ? bestRate > 24000 || bps > bestRate : bestRate > 24000 && bps < bestRate);
                    if (better) { if (best != null) Marshal.ReleaseComObject(best); best = t; bestRate = bps; }
                    else Marshal.ReleaseComObject(t);
                }
            }
            finally { Marshal.ReleaseComObject(types); }
            if (best == null) throw new NotSupportedException("aucun format 48 kHz stéréo");
            int s;
            IMFAttributes input;
            try
            {
                writer.AddStream(best, out s);
                MFCreateMediaType(out input);
                input.SetGUID(MajorType, Audio);
                input.SetGUID(SubType, Pcm);
                input.SetUINT32(BitsPerSample, 16);
                input.SetUINT32(SampleRate, AudioSource.Rate);
                input.SetUINT32(Channels, 2);
                input.SetUINT32(BlockAlign, 4);
                input.SetUINT32(BytesPerSecond, AudioSource.Rate * 4);
                writer.SetInputMediaType(s, input, null);
                Marshal.ReleaseComObject(input);
            }
            finally { Marshal.ReleaseComObject(best); }
            return s;
        }

        void Send(int s, IntPtr src, byte[] bytes, short[] shorts, int len, long time, long dur)
        {
            IMFMediaBuffer buffer;
            IMFSample sample;
            MFCreateMemoryBuffer(len, out buffer);
            IntPtr data;
            int max, cur;
            buffer.Lock(out data, out max, out cur);
            if (bytes != null) Marshal.Copy(bytes, 0, data, len);
            else Marshal.Copy(shorts, 0, data, len / 2);
            buffer.Unlock();
            buffer.SetCurrentLength(len);
            MFCreateSample(out sample);
            sample.AddBuffer(buffer);
            sample.SetSampleTime(time);
            sample.SetSampleDuration(dur);
            writer.WriteSample(s, sample);
            Marshal.ReleaseComObject(sample); // sinon la mémoire grimpe à chaque image
            Marshal.ReleaseComObject(buffer);
        }

        public void Write(byte[] bgra, long time) { Send(stream, IntPtr.Zero, bgra, null, length, time, duration); }

        public void WriteAudio(short[] pcm, int frames, long time)
        {
            if (audioStream < 0) return;
            Send(audioStream, IntPtr.Zero, null, pcm, frames * 4, time, frames * 10000000L / AudioSource.Rate);
        }

        public void Dispose()
        {
            try { writer.Finish(); }
            finally
            {
                Marshal.ReleaseComObject(writer);
                MFShutdown();
            }
        }
    }

    /// <summary>
    /// AVI en Motion JPEG (+ son PCM) : chaque image est un JPEG, lu par tous les lecteurs et logiciels de montage.
    /// Débit fixe : une image manquée est répétée pour garder la durée exacte. Limité à 4 Go par le format AVI 1.0.
    /// </summary>
    sealed class AviWriter : IVideoSink
    {
        public const long MaxBytes = 3900L * 1024 * 1024;

        readonly FileStream fs;
        readonly BinaryWriter bw;
        readonly int w, h, fps;
        readonly bool audio;
        readonly ImageCodecInfo jpeg;
        readonly EncoderParameters quality;
        readonly List<KeyValuePair<string, KeyValuePair<long, int>>> index = new List<KeyValuePair<string, KeyValuePair<long, int>>>();
        readonly long moviList;
        byte[] last;
        int maxChunk, frames;
        long audioBytes;

        public long Length { get { return fs.Length; } }

        public AviWriter(string path, int w, int h, int fps, int quality, bool audio)
        {
            this.w = w; this.h = h; this.fps = fps; this.audio = audio;
            foreach (var c in ImageCodecInfo.GetImageEncoders()) if (c.FormatID == ImageFormat.Jpeg.Guid) jpeg = c;
            this.quality = new EncoderParameters(1);
            this.quality.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
            fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 1 << 20);
            bw = new BinaryWriter(fs);
            WriteHeaders();
            moviList = fs.Position;
            Four("LIST"); bw.Write(0); Four("movi");
        }

        void Four(string s) { bw.Write(Encoding.ASCII.GetBytes(s)); }

        const int VideoStrl = 4 + 8 + 56 + 8 + 40, AudioStrl = 4 + 8 + 56 + 8 + 18;

        // en-têtes de taille fixe : réécrits à la fin avec les nombres d'images et d'échantillons
        void WriteHeaders()
        {
            fs.Position = 0;
            Four("RIFF"); bw.Write(0); Four("AVI ");
            Four("LIST"); bw.Write(4 + 8 + 56 + 8 + VideoStrl + (audio ? 8 + AudioStrl : 0)); Four("hdrl");
            Four("avih"); bw.Write(56);
            bw.Write(1000000 / fps); bw.Write(0); bw.Write(0); bw.Write(0x10 /* AVIF_HASINDEX */);
            bw.Write(frames); bw.Write(0); bw.Write(audio ? 2 : 1); bw.Write(maxChunk); bw.Write(w); bw.Write(h);
            bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);

            Four("LIST"); bw.Write(VideoStrl); Four("strl");
            Four("strh"); bw.Write(56);
            Four("vids"); Four("MJPG"); bw.Write(0); bw.Write((short)0); bw.Write((short)0); bw.Write(0);
            bw.Write(1); bw.Write(fps); bw.Write(0); bw.Write(frames); bw.Write(maxChunk); bw.Write(-1); bw.Write(0);
            bw.Write((short)0); bw.Write((short)0); bw.Write((short)w); bw.Write((short)h);
            Four("strf"); bw.Write(40);
            bw.Write(40); bw.Write(w); bw.Write(h); bw.Write((short)1); bw.Write((short)24); Four("MJPG");
            bw.Write(w * h * 3); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);

            if (!audio) return;
            Four("LIST"); bw.Write(AudioStrl); Four("strl");
            Four("strh"); bw.Write(56);
            Four("auds"); bw.Write(0); bw.Write(0); bw.Write((short)0); bw.Write((short)0); bw.Write(0);
            bw.Write(4); bw.Write(AudioSource.Rate * 4); bw.Write(0); bw.Write((int)(audioBytes / 4)); bw.Write(AudioSource.Rate * 4); bw.Write(-1); bw.Write(4);
            bw.Write((short)0); bw.Write((short)0); bw.Write((short)0); bw.Write((short)0);
            Four("strf"); bw.Write(18);
            bw.Write((short)1 /* PCM */); bw.Write((short)2); bw.Write(AudioSource.Rate); bw.Write(AudioSource.Rate * 4); bw.Write((short)4); bw.Write((short)16); bw.Write((short)0);
        }

        public unsafe void Write(byte[] bgra, long time)
        {
            byte[] frame;
            fixed (byte* p = bgra)
            using (var bmp = new Bitmap(w, h, w * 4, PixelFormat.Format32bppRgb, (IntPtr)p))
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, jpeg, quality);
                frame = ms.ToArray();
            }
            // horodatage → numéro d'image : on comble les images manquées en répétant la précédente
            long target = time * fps / 10000000L;
            while (last != null && frames < target) { Chunk("00dc", last, last.Length); frames++; }
            Chunk("00dc", frame, frame.Length);
            frames++;
            last = frame;
        }

        public void WriteAudio(short[] pcm, int count, long time)
        {
            if (!audio) return;
            var b = new byte[count * 4];
            Buffer.BlockCopy(pcm, 0, b, 0, b.Length);
            Chunk("01wb", b, b.Length);
            audioBytes += b.Length;
        }

        void Chunk(string id, byte[] data, int len)
        {
            index.Add(new KeyValuePair<string, KeyValuePair<long, int>>(id, new KeyValuePair<long, int>(fs.Position - (moviList + 8), len)));
            Four(id); bw.Write(len); bw.Write(data, 0, len);
            if ((len & 1) != 0) bw.Write((byte)0);
            maxChunk = Math.Max(maxChunk, len);
        }

        public void Dispose()
        {
            long end = fs.Position;
            Four("idx1"); bw.Write(index.Count * 16);
            foreach (var e in index) { Four(e.Key); bw.Write(0x10 /* AVIIF_KEYFRAME */); bw.Write((int)e.Value.Key); bw.Write(e.Value.Value); }
            long total = fs.Position;
            fs.Position = moviList + 4; bw.Write((int)(end - moviList - 8));
            WriteHeaders();
            fs.Position = 4; bw.Write((int)(total - 8));
            bw.Flush();
            fs.Dispose();
            quality.Dispose();
        }
    }
}
