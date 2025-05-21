#include "Node.h"
#include <crucible/scripting/ScriptingEngine.h>
#include <cassert>
#include <boost/uuid/random_generator.hpp>
#include <boost/functional/hash.hpp>
#include <unordered_map>
#include <crucible/core/scenes/World.h>

#include "crucible/core/ECSInterop.h"
#include "crucible/core/Material.h"
#include "crucible/core/animation/Skeleton.h"

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
            _entity = World::ECS.entity();
            auto crucibleNodeRefType = NodeECSReference::NodeEcsReferenceID();
            NodeECSReference reference{.node = this};
            ecs_add_id(core::World::ECS.world_,entity(),crucibleNodeRefType);
            ecs_set_id(core::World::ECS.world_,entity(),crucibleNodeRefType,sizeof(NodeECSReference),&reference);

            std::lock_guard<std::mutex> lockMap(NODE_MAP_LOCK);
            NODE_UUID_MAP.insert({_uuid,this});

        }

        Node::Node(Node* parent)
        {
            std::lock_guard<std::mutex> lockChild(_familyMutex);
            _uuid = NODE_UUID_GENERATOR();
            _entity = World::ECS.entity();
            auto crucibleNodeRefType = NodeECSReference::NodeEcsReferenceID();
            NodeECSReference reference{.node = this};
            ecs_add_id(core::World::ECS.world_,entity(),crucibleNodeRefType);
            ecs_set_id(core::World::ECS.world_,entity(),crucibleNodeRefType,sizeof(NodeECSReference),&reference);
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

        Node::Node(const scripting::ManagedType& scriptType):Node()
        {
            _script = scriptType.createNew();
        }

        Node::~Node()
        {
            std::lock_guard<std::mutex> lock(_familyMutex);
            {
                std::lock_guard<std::mutex> lockMap(NODE_MAP_LOCK);
                NODE_UUID_MAP.erase(_uuid);
            }
            _entity.destruct();
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

        ecs_entity_t NodeECSReference::NodeEcsReferenceID()
        {
            return ECSInterop::types().Node;
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

        scripting::ManagedInstance& Node::script()
        {
            return _script;
        }

        flecs::entity Node::entity() const
        {
            return _entity;
        }

        const std::string& Node::name()
        {
            return _name;
        }

        void Node::setName(const std::string& name)
        {
            _name = name;
        }

        void Node::updateNode(double deltaTime)
        {
            if (!_script.isNull())
            {
                update(_script.gcHandle(), deltaTime);
            }
            //the update may move nodes around, make sure that we're updating them separately from the family hierarchy
            std::vector<Node*> kids = _children;
            //TODO: I need to update once, and only once, regardless of moving the children around... maybe allow killing, and creating nodes, but not moving them?
            for (size_t i=0; i<kids.size(); i++)
            {
                kids[i]->updateNode(deltaTime);
            }
        }

        void Node::registerDraw(slag::DescriptorPool* descriptorPool, VirtualBuffer* virtualUniformBuffer, VirtualBuffer* virtualStorageBuffer, Transform* parentTransform)
        {
            auto nodeTransform = *parentTransform;
            auto transformType = ECSInterop::types().Transform;
            if (_entity.has(transformType))
            {
                Transform* local = (Transform*)_entity.get(transformType);
                nodeTransform = (*local)+(*parentTransform);
            }
            auto meshRendererType = ECSInterop::types().MeshRenderer;
            if (_entity.has(meshRendererType))
            {
                scripting::ManagedInstance* rendererManaged = (scripting::ManagedInstance*)_entity.get(meshRendererType);
                Mesh* mesh = nullptr;
                Material* material = nullptr;
                uint8_t priority = 0;
                scripting::ScriptingEngine::getMeshRenderData(*rendererManaged,&mesh,&material,&priority);

                //set position data
                auto& shader = material->shaderReference();
                auto materialBundle = material->makeBundle(descriptorPool, virtualUniformBuffer);

                auto instanceBundle = descriptorPool->makeBundle(shader.pipeline()->descriptorGroup(3));
                //set position data, always at position 0
                auto matr = nodeTransform.matrix();
                auto writeLocation = virtualUniformBuffer->write(&matr,sizeof(matr));
                instanceBundle.setUniformBuffer(0,0,writeLocation.buffer,writeLocation.offset,writeLocation.length);
                //set bone data if it's required, at position 1
                if(shader.hasAttribute(Mesh::VertexAttribute::BONE_WEIGHT))
                {
                    auto skeletonType = ECSInterop::types().Skeleton;
                    if (_entity.has(skeletonType))
                    {
                        scripting::ManagedInstance* skeletonManaged = (scripting::ManagedInstance*)_entity.get(skeletonType);
                        auto skeleton = scripting::ScriptingEngine::getSkeleton(*skeletonManaged);
                        auto& boneMatrixes = skeleton->shaderTransforms();

                        auto skeletalWriteLocation = virtualStorageBuffer->write(boneMatrixes.data(),boneMatrixes.size()*sizeof(glm::mat4));
                        instanceBundle.setStorageBuffer(1,0,skeletalWriteLocation.buffer,skeletalWriteLocation.offset,skeletalWriteLocation.length);
                    }
                    else
                    {
                        throw std::runtime_error("Skeleton required for drawing, none present");
                    }
                }

                World::MeshDrawPass->registerMeshData(priority,shader,mesh,std::move(materialBundle),std::move(instanceBundle));

            }

            for(auto& child : _children)
            {
                child->registerDraw(descriptorPool, virtualUniformBuffer, virtualStorageBuffer, &nodeTransform);
            }

        }
    } // core
} // crucible