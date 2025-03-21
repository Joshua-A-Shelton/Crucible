#include "Model.h"

#include <fstream>

namespace crucible
{
    namespace core
    {
        Model::Model(const std::filesystem::path& path)
        {
            std::ifstream shaderFile(path,std::ios::in | std::ios::binary);
            if (!shaderFile.is_open())
            {
                throw std::runtime_error("Unable to open "+path.string());
            }

            shaderFile.seekg(0,std::ios::end);
            size_t size = shaderFile.tellg();
            shaderFile.seekg(0,std::ios::beg);
            std::vector<unsigned char> data(size);
            shaderFile.read(std::bit_cast<char*>(data.data()), size);
            shaderFile.close();

            unsigned char* bytePointer = &data[0];

            auto magicNumber = std::string(reinterpret_cast<char*>(bytePointer),7);
            bytePointer+=7;

            if (magicNumber!="cmodel\n")
            {
                throw std::runtime_error(path.string()+" is not a valid model file");
            }

            uint32_t meshCount = 0;
            meshCount=*(uint32_t*)bytePointer;
            bytePointer+=sizeof(uint32_t);

            _meshes.reserve(meshCount);

            for (uint32_t i=0;i<meshCount;i++)
            {
                uint64_t dataSize = *(uint64_t*)bytePointer;
                bytePointer+=sizeof(uint64_t);
                _meshes.emplace_back(bytePointer,dataSize);
                bytePointer+=dataSize;
            }
        }

        Model::~Model()
        {
        }

        Model::Model(Model&& from)
        {
            move(from);
        }

        Model& Model::operator=(Model&& from)
        {
            move(from);
            return *this;
        }

        size_t Model::meshCount()
        {
            return _meshes.size();
        }

        Mesh& Model::mesh(size_t i)
        {
            return _meshes[i];
        }

        void Model::move(Model& from)
        {
            _meshes.swap(from._meshes);
        }
    } // core
} // crucible