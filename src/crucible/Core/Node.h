#ifndef CRUCIBLEEDITOR_NODE_H
#define CRUCIBLEEDITOR_NODE_H

#include <boost/uuid/uuid.hpp>
#include <vector>

namespace crucible
{

    class Node
    {
    public:
        Node();
        virtual ~Node();
        Node(const Node&) = delete;
        Node& operator=(const Node&)=delete;
        Node(Node&& other);
        Node& operator=(Node&& other);
        Node* parent();
        void setParent(Node* node);
        void addChild(Node* node);
        void removeChild(Node* node);
        void removeChild(size_t childIndex);
        Node* getChild(size_t childIndex)const;
        size_t childrenCount()const;
        boost::uuids::uuid id()const;
    private:
        boost::uuids::uuid _id;
        Node* _parent = nullptr;
        std::vector<Node*> _children;
        void move(Node&& other);
    };

} // crucible

#endif //CRUCIBLEEDITOR_NODE_H
