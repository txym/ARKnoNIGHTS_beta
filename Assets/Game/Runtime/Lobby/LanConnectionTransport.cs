using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ArknoNights.Lobby
{
    internal static class LanFrameTransport
    {
        public static async Task<byte[]> ReadFrameAsync(
            NetworkStream stream,
            CancellationToken cancellationToken,
            int maximumFrameBytes = MatchProtocol.AbsoluteMaximumFrameBytes)
        {
            if (maximumFrameBytes <= sizeof(int)
                || maximumFrameBytes > MatchProtocol.AbsoluteMaximumFrameBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumFrameBytes));
            }
            var prefix = new byte[sizeof(int)];
            if (!await ReadExactlyAsync(
                stream,
                prefix,
                0,
                prefix.Length,
                cancellationToken).ConfigureAwait(false))
            {
                return null;
            }
            var payloadLength = (prefix[0] << 24)
                | (prefix[1] << 16)
                | (prefix[2] << 8)
                | prefix[3];
            if (payloadLength <= 0
                || payloadLength + sizeof(int) > maximumFrameBytes)
            {
                throw new InvalidDataException("lan.frame.length.invalid");
            }
            var frame = new byte[payloadLength + sizeof(int)];
            Buffer.BlockCopy(prefix, 0, frame, 0, prefix.Length);
            if (!await ReadExactlyAsync(
                stream,
                frame,
                sizeof(int),
                payloadLength,
                cancellationToken).ConfigureAwait(false))
            {
                throw new EndOfStreamException("lan.frame.truncated");
            }
            return frame;
        }

        private static async Task<bool> ReadExactlyAsync(
            NetworkStream stream,
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            var received = 0;
            while (received < count)
            {
                var read = await stream.ReadAsync(
                    buffer,
                    offset + received,
                    count - received,
                    cancellationToken).ConfigureAwait(false);
                if (read == 0) return false;
                received += read;
            }
            return true;
        }
    }

    internal sealed class LanConnectionWriter : IDisposable
    {
        private const int DefaultCapacity = 64;
        private readonly object gate = new object();
        private readonly NetworkStream stream;
        private readonly int capacity;
        private readonly LinkedList<WriteItem> pending = new LinkedList<WriteItem>();
        private readonly SemaphoreSlim available = new SemaphoreSlim(0);
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly Task writeTask;
        private bool completing;
        private bool disposed;

        public LanConnectionWriter(NetworkStream stream, int capacity = DefaultCapacity)
        {
            this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.capacity = capacity;
            writeTask = Task.Run(WriteLoopAsync);
        }

        public event Action Faulted;

        public bool TryEnqueue(byte[] frame, bool replacePendingSnapshot = false)
        {
            if (frame == null || frame.Length == 0) throw new ArgumentException("Frame is empty.", nameof(frame));
            lock (gate)
            {
                if (completing || disposed) return false;
                if (replacePendingSnapshot)
                {
                    var node = pending.Last;
                    if (node != null && node.Value.ReplaceableSnapshot)
                    {
                        node.Value = new WriteItem(frame, true);
                        return true;
                    }
                }
                if (pending.Count >= capacity) return false;
                pending.AddLast(new WriteItem(frame, replacePendingSnapshot));
                available.Release();
                return true;
            }
        }

        public async Task CompleteAsync(TimeSpan timeout)
        {
            lock (gate)
            {
                if (!completing)
                {
                    completing = true;
                    available.Release();
                }
            }
            var completed = await Task.WhenAny(writeTask, Task.Delay(timeout)).ConfigureAwait(false);
            if (completed != writeTask)
            {
                cancellation.Cancel();
                try { stream.Close(); }
                catch (Exception) { }
                await Task.WhenAny(
                    writeTask,
                    Task.Delay(TimeSpan.FromMilliseconds(250)))
                    .ConfigureAwait(false);
                return;
            }
            try { await writeTask.ConfigureAwait(false); }
            catch (Exception) { }
        }

        public void Cancel()
        {
            lock (gate)
            {
                if (disposed) return;
                completing = true;
            }
            cancellation.Cancel();
            available.Release();
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                completing = true;
            }
            cancellation.Cancel();
            available.Release();
            try { writeTask.GetAwaiter().GetResult(); }
            catch (Exception) { }
            cancellation.Dispose();
            available.Dispose();
        }

        private async Task WriteLoopAsync()
        {
            try
            {
                while (true)
                {
                    await available.WaitAsync(cancellation.Token).ConfigureAwait(false);
                    WriteItem item = null;
                    lock (gate)
                    {
                        if (pending.Count > 0)
                        {
                            item = pending.First.Value;
                            pending.RemoveFirst();
                        }
                        else if (completing)
                        {
                            return;
                        }
                    }
                    if (item == null) continue;
                    await stream.WriteAsync(
                        item.Frame,
                        0,
                        item.Frame.Length,
                        cancellation.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception)
            {
                Faulted?.Invoke();
            }
        }

        private sealed class WriteItem
        {
            public WriteItem(byte[] frame, bool replaceableSnapshot)
            {
                Frame = frame;
                ReplaceableSnapshot = replaceableSnapshot;
            }

            public byte[] Frame { get; }
            public bool ReplaceableSnapshot { get; }
        }
    }
}
