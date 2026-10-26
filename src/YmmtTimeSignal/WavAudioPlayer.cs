using System;
using System.IO;
using System.Media;
using System.Text;
using System.Threading.Tasks;

namespace YmmtTimeSignal
{
    public class WavAudioPlayer : IDisposable
    {
        private SoundPlayer _currentPlayer;
        private readonly object _lock = new object();

        public void Play(string filePath, int volumePercent)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath) || volumePercent <= 0)
            {
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    var stream = BuildNormalizedPcm16Stream(filePath, volumePercent);
                    if (stream == null) return;

                    lock (_lock)
                    {
                        if (_currentPlayer != null)
                        {
                            try
                            {
                                _currentPlayer.Stop();
                                _currentPlayer.Dispose();
                            }
                            catch { }
                            _currentPlayer = null;
                        }

                        _currentPlayer = new SoundPlayer(stream);
                        _currentPlayer.Play();
                    }
                }
                catch
                {
                    // Ignore playback errors
                }
            });
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (_currentPlayer != null)
                {
                    try
                    {
                        _currentPlayer.Stop();
                        _currentPlayer.Dispose();
                    }
                    catch { }
                    _currentPlayer = null;
                }
            }
        }

        public static MemoryStream BuildNormalizedPcm16Stream(string filePath, int volumePercent)
        {
            byte[] fileBytes;
            using (var fs = File.OpenRead(filePath))
            {
                fileBytes = new byte[fs.Length];
                int read = 0;
                while (read < fileBytes.Length)
                {
                    int r = fs.Read(fileBytes, read, fileBytes.Length - read);
                    if (r <= 0) break;
                    read += r;
                }
            }

            if (fileBytes.Length < 12) return null;

            // Verify RIFF & WAVE
            string riff = Encoding.ASCII.GetString(fileBytes, 0, 4);
            string wave = Encoding.ASCII.GetString(fileBytes, 8, 4);
            if (riff != "RIFF" || wave != "WAVE") return null;

            int offset = 12;
            ushort audioFormat = 0;
            ushort channels = 0;
            uint sampleRate = 0;
            ushort bitsPerSample = 0;
            int dataOffset = -1;
            int dataSize = -1;

            while (offset + 8 <= fileBytes.Length)
            {
                string chunkId = Encoding.ASCII.GetString(fileBytes, offset, 4);
                uint chunkSize = BitConverter.ToUInt32(fileBytes, offset + 4);
                int chunkDataOffset = offset + 8;

                if (chunkId == "fmt " && chunkSize >= 16 && chunkDataOffset + 16 <= fileBytes.Length)
                {
                    audioFormat = BitConverter.ToUInt16(fileBytes, chunkDataOffset);
                    channels = BitConverter.ToUInt16(fileBytes, chunkDataOffset + 2);
                    sampleRate = BitConverter.ToUInt32(fileBytes, chunkDataOffset + 4);
                    bitsPerSample = BitConverter.ToUInt16(fileBytes, chunkDataOffset + 14);
                }
                else if (chunkId == "data")
                {
                    dataOffset = chunkDataOffset;
                    dataSize = (int)Math.Min((long)chunkSize, fileBytes.Length - chunkDataOffset);
                }

                long nextOffset = chunkDataOffset + chunkSize + (chunkSize % 2);
                if (nextOffset > fileBytes.Length) break;
                offset = (int)nextOffset;
            }

            if (audioFormat != 1 || channels == 0 || sampleRate == 0 || dataOffset < 0 || dataSize <= 0)
            {
                return null;
            }

            float volumeScale = Math.Max(0.0f, Math.Min(4.0f, volumePercent / 100.0f));
            bool useSoftLimiter = volumePercent >= 150;
            byte[] outputPcm;

            if (bitsPerSample == 16)
            {
                int sampleCount = dataSize / 2;
                outputPcm = new byte[sampleCount * 2];

                for (int i = 0; i < sampleCount; i++)
                {
                    int srcIdx = dataOffset + i * 2;
                    short sample = BitConverter.ToInt16(fileBytes, srcIdx);
                    short result = ApplyVolumeAndLimiter(sample, volumeScale, useSoftLimiter);
                    outputPcm[i * 2] = (byte)(result & 0xFF);
                    outputPcm[i * 2 + 1] = (byte)((result >> 8) & 0xFF);
                }
            }
            else if (bitsPerSample == 24)
            {
                int sampleCount = dataSize / 3;
                outputPcm = new byte[sampleCount * 2];

                for (int i = 0; i < sampleCount; i++)
                {
                    int srcIdx = dataOffset + i * 3;
                    int sample24 = fileBytes[srcIdx] | (fileBytes[srcIdx + 1] << 8) | ((sbyte)fileBytes[srcIdx + 2] << 16);
                    // Scale 24-bit to 16-bit
                    int sample16 = sample24 >> 8;
                    short result = ApplyVolumeAndLimiter(sample16, volumeScale, useSoftLimiter);
                    outputPcm[i * 2] = (byte)(result & 0xFF);
                    outputPcm[i * 2 + 1] = (byte)((result >> 8) & 0xFF);
                }
            }
            else
            {
                return null;
            }

            // Construct standard 44-byte RIFF WAV
            var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
            {
                uint finalPcmSize = (uint)outputPcm.Length;
                uint riffSize = 36 + finalPcmSize;
                ushort outChannels = channels;
                uint outSampleRate = sampleRate;
                ushort outBitsPerSample = 16;
                ushort outBlockAlign = (ushort)(outChannels * (outBitsPerSample / 8));
                uint outByteRate = outSampleRate * outBlockAlign;

                bw.Write(Encoding.ASCII.GetBytes("RIFF"));
                bw.Write(riffSize);
                bw.Write(Encoding.ASCII.GetBytes("WAVE"));

                bw.Write(Encoding.ASCII.GetBytes("fmt "));
                bw.Write((uint)16);
                bw.Write((ushort)1); // PCM
                bw.Write(outChannels);
                bw.Write(outSampleRate);
                bw.Write(outByteRate);
                bw.Write(outBlockAlign);
                bw.Write(outBitsPerSample);

                bw.Write(Encoding.ASCII.GetBytes("data"));
                bw.Write(finalPcmSize);
                bw.Write(outputPcm);
            }

            ms.Position = 0;
            return ms;
        }

        private static short ApplyVolumeAndLimiter(int sample16, float volumeScale, bool useSoftLimiter)
        {
            double scaled = sample16 * (double)volumeScale;

            if (!useSoftLimiter)
            {
                int val = (int)scaled;
                if (val > short.MaxValue) return short.MaxValue;
                if (val < short.MinValue) return short.MinValue;
                return (short)val;
            }

            // Soft-knee limiter above 80% (26214) when volumePercent > 100%
            const double threshold = 26214.0;
            const double headroom = 6553.0; // 32767 - 26214

            if (scaled > threshold)
            {
                scaled = threshold + headroom * Math.Tanh((scaled - threshold) / headroom);
            }
            else if (scaled < -threshold)
            {
                scaled = -threshold - headroom * Math.Tanh((-scaled - threshold) / headroom);
            }

            int result = (int)Math.Round(scaled);
            if (result > short.MaxValue) return short.MaxValue;
            if (result < short.MinValue) return short.MinValue;
            return (short)result;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
