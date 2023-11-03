#define SDL_MAIN_HANDLED
#include <crucible/Engine.h>
#include <slag/SlagLib.h>
#include <slag/BackEnd/Vulkan/VulkanCommandBuffer.h>
#include <slag/BackEnd/Vulkan/VulkanLib.h>
#include <SDL.h>
#include <SDL_syswm.h>
#include "imgui.h"
#include "backends/imgui_impl_sdl2.h"
#include "backends/imgui_impl_vulkan.h"
#include <vulkan/vulkan.h>


struct PoolSizes {
    std::vector<std::pair<VkDescriptorType,float>> sizes =
            {
                    { VK_DESCRIPTOR_TYPE_SAMPLER, 0.5f },
                    { VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER, 4.f },
                    { VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 4.f },
                    { VK_DESCRIPTOR_TYPE_STORAGE_IMAGE, 1.f },
                    { VK_DESCRIPTOR_TYPE_UNIFORM_TEXEL_BUFFER, 1.f },
                    { VK_DESCRIPTOR_TYPE_STORAGE_TEXEL_BUFFER, 1.f },
                    { VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER, 2.f },
                    { VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, 2.f },
                    { VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC, 1.f },
                    { VK_DESCRIPTOR_TYPE_STORAGE_BUFFER_DYNAMIC, 1.f },
                    { VK_DESCRIPTOR_TYPE_INPUT_ATTACHMENT, 0.5f }
            };
};

