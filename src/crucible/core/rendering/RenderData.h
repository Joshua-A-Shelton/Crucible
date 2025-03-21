#ifndef CRUCIBLE_RENDERDATA_H
#define CRUCIBLE_RENDERDATA_H
#include "crucible/core/Mesh.h"

namespace crucible
{
    namespace core
    {
        ///Represents a draw command, does not own memory
        class RenderData
        {
        public:
            RenderData(Mesh* mesh, slag::ShaderPipeline* shader, slag::DescriptorBundle&& bundle);
            ~RenderData()=default;
            RenderData(const RenderData&) = delete;
            RenderData& operator=(const RenderData&) = delete;
            RenderData(RenderData&& from);
            RenderData& operator=(RenderData&& from);
            void draw(slag::CommandBuffer* commandBuffer, const std::vector<Mesh::VertexAttribute>& attributes);

        private:
            void move(RenderData& from);
            Mesh* _mesh = nullptr;
            slag::ShaderPipeline* _shader = nullptr;
            slag::DescriptorBundle _instanceData;
        };
    } // core
} // crucible

#endif //CRUCIBLE_RENDERDATA_H
