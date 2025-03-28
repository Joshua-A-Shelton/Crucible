#include "ModelLoader.h"

#include <fstream>

namespace crucible
{
    namespace core
    {
        ModelLoader::ModelLoader(const std::filesystem::path& file)
        {
            std::ifstream shaderFile(file,std::ios::in | std::ios::binary);
            if (!shaderFile.is_open())
            {
                throw std::runtime_error("Unable to open "+file.string());
            }

            shaderFile.seekg(0,std::ios::end);
            size_t size = shaderFile.tellg();
            shaderFile.seekg(0,std::ios::beg);
            _modelData.resize(size);
            shaderFile.read(std::bit_cast<char*>(_modelData.data()), size);
            shaderFile.close();

            _currentByte = &_modelData[0];

            auto magicNumber = std::string(reinterpret_cast<char*>(_currentByte),7);
            _currentByte+=7;

            if (magicNumber!="cmodel\n")
            {
                throw std::runtime_error(file.string()+" is not a valid model file");
            }


            _meshCount=*(uint32_t*)_currentByte;
            _currentByte+=sizeof(uint32_t);
        }

        Mesh* ModelLoader::nextMesh()
        {
            uint64_t dataSize = *(uint64_t*)_currentByte;
            _currentByte+=sizeof(uint64_t);
            auto mesh = new Mesh(_currentByte,dataSize);
            _currentByte+=dataSize;
            _readMeshes++;
            return mesh;
        }

        bool ModelLoader::isAtEnd() const
        {
            return  _readMeshes == _meshCount;
        }

        uint32_t ModelLoader::meshCount() const
        {
            return _meshCount;
        }
    } // core
} // crucible
