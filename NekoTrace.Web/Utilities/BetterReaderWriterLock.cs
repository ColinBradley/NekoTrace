namespace NekoTrace.Web.Utilities;

public sealed class BetterReaderWriterLock : IDisposable
{
    internal ReaderWriterLockSlim mLock = new();

    public IDisposable Read()
    {
        mLock.EnterReadLock();

        return new Releaser(mLock, LockType.Read);
    }

    public IDisposable UpgradeableRead()
    {
        mLock.EnterUpgradeableReadLock();

        return new Releaser(mLock, LockType.UpgradeableRead);
    }

    public IDisposable Write()
    {
        mLock.EnterWriteLock();

        return new Releaser(mLock, LockType.Write);
    }

    public T GetOrCreate<T>(Func<T?> getter, Func<T> setter)
    {
        using var reader = this.UpgradeableRead();

        var value = getter();

        if (value is null)
        {
            using var writer = this.Write();

            value = getter() ?? setter();
        }

        return value;
    }

    public void Dispose()
    {
        mLock.Dispose();
    }
}

file readonly struct Releaser(
    ReaderWriterLockSlim readerWriterLock,
    LockType lockType
)
    : IDisposable
{
    void IDisposable.Dispose()
    {
        switch (lockType)
        {
            case LockType.Read:
                readerWriterLock.ExitReadLock();
                break;
            case LockType.UpgradeableRead:
                readerWriterLock.ExitUpgradeableReadLock();
                break;
            case LockType.Write:
                readerWriterLock.ExitWriteLock();
                break;
        }
    }
}

file enum LockType
{
    Read,
    UpgradeableRead,
    Write
}