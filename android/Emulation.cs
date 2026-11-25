using System;
using Android.Media;

namespace Boksol
{
    internal sealed class FramePixels : IDisposable
    {
        private readonly int[] pixels;
        private readonly int width;

        public FramePixels(int[] pixels, int width)
        {
            this.pixels = pixels;
            this.width = width;
        }

        public System.Drawing.Color GetPixel(int x, int y)
        {
            return System.Drawing.Color.FromArgb(pixels[y * width + x]);
        }

        public void Dispose() { }
    }

    internal sealed class CoreAudio : IDisposable
    {
        private readonly AudioTrack track;

        public CoreAudio()
        {
            const int sampleRate = 48000;
            int minimum = AudioTrack.GetMinBufferSize(sampleRate, ChannelOut.Stereo, Encoding.Pcm16bit);
            if (minimum <= 0) return;
            using var attributes = new AudioAttributes.Builder().SetUsage(AudioUsageKind.Game)
                .SetContentType(AudioContentType.Music).Build();
            using var format = new AudioFormat.Builder().SetSampleRate(sampleRate)
                .SetChannelMask(ChannelOut.Stereo).SetEncoding(Encoding.Pcm16bit).Build();
            using var builder = new AudioTrack.Builder().SetAudioAttributes(attributes).SetAudioFormat(format)
                .SetBufferSizeInBytes(Math.Max(minimum, 8192)).SetTransferMode(AudioTrackMode.Stream);
            track = builder.Build();
            if (track.State == AudioTrackState.Initialized) track.Play();
        }

        public void Write(short[] samples, int count)
        {
            if (track?.State == AudioTrackState.Initialized)
                track.Write(samples, 0, count, WriteMode.NonBlocking);
        }

        public void Dispose()
        {
            track?.Release();
            track?.Dispose();
        }
    }
}
