#ifndef CRUCIBLE_NODE_H
#define CRUCIBLE_NODE_H
#include <vector>
#include <mutex>
#include <boost/uuid/uuid.hpp>
#include <crucible/core/Transform.h>
#include <crucible/scripting/ManagedInstance.h>
#include <crucible/scripting/ManagedType.h>
#include <flecs.h>

#include <slag/CommandBuffer.h>

#include "crucible/core/VirtualUniformBuffer.h"

namespace crucible
{
    namespace scripting
    {
        class ScriptingEngine;
    }

    namespace core
    {
        /**
         * Represents a node in a scene graph. Child nodes memory are managed (and must only be managed) by their parents. If it has no parent, it is *your* responsibility to clean up after it (call delete)
         */
        class Node
        {
        public:
            ///Create a root node of a scene graph
            Node();
            /**
             * Create a node in a scene graph with a given parent
             * @param parent parent node for this one, or nullptr if this is a root node
             */
            explicit Node(Node* parent);
            explicit Node(const scripting::ManagedType& scriptType);
            Node(const Node&)=delete;
            Node& operator=(const Node&)=delete;
            Node(Node&&)=delete;
            Node& operator=(Node&&)=delete;
            ~Node();
            ///Gets this node's parent node, or nullptr if it's a root node
            Node* getParent();
            /**
             * Sets this node's parent/child relationship
             * @param parent the node's new parent node
             */
            void setParent(Node* parent);
            ///The number of children this node has
            size_t childrenCount();
            /**
             * get child node by index
             * @param index
             * @return
             */
            Node* getChild(size_t index);
            ///Add new child to this node
            Node* addChild();
            /**
             * Remove child from this node (cleans up all descendent nodes)
             * @param index
             */
            void killChild(size_t index);
            void killChildByReference(Node* child);
            ///Locks family mutex (makes adding/removing children block until unlocked). Needed *only* in scripting side, don't use in c++
            void lockFamily();
            ///Unlocks family mutex. Needed *only* in scripting side, don't use in c++
            void unlockFamily();
            ///Gets the UUID that uniquely represents this node
            boost::uuids::uuid uuid();
            ///Gets the instance of the script attached to this node (instance can have null gc handle, indicating no script)
            scripting::ManagedInstance& script();
            ///Gets the Entity key for the ECS system
            flecs::entity entity()const;
            const std::string& name();
            void setName(const std::string& name);

            void updateNode(double deltaTime);

            void registerDraw(slag::DescriptorPool* descriptorPool, VirtualUniformBuffer* virtualUniformBuffer, Transform* parentTransform, ecs_entity_t& transformType,ecs_entity_t& meshRendererType);

            static Node* getNodeByID(const boost::uuids::uuid& id);
            friend class crucible::scripting::ScriptingEngine;

        private:
            inline static void (*update)(void* scriptHandle,double deltaTime)=nullptr;
            boost::uuids::uuid _uuid;
            flecs::entity _entity;
            std::string _name;
            Node* _parent = nullptr;
            std::vector<Node*> _children;
            std::mutex _familyMutex;
            scripting::ManagedInstance _script;

        };

        class NodeECSReference
        {
        public:
            Node* node=nullptr;
            static ecs_entity_t NodeEcsReferenceID();
        };

    } // core
} // crucible

#endif //CRUCIBLE_NODE_H
