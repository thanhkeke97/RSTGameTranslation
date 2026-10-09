using System;
using NAudio.Wave;

namespace RSTGameTranslation
{
    /// <summary>
    /// Downmix any channel count to mono. NAudio's ToMono() only accepts stereo and throws
    /// "Source must be stereo" on 5.1/7.1 devices, which are common with gaming headsets.
    /// For the standard 5.1/7.1 layouts (FL FR FC LFE BL BR [SL SR]) this uses ITU-style
    /// weights: dialogue in games sits mostly in the center channel, so it must be kept at
    /// full level, and LFE carries no speech so it is dropped.
    /// </summary>
    public class MultiChannelToMonoSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider source;
        private readonly int channels;
        private readonly float[] weights;
        private float[] sourceBuffer = Array.Empty<float>();

        public WaveFormat WaveFormat { get; }

        public MultiChannelToMonoSampleProvider(ISampleProvider source)
        {
            this.source = source;
            channels = source.WaveFormat.Channels;
            weights = BuildWeights(channels);
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
        }

        private static float[] BuildWeights(int channels)
        {
            const float Center = 0.707f;
            const float Surround = 0.354f;
            switch (channels)
            {
                case 1:
                    return new[] { 1f };
                case 2:
                    return new[] { 0.5f, 0.5f };
                case 6: // FL FR FC LFE BL BR
                    return new[] { 0.5f, 0.5f, Center, 0f, Surround, Surround };
                case 8: // FL FR FC LFE BL BR SL SR
                    return new[] { 0.5f, 0.5f, Center, 0f, Surround, Surround, Surround, Surround };
                default:
                    // Unknown layout: plain average
                    var w = new float[channels];
                    for (int i = 0; i < channels; i++) w[i] = 1f / channels;
                    return w;
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            if (channels == 1) return source.Read(buffer, offset, count);

            int sourceCount = count * channels;
            if (sourceBuffer.Length < sourceCount) sourceBuffer = new float[sourceCount];

            int sourceRead = source.Read(sourceBuffer, 0, sourceCount);
            int frames = sourceRead / channels;
            for (int f = 0; f < frames; f++)
            {
                int baseIndex = f * channels;
                float sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    sum += sourceBuffer[baseIndex + c] * weights[c];
                }
                buffer[offset + f] = sum;
            }
            return frames;
        }
    }
}
