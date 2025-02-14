#ifndef CRUCIBLE_NODE_H
#define CRUCIBLE_NODE_H
#include <vector>
#include <mutex>
#include <crucible/core/interop/CSharpEntity.h>


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
            friend class crucible::scripting::ScriptingEngine;

        private:

            inline static CSharpEntity (*createEntity)()= nullptr;
            inline static void (*destroyEntity)(CSharpEntity entity)= nullptr;
            CSharpEntity _entity;
            Node* _parent = nullptr;
            std::vector<Node*> _children;
            std::mutex _familyMutex;

        };

    } // core
} // crucible

#endif //CRUCIBLE_NODE_H