int main(int argc, char** args)
{
    crucible::Engine::initialize();
    auto mainWindow = SDL_CreateWindow("Crucible",SDL_WINDOWPOS_UNDEFINED,SDL_WINDOWPOS_UNDEFINED,800,500,SDL_WindowFlags::SDL_WINDOW_VULKAN | SDL_WindowFlags::SDL_WINDOW_RESIZABLE);
    bool open = true;
    slag::PlatformData pd;
    SDL_SysWMinfo wmInfo;
    SDL_VERSION(&wmInfo.version);
    SDL_GetWindowWMInfo(mainWindow, &wmInfo);
#ifdef _WIN32
    pd.nativeWindowHandle = wmInfo.info.win.window;
#elif __linux
    pd.nativeWindowHandle = reinterpret_cast<void*>(wmInfo.info.x11.window);
    pd.nativeDisplayType = wmInfo.info.x11.display;
#endif
    slag::Swapchain* swapchain = slag::SwapchainBuilder(pd).setDesiredBackBuffers(2).setHeight(500).setWidth(800).create();

    // Setup Dear ImGui context
    IMGUI_CHECKVERSION();
    ImGui::CreateContext();
    ImGuiIO& io = ImGui::GetIO();
    io.ConfigFlags |= ImGuiConfigFlags_NavEnableKeyboard;     // Enable Keyboard Controls
    io.ConfigFlags |= ImGuiConfigFlags_NavEnableGamepad;      // Enable Gamepad Controls
    io.ConfigFlags |= ImGuiConfigFlags_DockingEnable;         // IF using Docking Branch
    io.ConfigFlags |= ImGuiConfigFlags_ViewportsEnable;



    PoolSizes poolSizes;
    std::vector<VkDescriptorPoolSize> sizes;
    sizes.reserve(poolSizes.sizes.size());
    auto count = 1000;
    for (auto sz : poolSizes.sizes) {
        sizes.push_back({ sz.first, uint32_t(sz.second * count) });
    }
    VkDescriptorPoolCreateInfo pool_info = {};
    pool_info.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
    pool_info.flags = 0;
    pool_info.maxSets = count;
    pool_info.poolSizeCount = (uint32_t)sizes.size();
    pool_info.pPoolSizes = sizes.data();
    pool_info.flags = VK_DESCRIPTOR_POOL_CREATE_FREE_DESCRIPTOR_SET_BIT;

    VkDescriptorPool descriptorPool;
    vkCreateDescriptorPool(slag::vulkan::VulkanLib::graphicsCard()->device(), &pool_info, nullptr, &descriptorPool);


// Setup Platform/Renderer backends
    ImGui_ImplSDL2_InitForVulkan(mainWindow);
    ImGui_ImplVulkan_InitInfo init_info = {};
    init_info.Instance = slag::vulkan::VulkanLib::instance();
    init_info.PhysicalDevice = slag::vulkan::VulkanLib::graphicsCard()->physicalDevice();
    init_info.Device = slag::vulkan::VulkanLib::graphicsCard()->device();
    init_info.QueueFamily = slag::vulkan::VulkanLib::graphicsCard()->graphicsQueueFamily();
    init_info.Queue = slag::vulkan::VulkanLib::graphicsCard()->graphicsQueue();
    init_info.PipelineCache = nullptr;
    init_info.DescriptorPool = descriptorPool;
    init_info.Subpass = 0;
    init_info.MinImageCount = 2;
    init_info.ImageCount = 2;
    init_info.MSAASamples = VK_SAMPLE_COUNT_1_BIT;
    init_info.Allocator = nullptr;
    init_info.CheckVkResultFn = nullptr;
    init_info.UseDynamicRendering = true;
    init_info.ColorAttachmentFormat = VK_FORMAT_B8G8R8A8_UNORM;
    ImGui_ImplVulkan_Init(&init_info, nullptr);
// (this gets a bit more complicated, see example app for full reference)
    slag::vulkan::VulkanLib::graphicsCard()->runOneTimeCommands(slag::vulkan::VulkanLib::graphicsCard()->graphicsQueue(),slag::vulkan::VulkanLib::graphicsCard()->graphicsQueueFamily(),[=](VkCommandBuffer commandBuffer)
    {
        ImGui_ImplVulkan_CreateFontsTexture(commandBuffer);
    });
// (your code submit a queue)
    ImGui_ImplVulkan_DestroyFontUploadObjects();


    while(open)
    {
        SDL_Event e;

        // Events management
        while(SDL_PollEvent(&e))
        {
            if( e.type == SDL_QUIT )
            {
                open = false;
            }
            else if(e.type == SDL_WINDOWEVENT && e.window.event == SDL_WINDOWEVENT_CLOSE)
            {
                open = false;
            }
            else if(e.type == SDL_WINDOWEVENT && e.window.event == SDL_WINDOWEVENT_RESIZED)
            {
                int w,h;
                SDL_GetWindowSize(mainWindow,&w,&h);
                swapchain->resize(w,h);
            }
            ImGui_ImplSDL2_ProcessEvent(&e); // Forward your event to backend
        }
        if(auto frame = swapchain->next())
        {
            frame->begin();
            ImGui_ImplVulkan_NewFrame();
            ImGui_ImplSDL2_NewFrame();
            ImGui::NewFrame();

            auto* commandBuffer = frame->getCommandBuffer();
            ImGui::DockSpaceOverViewport(ImGui::GetMainViewport());
            ImGui::Begin("Scene View");
            ImGui::End();

            ImGui::Begin("Inspector");
            ImGui::End();

            ImGui::Begin("Scene Tree");
            ImGui::End();

            slag::Rectangle view{{0,0},{swapchain->width(),swapchain->height()}};
            slag::Attachment colorAttachment{.texture = frame->getBackBuffer(), .clearOnLoad = true, .clear={0.5,0.5,0.5,0.5}};
            commandBuffer->setTargetFramebuffer(view,&colorAttachment,1);
            ImGui::Render();
            ImGui_ImplVulkan_RenderDrawData(ImGui::GetDrawData(), static_cast<slag::vulkan::VulkanCommandBuffer*>(commandBuffer)->vulkanCommandBuffer());
            commandBuffer->endTargetFramebuffer();
            ImGui::UpdatePlatformWindows();
            ImGui::RenderPlatformWindowsDefault();
            frame->end();
        }
    }
    vkDeviceWaitIdle(slag::vulkan::VulkanLib::graphicsCard()->device());
    vkDestroyDescriptorPool(slag::vulkan::VulkanLib::graphicsCard()->device(),descriptorPool, nullptr);
    ImGui_ImplVulkan_Shutdown();
    ImGui_ImplSDL2_Shutdown();
    ImGui::DestroyContext();

    delete swapchain;
    SDL_DestroyWindow(mainWindow);

    crucible::Engine::cleanup();
}