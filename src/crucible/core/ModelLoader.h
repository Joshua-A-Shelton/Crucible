#ifndef CRUCIBLE_MODELLOADER_H
#define CRUCIBLE_MODELLOADER_H
#include <filesystem>

#include "Mesh.h"

namespace crucible
{
    namespace core
    {
        class ModelLoader
        {
        public:
            ModelLoader(const std::filesystem::path& file);
            ~ModelLoader()=default;
            ModelLoader(const ModelLoader&) = delete;
            ModelLoader& operator=(const ModelLoader&) = delete;
            ModelLoader(ModelLoader&& from);
            ModelLoader& operator=(ModelLoader&& from);

            /**
             * Gets the next mesh in the file (constructed on the heap, it becomes caller's responsibility to clean it up)
             * @return
             */
            Mesh* nextMesh();
            ///If we're done reading meshes
            bool isAtEnd() const;

            uint32_t meshCount() const;
        private:
            void move(ModelLoader& from);
            std::vector<unsigned char> _modelData;
            unsigned char* _currentByte = nullptr;
            uint32_t _meshCount = 0;
            uint32_t _readMeshes = 0;

        };
    } // core
} // crucible

#endif //CRUCIBLE_MODELLOADER_H
