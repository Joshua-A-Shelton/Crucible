#ifndef CRUCIBLE_MODEL_H
#define CRUCIBLE_MODEL_H
#include "Mesh.h"
#include <vector>

namespace crucible
{
    class Model
    {
    public:
    private:
        std::vector<Mesh> meshes;
    };
} // crucible
#endif //CRUCIBLE_MODEL_H