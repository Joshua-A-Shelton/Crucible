using System.Runtime.CompilerServices;

namespace Crucible.Core;
/// <summary>
/// Presents a view into CPU accessible GPU buffers as a continuous sequence of structured objects
/// </summary>
/// <typeparam name="T">structured elements in the buffer</typeparam>
public unsafe ref struct StructuredBufferView<T> where T : unmanaged
{
    private void* _bufferHandle;
    /// <summary>
    /// Total number of elements in the buffer
    /// </summary>
    public UInt64 Count { get; private set; }

    internal StructuredBufferView(void* bufferHandle, UInt64 elementCount)
    {
        _bufferHandle = bufferHandle;
        Count = elementCount;
    }
    /// <summary>
    /// Return a reference to an object in the buffer
    /// </summary>
    /// <param name="index"></param>
    /// <exception cref="IndexOutOfRangeException"></exception>
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