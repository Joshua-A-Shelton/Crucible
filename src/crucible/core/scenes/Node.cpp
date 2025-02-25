#include "Node.h"
#include <crucible/scripting/ScriptingEngine.h>
#include <cassert>
#include <boost/uuid/random_generator.hpp>
#include <boost/functional/hash.hpp>
#include <unordered_map>

namespace crucible
{
    namespace core
    {
        boost::uuids::random_generator NODE_UUID_GENERATOR;
        std::unordered_map<boost::uuids::uuid,Node*,boost::hash<boost::uuids::uuid>> NODE_UUID_MAP;
        std::mutex NODE_MAP_LOCK;

        Node::Node()
        {
            _uuid = NODE_UUID_GENERATOR();
            std::lock_guard<std::mutex> lockMap(NODE_MAP_LOCK);
            NODE_UUID_MAP.insert({_uuid,this});
        }

        Node::Node(Node* parent)
        {
            std::lock_guard<std::mutex> lockChild(_familyMutex);
            _uuid = NODE_UUID_GENERATOR();
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
            for(auto & i : _children)
            {
                delete i;
            }
        }

        Node* Node::getParent()
        {
            return _parent;
        }

        void Node::setParent(Node* parent)
        {
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
            return _children.size();
        }

        Node* Node::getChild(size_t index)
        {
            return _children.at(index);
        }

        Node* Node::addChild()
        {
            Node* child = new Node(this);
            return child;
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

        void Node::lockFamily()
        {
            _familyMutex.lock();
        }

        void Node::unlockFamily()
        {
            _familyMutex.unlock();
        }

        boost::uuids::uuid Node::uuid()
        {
            return _uuid;
        }


    } // core
} // crucible