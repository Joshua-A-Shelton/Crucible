#ifndef CRUCIBLE_MODEL_H
#define CRUCIBLE_MODEL_H
#include <filesystem>
#include "Mesh.h"
namespace crucible
{
    namespace core
    {

    class Model
    {
    public:
        Model(const std::filesystem::path& path);
        ~Model();
        Model(const Model&)=delete;
        Model& operator=(const Model&)=delete;
        Model(Model&& from);
        Model& operator=(Model&& from);
        size_t meshCount();
        Mesh& mesh(size_t i);

    private:
        void move(Model& from);
        std::vector<Mesh> _meshes;
    };

    } // core
} // crucible

#endif //CRUCIBLE_MODEL_H
