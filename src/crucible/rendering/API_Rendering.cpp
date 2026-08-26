#include "API_Rendering.h"

#include <format>
#include <iostream>
#include <ostream>
#include <queue>
#include <crucible/Crucible.h>

#include "exceptions/ExceededHeapMemoryException.h"
struct CRUCIBLE_TEXTURE_INDICES
{
    uint32_t sampledIndex = UINT32_MAX;
    std::vector<uint32_t> readWriteIndices;
};

#ifndef NDEBUG
void graphicsDebug(const std::string& message, slag::DebugLevel level, int messageID)
{
    std::cout << message << std::endl;
}
#endif

slag::GraphicsCard* CRUCIBLE_GRAPHICS_CARD = nullptr;
slag::ResourceDescriptorHeap* CRUCIBLE_RESOURCE_DESCRIPTOR_HEAP = nullptr;
uint32_t CRUCIBLE_NEXT_RESOURCE_INDEX = 0;
std::queue<uint32_t> CRUCIBLE_FREED_RESOURCE_INDICES;
slag::SamplerDescriptorHeap* CRUCIBLE_SAMPLER_DESCRIPTOR_HEAP = nullptr;
uint32_t CRUCIBLE_NEXT_SAMPLER_INDEX = 0;
std::queue<uint32_t> CRUCIBLE_FREED_SAMPLER_INDICES;
uint32_t CRUCIBLE_RESOURCE_DESCRIPTOR_SIZE = 0;
uint32_t CRUCIBLE_SAMPLER_DESCRIPTOR_SIZE = 0;

crucible::CrucibleInitializationResult crucible::rendering::initializeRenderingSubmodule()
{
    slag::InitializationData data{.backend = slag::BackendAPI::VULKAN};
#ifndef NDEBUG
    data.debugHandler = graphicsDebug;
#endif

    auto result = slag::Slag::initialize(data);
    if (result != slag::SlagInitializationResult::SUCCESS)
    {
        return crucible::CrucibleInitializationResult::UNABLE_TO_INIT_GRAPHICS_BACKEND;
    }
    //TODO: actually pick a graphics card intelligently
    CRUCIBLE_GRAPHICS_CARD = slag::Slag::backend()->graphicsCard(0);

    auto properties = CRUCIBLE_GRAPHICS_CARD->descriptorHeapDetails();
    CRUCIBLE_RESOURCE_DESCRIPTOR_HEAP = CRUCIBLE_GRAPHICS_CARD->newResourceDescriptorHeap(properties.maxResourceDescriptors);
    CRUCIBLE_SAMPLER_DESCRIPTOR_HEAP = CRUCIBLE_GRAPHICS_CARD->newSamplerDescriptorHeap(properties.maxSamplerDescriptors);

    auto heapProperties = CRUCIBLE_GRAPHICS_CARD->descriptorHeapDetails();
    CRUCIBLE_RESOURCE_DESCRIPTOR_SIZE = std::max(heapProperties.samplerDescriptorSize, heapProperties.samplerDescriptorSize);
    CRUCIBLE_SAMPLER_DESCRIPTOR_SIZE = heapProperties.samplerDescriptorSize;

    return crucible::CrucibleInitializationResult::SUCCESS;
}

void crucible::rendering::cleanupRenderingSubmodule()
{
    delete CRUCIBLE_RESOURCE_DESCRIPTOR_HEAP;
    delete CRUCIBLE_SAMPLER_DESCRIPTOR_HEAP;
    CRUCIBLE_RESOURCE_DESCRIPTOR_HEAP = nullptr;
    CRUCIBLE_SAMPLER_DESCRIPTOR_HEAP = nullptr;
    CRUCIBLE_GRAPHICS_CARD = nullptr;
    slag::Slag::cleanup();
}

slag::GraphicsCard* crucible::rendering::getGraphicsCard()
{
    return CRUCIBLE_GRAPHICS_CARD;
}

//API Calls

crucible::rendering::Window* CRUCIBLE_API_createWindow(const char* name, uint32_t width, uint32_t height,
    crucible::rendering::WindowDecorationMode decorationMode, crucible::rendering::WindowTransparency transparency)
{
    return new crucible::rendering::Window(name, width, height, decorationMode, transparency);
}

void CRUCIBLE_API_destroyWindow(crucible::rendering::Window* window)
{
    delete window;
}

void CRUCIBLE_API_showWindow(crucible::rendering::Window* window)
{
    window->show();
}


void CRUCIBLE_API_setWindowPresentMode(crucible::rendering::CRUCIBLE_WINDOW_PRESENT_MODE mode)
{
    crucible::rendering::Window::updateWindowPresentMode(mode);
}

crucible::rendering::CRUCIBLE_WINDOW_PRESENT_MODE CRUCIBLE_API_getWindowPresentMode()
{
    return crucible::rendering::Window::getWindowPresentMode();
}