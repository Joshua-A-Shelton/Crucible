#include "Node.h"
#include <boost/uuid/uuid_generators.hpp>

namespace crucible
{

    boost::uuids::random_generator NODE_UUID_GENERATOR;

    Node::Node()
    {
        _id = NODE_UUID_GENERATOR();
    }

    Node::~Node()
    {
        std::remove(_parent->_children.begin(), _parent->_children.end(),this);
        for(Node* child: _children)
        {
            delete child;
        }
    }

    Node::Node(Node &&other)
    {
        move(std::move(other));
    }

    Node &Node::operator=(Node &&other)
    {
        move(std::move(other));
        return *this;
    }

    Node *Node::parent()
    {
        return _parent;
    }

    void Node::setParent(Node *node)
    {

        if(_parent)
        {
            std::remove(_parent->_children.begin(), _parent->_children.end(),this);
        }
        _parent = node;
        if(_parent)
        {
            _parent->_children.push_back(this);
        }
    }

    void Node::addChild(Node *node)
    {
        node->setParent(this);
    }

    void Node::removeChild(Node *node)
    {
        for(auto i=0; i< _children.size(); i++)
        {
            Node* comp = _children[i];
            if(comp == node)
            {
                removeChild(i);
                return;
            }
        }
    }

    void Node::removeChild(size_t childIndex)
    {
        Node* child = _children[childIndex];
        _children.erase(_children.begin()+childIndex);
        delete child;
    }

    Node *Node::getChild(size_t childIndex) const
    {
        return _children[childIndex];
    }

    size_t Node::childrenCount()const
    {
        return _children.size();
    }

    boost::uuids::uuid Node::id() const
    {
        return _id;
    }
} // crucible