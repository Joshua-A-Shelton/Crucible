using Arch.Core;
using Arch.Core.Extensions;

namespace Crucible;
using System;
public class GameObject
{
    private static World _world = World.Create();
    private Entity _entity;
    public string Name { get; set; }
    
    private List<GameObject> _children = new List<GameObject>();
    private IReadOnlyCollection<GameObject> Children
    {
        get { return _children; }
    }
    private GameObject? _parent = null;

    public GameObject? Parent
    {
        get
        {
            return _parent;
        }
        set
        {
            if (_parent != null)
            {
                _parent._children.Remove(this);
            }
            _parent = value;
            if (_parent != null)
            {
                _parent._children.Add(this);
            }
        }
    }

    public GameObject()
    {
        Name = "Game Object";
        _entity = _world.Create();
    }

    private GameObject(string name, Entity entity)
    {
        Name = name;
        _entity = entity;
    }

    ~GameObject()
    {
        _world.Destroy(_entity);
    }

    public void AddChild(GameObject gameObject)
    {
        gameObject.Parent = this;
    }

    public void AddComponent<T>(T component)
    {
        _entity.Add(component);
    }

    public ref T GetComponent<T>()
    {
        return ref _entity.Get<T>();
    }

    public bool Has<T>()
    {
        return _entity.Has<T>();
    }
    
}