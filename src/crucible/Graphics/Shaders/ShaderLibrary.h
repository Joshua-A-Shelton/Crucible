#ifndef CRUCIBLEEDITOR_SHADERLIBRARY_H
#define CRUCIBLEEDITOR_SHADERLIBRARY_H

#include <unordered_map>
#include "MaterialShader.h"

namespace crucible
{

    class ShaderLibrary
    {
    public:
        static MaterialShader* getMaterialShader(const std::string& name);
        static MaterialShader* loadMaterialShader(const std::string& path, const std::string& name="");
        static MaterialShader* loadMaterialShader(const std::string& vert, const std::string& frag, const std::string& name);
        static void unloadShaders();
    private:
        static inline std::unordered_map<std::string,MaterialShader> _materialShaders;
    };

} // crucible

#endif //CRUCIBLEEDITOR_SHADERLIBRARY_H
