using Arch.Core;
using Arch.Core.Extensions;

namespace Crucible;
using System;
public class GameObject
{
    public Guid Uuid { get; private set; }
    private Entity _entity;
    public string Name { get; set; }
    
    private List<GameObject> _children = new List<GameObject>();
    public IReadOnlyCollection<GameObject> Children
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
        _entity = GameWorld._world.Create();
        Uuid = Guid.NewGuid();
        GameWorld.gameObjects.Add(Uuid,new WeakReference<GameObject>(this));
    }

    private GameObject(string name, Guid uuid)
    {
        Name = name;
        Uuid = uuid;
        _entity = GameWorld._world.Create();
    }

    ~GameObject()
    {
        GameWorld.gameObjects.Remove(Uuid);
        GameWorld._world.Destroy(_entity);
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

    public IReadOnlyList<Type> ComponentTypes()
    {
        var archetype = _entity.GetArchetype();
        List<Type> types = new List<Type>(archetype.Entities);
        foreach (var atype in _entity.GetArchetype().Types)
        {
            types.Add(atype.Type);
        }

        return types;
    }
}