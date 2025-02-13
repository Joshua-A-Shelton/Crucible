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
            if(createEntity == nullptr)
            {
                auto gameWorldType = scripting::ScriptingEngine::getManagedType("Crucible.Core.GameWorld");
                createEntity = gameWorldType.getFunction<CSharpEntity(*)()>("UnmanagedCreateEntity");
                assert(createEntity!= nullptr && "Unable to find Managed Function \"UnmanagedCreateEntity\"");
            }
            _entity = createEntity();
        }

        Node::~Node()
        {
            if(destroyEntity== nullptr)
            {
                auto gameWorldType = scripting::ScriptingEngine::getManagedType("Crucible.Core.GameWorld");
                destroyEntity = gameWorldType.getFunction<void(*)(CSharpEntity)>("UnmanagedFreeEntity");
                assert(destroyEntity!= nullptr && "Unable to find Managed Function \"UnmanagedFreeEntity\"");
            }
            destroyEntity(_entity);
        }
    } // core
} // crucible