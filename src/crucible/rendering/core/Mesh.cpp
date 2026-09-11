#include "Mesh.h"

#include <stdexcept>

#include "ResourceManager.h"
#include "crucible/rendering/API_Rendering.h"

namespace crucible
{
    namespace rendering
    {
        Mesh::Mesh(const VertexDataStreams& vertexStreams, VertexAttributeFlags cpuAccessibleAttributes)
        {
            auto card = getGraphicsCard();
            if (card->memoryProperties().cacheCoherentSharedMemory)
            {

            }
            else
            {

            }
            throw std::runtime_error("Not implemented");
        }

        Mesh::~Mesh()
        {
            if (_positionBuffer)
            {
                ResourceManager::queueDelete(_positionBuffer);
            }
            if (_normalBuffer)
            {
                ResourceManager::queueDelete(_normalBuffer);
            }
            if (_uvBuffer)
            {
                ResourceManager::queueDelete(_uvBuffer);
            }
            if (_tangentBuffer)
            {
                ResourceManager::queueDelete(_tangentBuffer);
            }
            if (_colorBuffer)
            {
                ResourceManager::queueDelete(_colorBuffer);
            }
            if (_boneWeightBuffer)
            {
                ResourceManager::queueDelete(_boneWeightBuffer);
            }
            if (_indexBuffer)
            {
                ResourceManager::queueDelete(_indexBuffer);
            }
        }
    } // rendering
} // crucible