#include "Node.h"
#include <boost/uuid/random_generator.hpp>
#include <boost/functional/hash.hpp>
#include <unordered_map>
#include <crucible/ecs/World.h>
namespace crucible
{
    boost::uuids::random_generator NODE_UUID_GENERATOR;
    std::unordered_map<boost::uuids::uuid,Node*,boost::hash<boost::uuids::uuid>> NODE_UUID_MAP;
    std::mutex NODE_MAP_LOCK;

    Node::Node()
    {
        _uuid = NODE_UUID_GENERATOR();
        _entity = ecs::world().entity();

        auto crucibleNodeRefType = NodeECSReference::NodeEcsReferenceID();
        NodeECSReference reference{.node = this};
        ecs_add_id(ecs::world().world_,entity(),crucibleNodeRefType);
        ecs_set_id(ecs::world().world_,entity(),crucibleNodeRefType,sizeof(NodeECSReference),&reference);

        _entity.disable();
        std::lock_guard<std::mutex> lockMap(NODE_MAP_LOCK);
        NODE_UUID_MAP.insert({_uuid,this});

    }

    Node::Node(Node* parent)
    {
        std::lock_guard<std::mutex> lockChild(_familyMutex);
        _uuid = NODE_UUID_GENERATOR();
        auto& world = ecs::world();
        _entity = world.entity();
        if (parent!=nullptr)
        {
            if (!parent->isEnabled())
            {
                _entity.disable();
            }
        }
        else
        {
            _entity.disable();
        }
        auto crucibleNodeRefType = NodeECSReference::NodeEcsReferenceID();
        NodeECSReference reference{.node = this};
        ecs_add_id(world.world_,entity(),crucibleNodeRefType);
        ecs_set_id(world.world_,entity(),crucibleNodeRefType,sizeof(NodeECSReference),&reference);
        {
            std::lock_guard<std::mutex> lockMap(NODE_MAP_LOCK);
            NODE_UUID_MAP.insert({_uuid, this});
        }
        if(parent != nullptr && parent != this)
        {
            std::lock_guard<std::mutex> lockParent(parent->_familyMutex);
            parent->_children.push_back(this);
            _parent = parent;
        }
    }


    Node::~Node()
    {
        std::lock_guard<std::mutex> lock(_familyMutex);
        {
            std::lock_guard<std::mutex> lockMap(NODE_MAP_LOCK);
            NODE_UUID_MAP.erase(_uuid);
        }
        _entity.destruct();
        for(auto & i : _children)
        {
            delete i;
        }
    }

    Node* Node::getParent() const
    {
        return _parent;
    }

    void Node::setParent(Node* parent)
    {
        if (parent == this)
        {
            throw std::runtime_error("Unable to set self as parent");
        }
        if (parent != nullptr && parent->childrenCount() == UINT32_MAX)
        {
            throw std::runtime_error("Max Number of children reached");
        }
        std::lock_guard<std::mutex> lockchild(_familyMutex);
        if(parent!=_parent)
        {
            //remove from old parent
            if (_parent != nullptr)
            {
                std::lock_guard<std::mutex> lockparent(_parent->_familyMutex);
                _parent->_children.erase(std::find(_parent->_children.begin(), _parent->_children.end(), this));
            }
            _parent = parent;
            //add as child of new parent
            if (_parent)
            {
                std::lock_guard<std::mutex> lockparent(_parent->_familyMutex);
                parent->_children.push_back(this);
            }
        }
    }

    size_t Node::childrenCount()
    {
        std::lock_guard<std::mutex> lock(_familyMutex);
        return _children.size();
    }

    Node* Node::getChild(size_t index)
    {
        std::lock_guard<std::mutex> lock(_familyMutex);
        if (index >= _children.size())
        {
            return nullptr;
        }
        return _children.at(index);
    }

    Node* Node::addChild()
    {
        if (_children.size() < UINT32_MAX)
        {
            Node* child = new Node(this);
            return child;
        }
        throw std::runtime_error("Max Number of children reached");
    }

    void Node::killChild(size_t index)
    {
        std::lock_guard<std::mutex> lock(_familyMutex);
        delete _children.at(index);
        _children.erase(_children.begin()+static_cast<int>(index));
    }

    void Node::killChildByReference(Node* child)
    {
        std::lock_guard<std::mutex> lock(_familyMutex);
        auto deadChild = std::find(_children.begin(),_children.end(),child);
        if(deadChild!=_children.end())
        {
            delete child;
            _children.erase(deadChild);
        }
    }

    Node* Node::removeChild(size_t index)
    {
        std::lock_guard<std::mutex> lock(_familyMutex);
        auto returnNode =  _children.at(index);
        _children.erase(_children.begin()+static_cast<int>(index));
        return returnNode;
    }

    Node* Node::removeChildByReference(Node* child)
    {
        std::lock_guard<std::mutex> lock(_familyMutex);
        auto removedChild = std::find(_children.begin(),_children.end(),child);
        if(removedChild!=_children.end())
        {
            _children.erase(removedChild);
            return child;
        }
        return nullptr;
    }

    Node* Node::getNodeByID(const boost::uuids::uuid& id)
    {
        std::lock_guard<std::mutex> lockMap(NODE_MAP_LOCK);
        auto node = NODE_UUID_MAP.find(id);
        if(node != NODE_UUID_MAP.end())
        {
            return node->second;
        }
        return nullptr;
    }

    ecs_entity_t NodeECSReference::NodeEcsReferenceID()
    {
        return ecs::engineTypes().Node;
    }

    boost::uuids::uuid Node::uuid()
    {
        return _uuid;
    }

    flecs::entity Node::entity() const
    {
        return _entity;
    }

    const std::string& Node::name()
    {
        return _name;
    }

    void Node::setName(const std::string& name)
    {
        _name = name;
    }

    bool Node::isEnabled() const
    {
        return _entity.enabled();
    }

    void Node::enable(bool propagate) const
    {
        _entity.enable();
        if (propagate)
        {
            for(auto& child : _children)
            {
               child->enable(propagate);
            }
        }
    }

    void Node::disable(bool propagate) const
    {
        _entity.disable();
        if (propagate)
        {
            for(auto& child : _children)
            {
                child->disable(propagate);
            }
        }
    }
} // crucible
