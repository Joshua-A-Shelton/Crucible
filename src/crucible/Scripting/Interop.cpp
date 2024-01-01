#include "Interop.h"
#include <glm/geometric.hpp>
#include <glm/gtc/matrix_transform.hpp>
#include <glm/gtx/quaternion.hpp>

namespace crucible
{
    //Matrices
    void cs_MatrixMatrixMultiply(glm::mat4 &m1, glm::mat4 &m2, glm::mat4 &result)
    {
        result = m1*m2;
    }

    void cs_MatrixFromTRS(glm::vec3 &translation, glm::quat &rotation, glm::vec3 &scale, glm::mat4 &result)
    {
        glm::translate(result,translation);
        result*=glm::toMat4(rotation);
        glm::scale(result,scale);
    }

    //Meshes
    Mesh *cs_CreateMesh()
    {
        std::vector<Vertex3D> verts
                {
                        Vertex3D{.position{-1,1,0},.normal{0,0,1},.uv{0,0}},
                        Vertex3D{.position{1,1,0},.normal{0,0,1},.uv{0,1}},
                        Vertex3D{.position{-1,-1,0},.normal{0,0,1},.uv{1,0}},
                        Vertex3D{.position{1,-1,0},.normal{0,0,1},.uv{1,1}},
                };
        std::vector<uint16_t> indecies
                {
                        0,1,2,1,3,2
                };
        return new Mesh(verts,indecies);
    }

    void cs_DeleteMesh(Mesh *mesh)
    {
        delete mesh;
    }

    //TODO: make backing structure selectable, we're currently using R32G32B32A32_SFLOAT to match colors vector
    slag::Texture *cs_CreateColorTexture(int width, int height, glm::vec4 defaultColor, int mipLevels, bool renderTargetable)
    {
        std::vector<glm::vec4> colors(width*height,defaultColor);
        return slag::Texture::create(width,height,slag::Pixels::R32G32B32A32_SFLOAT,colors.data(),mipLevels, renderTargetable);
    }

    slag::Texture *cs_CreateDepthTexture(int width, int height)
    {
        std::vector<float> colors(width*height,0);
        return slag::Texture::create(width,height,slag::Pixels::D32_SFLOAT,colors.data(),1, true);
    }

    void cs_DeleteTexture(slag::Texture *texture)
    {
        delete texture;
    }

    int cs_TextureWidth(slag::Texture *texture)
    {
        return texture->width();
    }

    int cs_TextureHeight(slag::Texture *texture)
    {
        return texture->height();
    }

    //Textures

}
