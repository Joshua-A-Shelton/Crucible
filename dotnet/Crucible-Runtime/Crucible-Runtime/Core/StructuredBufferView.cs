using System.Runtime.CompilerServices;

namespace Crucible.Core;

public unsafe ref struct StructuredBufferView<T> where T : unmanaged
{
    private void* _bufferHandle;
    public UInt64 Count { get; private set; }

    internal StructuredBufferView(void* bufferHandle, UInt64 elementCount)
    {
        _bufferHandle = bufferHandle;
        Count = elementCount;
    }

    public ref T this[UInt64 index]
    {
        get
        {
            if (index >= Count)
            {
                throw new IndexOutOfRangeException();
            }
            return ref Unsafe.AsRef<T>(((T*)_bufferHandle) + index);
        }
    }
}