#ifndef CRUCIBLE_NODE_H
#define CRUCIBLE_NODE_H
#include <mutex>
#include <vector>

#include <boost/uuid/uuid.hpp>
#include <flecs.h>
#include <string>
#include <crucible/CrucibleCore.h>

namespace crucible
{
    /**
     * Represents a node in a scene graph. Child nodes memory are managed (and must only be managed) by their parents. If it has no parent, it is *your* responsibility to clean up after it (call delete)
     */
    class CRUCIBLE_API Node
    {
    public:
        ///Create a root node of a scene graph
        Node();
        /**
         * Create a node in a scene graph with a given parent
         * @param parent parent node for this one, or nullptr if this is a root node
         */
        explicit Node(Node* parent);
        Node(const Node&) = delete;
        Node& operator=(const Node&) = delete;
        Node(Node&&) = delete;
        Node& operator=(Node&&) = delete;
        ~Node();
        ///Gets this node's parent node, or nullptr if it's a root node
        Node* getParent() const;
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
         * Remove child from this node (deletes node and cleans up all descendant nodes)
         * @param index
         */
        void killChild(size_t index);
        /**
         * Remove child from this node (deletes node and cleans up all descendant nodes)
         * @param child
         */
        void killChildByReference(Node* child);
        /**
         * Remove a child from this node, and return it to the user
         * @param index
         * @return
         */
        Node* removeChild(size_t index);
        /**
          * Remove a child from this node, and return it to the user
          * @param child
          * @return removed child, or null if no child was removed
          */
        Node* removeChildByReference(Node* child);
        ///Gets the UUID that uniquely represents this node
        boost::uuids::uuid uuid();
        ///Gets the Entity key for the ECS system
        flecs::entity entity() const;
        const std::string& name();
        void setName(const std::string& name);

        bool isEnabled() const;
        void enable(bool propagate = true) const;
        void disable(bool propagate = true) const;

        static Node* getNodeByID(const boost::uuids::uuid& id);

    private:
        boost::uuids::uuid _uuid;
        flecs::entity _entity;
        std::string _name;
        Node* _parent = nullptr;
        std::vector<Node*> _children;
        std::mutex _familyMutex;
    };

    class NodeECSReference
    {
    public:
        Node* node = nullptr;
        static ecs_entity_t NodeEcsReferenceID();
    };
} // crucible

#endif //CRUCIBLE_NODE_H
