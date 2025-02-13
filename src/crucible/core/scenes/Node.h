#ifndef CRUCIBLE_NODE_H
#define CRUCIBLE_NODE_H
#include <vector>
#include <crucible/core/interop/CSharpEntity.h>
namespace crucible
{
    namespace core
    {
        ///Represents a node in the scene graph
        class Node
        {
        public:
            static Node* newNode(Node* parent);
            Node(const Node&)=delete;
            Node& operator=(const Node&)=delete;
            Node(Node&&)=delete;
            Node& operator=(Node&&)=delete;
            ~Node();

        private:
            Node(Node* parent);
            inline static CSharpEntity (*createEntity)()= nullptr;
            inline static void (*destroyEntity)(CSharpEntity entity)= nullptr;
            CSharpEntity _entity;
            Node* _parent;
            std::vector<Node*> _children;
        };

    } // core
} // crucible

#endif //CRUCIBLE_NODE_H
