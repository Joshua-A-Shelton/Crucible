using System.Runtime.InteropServices;
using Crucible.Core.Exceptions;
using Crucible.Initialization;

namespace Crucible.Core;

public unsafe ref struct Node
{
    private IntPtr _pointer;

    internal Node(IntPtr pointer)
    {
        _pointer = pointer;
    }

    internal IntPtr Pointer
    {
        get { return _pointer; }
    }
    
#pragma warning disable 0649
    private static delegate* unmanaged<IntPtr, void> _nodePointerLockFamily_ptr;
    private static delegate* unmanaged<IntPtr, void> _nodePointerUnlockFamily_ptr;
    
    private static delegate* unmanaged<IntPtr, UUID> _nodePointerUUID_ptr;
    private static delegate* unmanaged<IntPtr, int> _nodePointerGetNameLength;
    private static delegate* unmanaged<IntPtr, IntPtr, int,void> _nodePointerGetName;
    private static delegate* unmanaged<IntPtr, string, void> _nodePointerSetName;
    private static delegate* unmanaged<IntPtr, IntPtr> _nodePointerParent_ptr;
    private static delegate* unmanaged<IntPtr, IntPtr, void> _nodePointerSetParent_ptr;
    private static delegate* unmanaged<IntPtr, int> _nodePointerChildCount_ptr;
    private static delegate* unmanaged<IntPtr, int, IntPtr> _nodePointerGetChild_ptr;
    private static delegate* unmanaged<IntPtr, IntPtr> _nodePointerAddChild_ptr;
    private static delegate* unmanaged<IntPtr, int, void> _nodePointerRemoveChildByIndex_ptr;
    private static delegate* unmanaged<IntPtr, IntPtr, void> _nodePointerRemoveChildByValue_ptr;

    private static delegate* unmanaged<IntPtr, string, UInt64, UInt64, IntPtr, void> _nodePointerAddDataComponent_ptr;
    private static delegate* unmanaged<IntPtr, string, UInt64, UInt64, void> _nodePointerRemoveDataComponent_ptr;
    private static delegate* unmanaged<IntPtr, string, UInt64, UInt64, void*> _nodePointerGetDataComponent_ptr;
    
    private static delegate* unmanaged<IntPtr, string, IntPtr, IntPtr, void> _nodePointerAddReferenceComponent_ptr;
    private static delegate* unmanaged<IntPtr, string, void> _nodePointerRemoveReferenceComponent_ptr;
    private static delegate* unmanaged<IntPtr, string, IntPtr> _nodePointerGetReferenceComponent_ptr;
    
    private static delegate* unmanaged<IntPtr, ref Transform, void> _nodePointerGetCumulativeTransform_ptr;
    
#pragma warning restore 0649

    public UUID Uuid()
    {
        return _nodePointerUUID_ptr(_pointer);
    }

    public string Name
    {
        get
        {
            var arraySize = _nodePointerGetNameLength(_pointer);
            byte[] buffer = new byte[arraySize];
            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            _nodePointerGetName(_pointer, handle.AddrOfPinnedObject(), arraySize);
            string? name = Marshal.PtrToStringUTF8(handle.AddrOfPinnedObject());
            handle.Free();
            if (name != null)
            {
                return name;
            }

            return "";
        }
        set
        {
            _nodePointerSetName(_pointer, value);
        }
    }

    public Node Parent()
    {
        var parentPointer = _nodePointerParent_ptr(_pointer);
        if (parentPointer == IntPtr.Zero)
        {
            throw new NullReferenceException();
        }

        return new Node(parentPointer);
    }

    public bool HasParent()
    {
        var parentPointer = _nodePointerParent_ptr(_pointer);
        if (parentPointer == IntPtr.Zero)
        {
            return false;
        }
        return true;
    }

    public void SetParent(Node parent)
    {
        _nodePointerSetParent_ptr(_pointer,parent._pointer);
    }

    public int ChildCount()
    {
        return _nodePointerChildCount_ptr(_pointer);
    }

    public Node GetChild(int index)
    {
        _nodePointerLockFamily_ptr(_pointer);
        if (index < 0 && index >= ChildCount())
        {
            _nodePointerUnlockFamily_ptr(_pointer);
            throw new IndexOutOfRangeException();
        }
        var child = _nodePointerGetChild_ptr(_pointer, index);
        _nodePointerUnlockFamily_ptr(_pointer);
        return new Node(child);

    }

    public Node AddChild()
    {
        var child = _nodePointerAddChild_ptr(_pointer);
        return new Node(child);
    }

    public Node AddChild(string name)
    {
        var child = AddChild();
        _nodePointerSetName(child._pointer, name);
        return child;
    }

    public void RemoveChild(int index)
    {
        _nodePointerRemoveChildByIndex_ptr(_pointer, index);
    }

    public void RemoveChild(Node child)
    {
        _nodePointerRemoveChildByValue_ptr(_pointer, child._pointer);
    }

    public void AddDataComponent<T>(T component)where T:unmanaged
    {
        string? name = typeof(T).FullName;
        if (name != null)
        {
            var handle = GCHandle.Alloc(component, GCHandleType.Pinned);
            _nodePointerAddDataComponent_ptr(_pointer, name, (UInt64)sizeof(T), (UInt64)BlittableHelper.AlignmentOf<T>(),handle.AddrOfPinnedObject());
            handle.Free();
        }
        else
        {
            throw new TypeAccessException("Type \""+typeof(T)+"\" cannot be made into component");
        }
        
    }

    public void RemoveDataComponent<T>()where T:unmanaged
    {
        string? name = typeof(T).FullName;
        if (name != null)
        {
            _nodePointerRemoveDataComponent_ptr(_pointer, name, (UInt64) sizeof(T), (UInt64) BlittableHelper.AlignmentOf<T>());
        }
    }

    public ref T GetDataComponent<T>()where T:unmanaged
    {
        string? name = typeof(T).FullName;
        if (name != null)
        {
            var componentPointer = _nodePointerGetDataComponent_ptr(_pointer, name, (UInt64) sizeof(T), (UInt64) BlittableHelper.AlignmentOf<T>());
            if (componentPointer == null)
            {
                throw new ComponentNotFoundException("No component \""+typeof(T)+"\" is registered to this object");
            }
            return ref System.Runtime.CompilerServices.Unsafe.AsRef<T>(componentPointer);
        }
        throw new TypeAccessException("Type \""+typeof(T)+"\" cannot be made into component");
    }

    public void AddReferenceComponent<T>(T component) where T : class
    {
        string? name = typeof(T).FullName;
        if (name != null)
        {
            var handle = GCHandle.Alloc(component, GCHandleType.Normal);
            _nodePointerAddReferenceComponent_ptr(_pointer, name, typeof(T).TypeHandle.Value, GCHandle.ToIntPtr(handle));
        }
        else
        {
            throw new TypeAccessException("Type \""+typeof(T)+"\" cannot be made into component"); 
        }
        
    }

    public void RemoveReferenceComponent<T>() where T : class
    {
        string? name = typeof(T).FullName;
        if (name != null)
        {
            _nodePointerRemoveReferenceComponent_ptr(_pointer, name);

        }
    }

    public T? GetReferenceComponent<T>() where T : class
    {
        string? name = typeof(T).FullName;
        if (name != null)
        {
            var pointer = _nodePointerGetReferenceComponent_ptr(_pointer, name);
            if (pointer == IntPtr.Zero)
            {
                return null;
            }
            var handle = GCHandle.FromIntPtr(pointer);
            return (T?)handle.Target;
        }
        else
        {
            throw new TypeAccessException("Type \""+typeof(T)+"\" cannot be made into component");
        }
    }

    public Transform CumulativeTransform()
    {
        Transform t = new Transform();
        _nodePointerGetCumulativeTransform_ptr(_pointer,ref t);
        return t;
    }

    public static bool operator ==(Node n1, Node n2)
    {
        return n1.Pointer == n2.Pointer;
    }

    public static bool operator !=(Node n1, Node n2)
    {
        return n1.Pointer != n2.Pointer;
    }

    public override bool Equals(object? obj)
    {
        throw new NotSupportedException("Node does not support Equals");
    }

    public override int GetHashCode()
    {
        throw new NotSupportedException("Node does not support GetHashCode");
    }
    
}