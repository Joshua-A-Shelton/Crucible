#ifndef CRUCIBLE_MODEL_H
#define CRUCIBLE_MODEL_H
#include "../Crucible.h"
#include "Mesh.h"
#include <vector>

namespace crucible
{
    class CRUCIBLE_EXPORT Model
    {
    public:
    private:
        std::vector<Mesh> meshes;
    };
} // crucible
#endif //CRUCIBLE_MODEL_H