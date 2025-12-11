using System.Runtime.InteropServices;
using System.Text;
using Crucible.Core.ECS;
using Crucible.Core.Exceptions;

namespace Crucible.Core;
/// <summary>
/// Structure in the scene tree with parents, children, and components
/// </summary>
public unsafe ref partial struct Node
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
    
    public bool IsNull{get{return _pointer == IntPtr.Zero;}}

    private void ErrorIfNull()
    {
        if (_pointer == IntPtr.Zero)
        {
            throw new NullReferenceException();
        }
    }
    
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_NodeCreate(ref IntPtr node);
    [LibraryImport("Crucible")]
    private static partial UUID CRUCIBLE_NATIVE_NodeGetUUID(IntPtr node);
    [LibraryImport("Crucible")]
    private static partial Int32 CRUCIBLE_NATIVE_NodeGetNameLength(IntPtr node);
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_NodeGetName(IntPtr node, byte* nameArray);
    [LibraryImport("Crucible", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void CRUCIBLE_NATIVE_NodeSetName(IntPtr node, string name);
    [LibraryImport("Crucible")]
    private static partial IntPtr CRUCIBLE_NATIVE_NodeGetParent(IntPtr node);
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_NodeSetParent(IntPtr node, IntPtr parent);
    [LibraryImport("Crucible")]
    private static partial UInt32 CRUCIBLE_NATIVE_NodeGetChildCount(IntPtr node);
    [LibraryImport("Crucible")]
    private static partial IntPtr CRUCIBLE_NATIVE_NodeGetChild(IntPtr node, UInt32 index);
    [LibraryImport("Crucible")]
    private static partial IntPtr CRUCIBLE_NATIVE_NodeAddChild(IntPtr node);
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_NodeRemoveChildByIndex(IntPtr node, UInt32 index);
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_NodeRemoveChildByValue(IntPtr node, IntPtr child);
    [LibraryImport("Crucible", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void CRUCIBLE_NATIVE_NodeAddDataComponent(IntPtr node, string typeName, UInt64 size, UInt64 alignment, IntPtr data);
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_NodeAddDataComponentFast(IntPtr node, DataTypeId id, IntPtr data, UInt64 size);
    [LibraryImport("Crucible", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void CRUCIBLE_NATIVE_NodeRemoveDataComponent(IntPtr node, string typeName, UInt64 size, UInt64 alignment);
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_NodeRemoveDataComponentFast(IntPtr node,DataTypeId id);
    [LibraryImport("Crucible", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CRUCIBLE_NATIVE_NodeHasDataComponent(IntPtr node, string typeName, UInt64 size, UInt64 alignment);
    [LibraryImport("Crucible")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CRUCIBLE_NATIVE_NodeHasDataComponentFast(IntPtr node, DataTypeId id);
    [LibraryImport("Crucible", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr CRUCIBLE_NATIVE_NodeGetDataComponent(IntPtr node, string typeName, UInt64 size, UInt64 alignment);
    [LibraryImport("Crucible")]
    private static partial IntPtr CRUCIBLE_NATIVE_NodeGetDataComponentFast(IntPtr node, DataTypeId id);
    [LibraryImport("Crucible", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void CRUCIBLE_NATIVE_NodeAddReferenceComponent(IntPtr node, string typeName, IntPtr gcHandle);
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_NodeAddReferenceComponentFast(IntPtr node, ReferenceTypeId id, IntPtr gcHandle);
    [LibraryImport("Crucible", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void CRUCIBLE_NATIVE_NodeRemoveReferenceComponent(IntPtr node, string typeName);
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_NodeRemoveReferenceComponentFast(IntPtr node, ReferenceTypeId id);
    [LibraryImport("Crucible", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CRUCIBLE_NATIVE_NodeHasReferenceComponent(IntPtr node, string typeName);
    [LibraryImport("Crucible")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CRUCIBLE_NATIVE_NodeHasReferenceComponentFast(IntPtr node, ReferenceTypeId id);
    [LibraryImport("Crucible", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr CRUCIBLE_NATIVE_NodeGetReferenceComponent(IntPtr node, string typeName);
    [LibraryImport("Crucible")]
    private static partial IntPtr CRUCIBLE_NATIVE_NodeGetReferenceComponentFast(IntPtr node, ReferenceTypeId id);
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_NodeGetCumulativeTransform(IntPtr node, out Transform transform);
    [LibraryImport("Crucible")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CRUCIBLE_NATIVE_NodeIsEnabled(IntPtr node);
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_NodeEnable(IntPtr node, [MarshalAs(UnmanagedType.Bool)]bool propagate);
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_NodeDisable(IntPtr node, [MarshalAs(UnmanagedType.Bool)]bool propagate);


    /// <summary>
    /// Unique identifier that represents this node's ID
    /// </summary>
    /// <returns></returns>
    public UUID Uuid()
    {
        ErrorIfNull();
        return CRUCIBLE_NATIVE_NodeGetUUID(_pointer);
    }
    /// <summary>
    /// The name of the node, useful for identifying in a scene tree
    /// </summary>
    public string Name
    {
        get
        {
            ErrorIfNull();
            var nameSize = CRUCIBLE_NATIVE_NodeGetNameLength(_pointer);
            if (nameSize == 0)
            {
                return string.Empty;
            }
            byte[] nameArray = new byte[nameSize];
            fixed (byte* nameArrayPtr = nameArray)
            {
                CRUCIBLE_NATIVE_NodeGetName(_pointer, nameArrayPtr);
            }
            return Encoding.UTF8.GetString(nameArray);
        }
        set
        {
            ErrorIfNull();
            CRUCIBLE_NATIVE_NodeSetName(_pointer, value);
        }
    }
    /// <summary>
    /// The Node that owns this node in the scene tree
    /// </summary>
    /// <returns></returns>
    public Node Parent()
    {
        ErrorIfNull();
        var parentPointer = CRUCIBLE_NATIVE_NodeGetParent(_pointer);
        return new Node(parentPointer);
    }
    /// <summary>
    /// Whether the node has a parent (only the root node should have no parent)
    /// </summary>
    /// <returns></returns>
    public bool HasParent()
    {
        ErrorIfNull();
        var parentPointer = CRUCIBLE_NATIVE_NodeGetParent(_pointer);
        if (parentPointer == IntPtr.Zero)
        {
            return false;
        }
        return true;
    }
    /// <summary>
    /// Attempts to get the parent if a node has one
    /// </summary>
    /// <param name="node">The parent node if one exists</param>
    /// <returns>True if parent exists, false if not</returns>
    public bool TryGetParent(out Node node)
    {
        ErrorIfNull();
        var parentPointer = CRUCIBLE_NATIVE_NodeGetParent(_pointer);
        node = new Node(parentPointer);
        if (parentPointer == IntPtr.Zero)
        {
            return false;
        }
        return true;
    }
    /// <summary>
    /// Sets the node's parent to another node
    /// </summary>
    /// <param name="parent">The new parent</param>
    /// <exception cref="NullReferenceException">Parent node must not be null</exception>
    /// <exception cref="InvalidOperationException">Parent node must not be self</exception>
    public void SetParent(Node parent)
    {
        ErrorIfNull();
        if (parent._pointer == IntPtr.Zero)
        {
            throw new NullReferenceException("Cannot set node parent to null.");
        }

        if (parent._pointer == _pointer)
        {
            throw new InvalidOperationException("Cannot set self to parent");
        }
        CRUCIBLE_NATIVE_NodeSetParent(_pointer, parent._pointer);
    }
    /// <summary>
    /// The number of children this node has
    /// </summary>
    /// <returns></returns>
    public UInt32 ChildCount()
    {
        ErrorIfNull();
        return CRUCIBLE_NATIVE_NodeGetChildCount(_pointer);
    }
    /// <summary>
    /// Retrieve a child at a given index
    /// </summary>
    /// <param name="index"></param>
    /// <returns></returns>
    /// <exception cref="IndexOutOfRangeException"></exception>
    public Node GetChild(UInt32 index)
    {
        ErrorIfNull();
        var child = CRUCIBLE_NATIVE_NodeGetChild(_pointer, index);
        if (child == IntPtr.Zero)
        {
            throw new IndexOutOfRangeException();
        }
        return new Node(child);

    }
    /// <summary>
    /// Add new child node to this one
    /// </summary>
    /// <returns></returns>
    public Node AddChild()
    {
        ErrorIfNull();
        var child = CRUCIBLE_NATIVE_NodeAddChild(_pointer);
        return new Node(child);
    }
    /// <summary>
    /// Add a new child node to this one
    /// </summary>
    /// <param name="name">Name of the new node</param>
    /// <returns></returns>
    public Node AddChild(string name)
    {
        ErrorIfNull();
        var child = AddChild();
        child.Name = name;
        return child;
    }
    /// <summary>
    /// Remove a child at an index
    /// </summary>
    /// <param name="index"></param>
    public void RemoveChild(UInt32 index)
    {
        ErrorIfNull();
        CRUCIBLE_NATIVE_NodeRemoveChildByIndex(_pointer, index);
    }
    /// <summary>
    /// Remove a child by reference
    /// </summary>
    /// <param name="child"></param>
    public void RemoveChild(Node child)
    {
        ErrorIfNull();
        CRUCIBLE_NATIVE_NodeRemoveChildByValue(_pointer, child._pointer);
    }

    public NodeKeeper Detach(bool autoDisable = true)
    {
        ErrorIfNull();
        if (!HasParent())
        {
            throw new InvalidOperationException("Root Nodes cannot be detached");
        }
        CRUCIBLE_NATIVE_NodeSetParent(_pointer, IntPtr.Zero);
        if (autoDisable)
        {
            Enabled = false;
        }
        return new NodeKeeper(this);
    }
    /// <summary>
    /// Add value component to this node
    /// </summary>
    /// <param name="component"></param>
    /// <typeparam name="T"></typeparam>
    /// <exception cref="TypeAccessException"></exception>
    public void AddDataComponent<T>(T component)where T:unmanaged
    {
        ErrorIfNull();
        string? name = typeof(T).FullName;
        if (name != null)
        {
            var handle = GCHandle.Alloc(component, GCHandleType.Pinned);
            CRUCIBLE_NATIVE_NodeAddDataComponent(_pointer, name, (UInt64)sizeof(T), (UInt64)Alignment.AlignmentOf<T>(),handle.AddrOfPinnedObject());
            handle.Free();
        }
        else
        {
            throw new TypeAccessException("Type \""+typeof(T)+"\" cannot be made into component");
        }
        
    }
    /// <summary>
    /// Add value component to this node. This is faster than the safe method assuming the id has already been found, but expects the caller to ensure DataTypeId matches T
    /// </summary>
    /// <param name="id"></param>
    /// <param name="component"></param>
    /// <typeparam name="T"></typeparam>
    public void AddDataComponentUnsafe<T>(DataTypeId id, T component) where T : unmanaged
    {
        var handle = GCHandle.Alloc(component, GCHandleType.Pinned);
        var size = sizeof(T);
        CRUCIBLE_NATIVE_NodeAddDataComponentFast(_pointer,id,handle.AddrOfPinnedObject(),(ulong)size);
        handle.Free();
    }
    /// <summary>
    /// Remove value component from this node
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public void RemoveDataComponent<T>()where T:unmanaged
    {
        ErrorIfNull();
        string? name = typeof(T).FullName;
        if (name != null)
        {
            CRUCIBLE_NATIVE_NodeRemoveDataComponent(_pointer, name, (UInt64) sizeof(T), (UInt64) Alignment.AlignmentOf<T>());
        }
    }
    /// <summary>
    /// Remove value component from this node using a type index. This is faster than the generic method assuming the id has already been found
    /// </summary>
    /// <param name="id">Type id to remove</param>
    public void RemoveDataComponent(DataTypeId id)
    {
        ErrorIfNull();
        CRUCIBLE_NATIVE_NodeRemoveDataComponentFast(_pointer, id);
    }
    /// <summary>
    /// If the node has a given data component attached
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public bool HasDataComponent<T>() where T : unmanaged
    {
        ErrorIfNull();
        string? name = typeof(T).FullName;
        if (name != null)
        {
            return CRUCIBLE_NATIVE_NodeHasDataComponent(_pointer, name, (UInt64) sizeof(T), (UInt64) Alignment.AlignmentOf<T>());
        }
        return false;
    }
    /// <summary>
    /// If the node has a given data component attached. This is faster than the generic method assuming the id has already been found
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public bool HasDataComponent(DataTypeId id)
    {
        ErrorIfNull();
        return CRUCIBLE_NATIVE_NodeHasDataComponentFast(_pointer, id);
    }
    
    /// <summary>
    /// Retrieve value component from this node
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    /// <exception cref="ComponentNotFoundException"></exception>
    /// <exception cref="TypeAccessException"></exception>
    public ref T GetDataComponent<T>()where T:unmanaged
    {
        ErrorIfNull();
        string? name = typeof(T).FullName;
        if (name != null)
        {
            var componentPointer = CRUCIBLE_NATIVE_NodeGetDataComponent(_pointer, name, (UInt64) sizeof(T), (UInt64) Alignment.AlignmentOf<T>());
            if (componentPointer == IntPtr.Zero)
            {
                throw new ComponentNotFoundException("No component \""+typeof(T)+"\" is registered to this object");
            }
            return ref System.Runtime.CompilerServices.Unsafe.AsRef<T>((void*)componentPointer);
        }
        throw new TypeAccessException("Type \""+typeof(T)+"\" cannot be made into component");
    }
    /// <summary>
    /// Retrieve value component from this node. This is faster than the safe method assuming the id has already been found, but expects the caller to ensure DataTypeId matches T
    /// </summary>
    /// <param name="id">Type id to remove</param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public ref T GetDataComponentUnsafe<T>(DataTypeId id)
    {
        ErrorIfNull();
        var componentPointer = CRUCIBLE_NATIVE_NodeGetDataComponentFast(_pointer, id);
        if(componentPointer == IntPtr.Zero)
        {
            throw new ComponentNotFoundException("No component \""+typeof(T)+"\" is registered to this object");
        }
        return ref System.Runtime.CompilerServices.Unsafe.AsRef<T>((void*)componentPointer);
    }
    
    
    /// <summary>
    /// Add reference component to this node
    /// </summary>
    /// <param name="component"></param>
    /// <typeparam name="T"></typeparam>
    /// <exception cref="TypeAccessException"></exception>
    public void AddReferenceComponent<T>(T component) where T : class
    {
        ErrorIfNull();
        string? name = typeof(T).FullName;
        if (name != null)
        {
            var handle = GCHandle.Alloc(component, GCHandleType.Normal);
            CRUCIBLE_NATIVE_NodeAddReferenceComponent(_pointer, name, GCHandle.ToIntPtr(handle));
        }
        else
        {
            throw new TypeAccessException("Type \""+typeof(T)+"\" cannot be made into component"); 
        }
        
    }
    /// <summary>
    /// Add reference component to this node. This is faster than the safe method assuming the id has already been found, but expects the caller to ensure DataTypeId matches T
    /// </summary>
    /// <param name="id"></param>
    /// <param name="component"></param>
    /// <typeparam name="T"></typeparam>
    public void AddReferenceComponentUnsafe<T>(ReferenceTypeId id, T component) where T : class
    {
        ErrorIfNull();
        var handle = GCHandle.Alloc(component, GCHandleType.Normal);
        CRUCIBLE_NATIVE_NodeAddReferenceComponentFast(_pointer,id, GCHandle.ToIntPtr(handle));
    }
    /// <summary>
    /// Remove reference component from this node
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public void RemoveReferenceComponent<T>() where T : class
    {
        ErrorIfNull();
        string? name = typeof(T).FullName;
        if (name != null)
        {
            CRUCIBLE_NATIVE_NodeRemoveReferenceComponent(_pointer, name);
        }
    }
    /// <summary>
    /// Remove reference component from this node. This is faster than the generic method assuming the id has already been found
    /// </summary>
    /// <param name="id"></param>
    public void RemoveReferenceComponent(ReferenceTypeId id)
    {
        ErrorIfNull();
        CRUCIBLE_NATIVE_NodeRemoveReferenceComponentFast(_pointer, id);
    }
    /// <summary>
    /// If the node has a given reference component attached
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public bool HasReferenceComponent<T>() where T : class
    {
        ErrorIfNull();
        string? name = typeof(T).FullName;
        if (name != null)
        {
            return CRUCIBLE_NATIVE_NodeHasReferenceComponent(_pointer, name);
        }
        return false;
    }
    /// <summary>
    /// If the node has a given reference component attached. This is faster than the generic method assuming the id has already been found
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public bool HasReferenceComponent(ReferenceTypeId id)
    {
        ErrorIfNull();
        return CRUCIBLE_NATIVE_NodeHasReferenceComponentFast(_pointer, id);
    }
    
    /// <summary>
    /// Retrieve reference component for this node
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    /// <exception cref="TypeAccessException"></exception>
    public T? GetReferenceComponent<T>() where T : class
    {
        ErrorIfNull();
        string? name = typeof(T).FullName;
        if (name != null)
        {
            var pointer = CRUCIBLE_NATIVE_NodeGetReferenceComponent(_pointer, name);
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
    /// <summary>
    /// Retrieve reference component for this node. This is faster than the safe method assuming the id has already been found, but expects the caller to ensure DataTypeId matches T
    /// </summary>
    /// <param name="id"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public T? GetReferenceComponentUnsafe<T>(ReferenceTypeId id) where T : class
    {
        ErrorIfNull();
        var pointer = CRUCIBLE_NATIVE_NodeGetReferenceComponentFast(_pointer, id);
        if (pointer == IntPtr.Zero)
        {
            return null;
        }
        var handle = GCHandle.FromIntPtr(pointer);
        return (T?)handle.Target;
    }
    /// <summary>
    /// Get the transform for this node in global space
    /// </summary>
    /// <returns></returns>
    public Transform CumulativeTransform()
    {
        ErrorIfNull();
        CRUCIBLE_NATIVE_NodeGetCumulativeTransform(_pointer, out Transform transform);
        return transform;
    }
    /// <summary>
    /// IF this node will be included in systems and tree walks (Disabling/enabling nodes propagates to their children)
    /// </summary>
    public bool Enabled
    {
        get
        {
            return CRUCIBLE_NATIVE_NodeIsEnabled(_pointer);
        }
        set
        {
            if (value)
            {
                CRUCIBLE_NATIVE_NodeEnable(_pointer, true);
            }
            else
            {
                CRUCIBLE_NATIVE_NodeDisable(_pointer,true);
            }
        }
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