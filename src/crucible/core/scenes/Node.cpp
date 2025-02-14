#include "Node.h"
#include <crucible/scripting/ScriptingEngine.h>
#include <cassert>

namespace crucible
{
    namespace core
    {
        Node::Node()
        {
            _entity = createEntity();
        }

        Node::Node(Node* parent)
        {
            std::lock_guard<std::mutex> lockChild(_familyMutex);
            if(parent != nullptr && parent != this)
            {
                std::lock_guard<std::mutex> lockParent(parent->_familyMutex);
                parent->_children.push_back(this);
                _parent = parent;
            }
            _entity = createEntity();
        }

        Node::~Node()
        {
            std::lock_guard<std::mutex> lock(_familyMutex);
            destroyEntity(_entity);
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
            //remove from old parent
            if(_parent!= nullptr)
            {
                std::lock_guard<std::mutex> lockparent(_parent->_familyMutex);
                _parent->_children.erase(std::find(_parent->_children.begin(), _parent->_children.end(), this));
            }
            _parent = parent;
            //add as child of new parent
            if(_parent)
            {
                std::lock_guard<std::mutex> lockparent(_parent->_familyMutex);
                parent->_children.push_back(this);
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


    } // core
} // crucible