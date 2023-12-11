#ifndef CRUCIBLE_RENDERREGISTRY_H
#define CRUCIBLE_RENDERREGISTRY_H
#include <slag/Shader.h>
#include "Mesh.h"
namespace crucible
{

    class RenderRegistry
    {
    public:
        void registerDraw(slag::Shader* shader, Mesh* mesh);
        void draw(slag::CommandBuffer* commandBuffer);

    private:
        std::unordered_map<slag::Shader*,std::vector<Mesh*>> _meshMap;
        int renderThreads = 4;
    };

} // crucible

#endif //CRUCIBLE_RENDERREGISTRY_H
