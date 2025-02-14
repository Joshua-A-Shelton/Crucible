#include "Node.h"
#include <crucible/scripting/ScriptingEngine.h>
#include <cassert>

namespace crucible
{
    namespace core
    {
        Node* Node::newNode(Node* parent)
        {
            return new Node(parent);
        }

        Node::Node(Node* parent)
        {
            if(parent != nullptr)
            {
                parent->_children.push_back(this);
                _parent = parent;
            }
            _entity = createEntity();
        }

        Node::~Node()
        {
            if(destroyEntity== nullptr)
            {
                auto gameWorldType = scripting::ScriptingEngine::getManagedType("Crucible.Core.GameWorld");

            }
            destroyEntity(_entity);
        }
    } // core
} // crucible