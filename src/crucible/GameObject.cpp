#include "GameObject.h"
#include "EntityComponentSystem.h"
namespace crucible
{
    GameObject::GameObject()
    {
        _entity = EntityComponentSystem::registry.create();
    }

    GameObject::~GameObject()
    {
        if(!_moved)
        {
            EntityComponentSystem::registry.destroy(_entity);
        }
    }

    GameObject::GameObject(GameObject &&from)
    {
        move(std::move(from));
    }

    GameObject &GameObject::operator=(GameObject &&from)
    {
        move(std::move(from));
        return *this;
    }

    void GameObject::move(GameObject &&from)
    {
        std::swap(_entity,from._entity);
        _name.swap(from._name);
        from._moved = true;
    }

    template<class T>
    void GameObject::addComponent(T component)
    {
        EntityComponentSystem::registry.emplace<T>(_entity,std::move(component));
    }

    template<class T>
    void GameObject::removeComponent()
    {
        EntityComponentSystem::registry.erase<T>(_entity);
    }

    template<class T>
    T &GameObject::getComponent()
    {
        auto component = EntityComponentSystem::registry.get<T>(_entity);
        return component;
    }
} // crucible