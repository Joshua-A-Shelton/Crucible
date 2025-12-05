#ifndef CRUCIBLE_BONEWEIGHTS_H
#define CRUCIBLE_BONEWEIGHTS_H
#include <cstdint>

namespace crucible
{
    struct BoneWeight
    {
        uint32_t index;
        float weight;
    };
    struct BoneWeights
    {
    public:
        BoneWeight weights[4] {{0,0}};
    };
} // crucible

#endif //CRUCIBLE_BONEWEIGHTS_H
