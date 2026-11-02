using System;
using System.Threading;
using System.Threading.Tasks;

namespace SharpScript.Common
{
    public sealed class AsyncLock(int initialCount = 1)
    {
        private readonly SemaphoreSlim m_semaphore = new(initialCount);

        public async ValueTask<Releaser> LockAsync(CancellationToken cancellationToken = default)
        {
            await m_semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new Releaser(m_semaphore);
        }

        public readonly struct Releaser(SemaphoreSlim semaphore) : IDisposable
        {
            public void Dispose() => semaphore.Release();
        }
    }
}
