#include "Vertex.h"

namespace crucible
{
    std::vector<VkVertexInputBindingDescription> vertexbindingsdescription { {0,sizeof(Vertex),VK_VERTEX_INPUT_RATE_VERTEX}};
    std::vector<VkVertexInputAttributeDescription> vertexattributesdescription
    {
            {0,0,VK_FORMAT_R32G32B32_SFLOAT, offsetof(Vertex,position)},
            {1,0,VK_FORMAT_R32G32B32_SFLOAT,offsetof(Vertex,normal)},
            {2,0,VK_FORMAT_R32G32B32_SFLOAT, offsetof(Vertex,color)},
            {3,0,VK_FORMAT_R32G32_SFLOAT, offsetof(Vertex,uv)}
    };
    slag::VertexDescription vertexDescription(vertexbindingsdescription,vertexattributesdescription);

    slag::VertexDescription& Vertex::description()
    {
        return vertexDescription;
    }
} // crucible