﻿using System.Runtime.InteropServices;
using Crucible.Core.Exceptions;
using Crucible.Initialization;

namespace Crucible.Core;
/// <summary>
/// Structure in the scene tree with parents, children, and components
/// </summary>
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
    
    public bool IsNull{get{return _pointer == IntPtr.Zero;}}

    private void ErrorIfNull()
    {
        if (_pointer == IntPtr.Zero)
        {
            throw new NullReferenceException();
        }
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

    /// <summary>
    /// Unique identifier that represents this node's ID
    /// </summary>
    /// <returns></returns>
    public UUID Uuid()
    {
        ErrorIfNull();
        return _nodePointerUUID_ptr(_pointer);
    }
    /// <summary>
    /// The name of the node, useful for identifying in a scene tree
    /// </summary>
    public string Name
    {
        get
        {
            ErrorIfNull();
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
            ErrorIfNull();
            _nodePointerSetName(_pointer, value);
        }
    }
    /// <summary>
    /// The Node that owns this node in the scene tree
    /// </summary>
    /// <returns></returns>
    public Node Parent()
    {
        ErrorIfNull();
        var parentPointer = _nodePointerParent_ptr(_pointer);

        return new Node(parentPointer);
    }
    /// <summary>
    /// Whether the node has a parent (only the root node should have no parent)
    /// </summary>
    /// <returns></returns>
    public bool HasParent()
    {
        ErrorIfNull();
        var parentPointer = _nodePointerParent_ptr(_pointer);
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
        var parentPointer = _nodePointerParent_ptr(_pointer);
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
    public void SetParent(Node parent)
    {
        ErrorIfNull();
        if (parent._pointer == IntPtr.Zero)
        {
            throw new NullReferenceException("Cannot set node parent to null.");
        }
        _nodePointerSetParent_ptr(_pointer,parent._pointer);
    }
    /// <summary>
    /// The number of children this node has
    /// </summary>
    /// <returns></returns>
    public int ChildCount()
    {
        ErrorIfNull();
        return _nodePointerChildCount_ptr(_pointer);
    }
    /// <summary>
    /// Retrieve a child at a given index
    /// </summary>
    /// <param name="index"></param>
    /// <returns></returns>
    /// <exception cref="IndexOutOfRangeException"></exception>
    public Node GetChild(int index)
    {
        ErrorIfNull();
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
    /// <summary>
    /// Add new child node to this one
    /// </summary>
    /// <returns></returns>
    public Node AddChild()
    {
        ErrorIfNull();
        var child = _nodePointerAddChild_ptr(_pointer);
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
        _nodePointerSetName(child._pointer, name);
        return child;
    }
    /// <summary>
    /// Remove a child at an index
    /// </summary>
    /// <param name="index"></param>
    public void RemoveChild(int index)
    {
        ErrorIfNull();
        _nodePointerRemoveChildByIndex_ptr(_pointer, index);
    }
    /// <summary>
    /// Remove a child by reference
    /// </summary>
    /// <param name="child"></param>
    public void RemoveChild(Node child)
    {
        ErrorIfNull();
        _nodePointerRemoveChildByValue_ptr(_pointer, child._pointer);
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
            _nodePointerAddDataComponent_ptr(_pointer, name, (UInt64)sizeof(T), (UInt64)BlittableHelper.AlignmentOf<T>(),handle.AddrOfPinnedObject());
            handle.Free();
        }
        else
        {
            throw new TypeAccessException("Type \""+typeof(T)+"\" cannot be made into component");
        }
        
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
            _nodePointerRemoveDataComponent_ptr(_pointer, name, (UInt64) sizeof(T), (UInt64) BlittableHelper.AlignmentOf<T>());
        }
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
            var componentPointer = _nodePointerGetDataComponent_ptr(_pointer, name, (UInt64) sizeof(T), (UInt64) BlittableHelper.AlignmentOf<T>());
            if (componentPointer == null)
            {
                throw new ComponentNotFoundException("No component \""+typeof(T)+"\" is registered to this object");
            }
            return ref System.Runtime.CompilerServices.Unsafe.AsRef<T>(componentPointer);
        }
        throw new TypeAccessException("Type \""+typeof(T)+"\" cannot be made into component");
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
            _nodePointerAddReferenceComponent_ptr(_pointer, name, typeof(T).TypeHandle.Value, GCHandle.ToIntPtr(handle));
        }
        else
        {
            throw new TypeAccessException("Type \""+typeof(T)+"\" cannot be made into component"); 
        }
        
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
            _nodePointerRemoveReferenceComponent_ptr(_pointer, name);

        }
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
    /// <summary>
    /// Get the transform for this node in global space
    /// </summary>
    /// <returns></returns>
    public Transform CumulativeTransform()
    {
        ErrorIfNull();
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