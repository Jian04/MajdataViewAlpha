using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MajdataCore
{
    // A bounded FIFO moves encoder I/O off the render thread. If the encoder
    // falls behind we wait for a buffer, never discard or reorder a frame.
    public sealed class RecordingFrameWriter : IDisposable
    {
        private readonly BlockingCollection<byte[]> available = new BlockingCollection<byte[]>();
        private readonly BlockingCollection<byte[]> pending = new BlockingCollection<byte[]>();
        private readonly Task writing;
        private readonly int frameBytes;
        private volatile Exception failure;
        public Exception Failure => failure;
        public long WrittenFrames { get; private set; }
        public RecordingFrameWriter(Stream target, int frameBytes, int buffers = 3)
        {
            if (frameBytes <= 0 || buffers < 1) throw new ArgumentOutOfRangeException();
            this.frameBytes = frameBytes;
            for (int i = 0; i < buffers; i++) available.Add(new byte[frameBytes]);
            writing = Task.Run(async () => {
                using var deadline = new CancellationTokenSource();
                try {
                    foreach (var frame in pending.GetConsumingEnumerable()) {
                        try {
                            deadline.CancelAfter(TimeSpan.FromSeconds(15));
                            await target.WriteAsync(frame, 0, frameBytes, deadline.Token).ConfigureAwait(false);
                            deadline.CancelAfter(Timeout.Infinite);
                            WrittenFrames++;
                        }
                        finally { available.Add(frame); }
                    }
                    target.Flush();
                } catch (Exception error) { failure = error; }
            });
        }
        public void Enqueue(byte[] source)
        {
            if (source.Length != frameBytes) throw new ArgumentException("Frame size changed.");
            byte[] frame;
            while (!available.TryTake(out frame, 20)) ThrowIfFailed();
            ThrowIfFailed();
            Buffer.BlockCopy(source, 0, frame, 0, frameBytes);
            pending.Add(frame);
        }
        private void ThrowIfFailed()
        {
            if (failure != null) throw new IOException("Video encoder pipe failed.", failure);
        }
        public void Dispose()
        {
            pending.CompleteAdding();
            writing.GetAwaiter().GetResult();
            pending.Dispose(); available.Dispose();
        }
    }
}
